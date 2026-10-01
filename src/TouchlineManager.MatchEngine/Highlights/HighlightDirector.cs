using System.Globalization;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns a simulated match into replayable highlights (master plan §9.2, §9.3, ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// Selection is a pure function of the event stream. Every goal is always shown — a manager who scores and
/// cannot watch it has been given a worse product than one whose post-and-in was omitted — and the rest of
/// the budget goes to the best chances, measured by the goal probability the shot was resolved against.
/// </para>
/// <para>
/// The two limits are a count and a payload budget, and they are applied in that order: a match with fifty
/// chances is trimmed to the most important ones, and then trimmed again if the result still exceeds what a
/// phone should download. Goals survive both, so the requirement "all goals always remain" holds even for a
/// nine-goal game on a bad connection.
/// </para>
/// <para>
/// Since `replay-v2`, a highlight is a whole passage of play (Stage 3): its 22 players move through the
/// passage with the coordinated shape the play model gave them — a defensive block dropping off, a
/// full-back overlapping — and the ball flies with the altitude the flight model computed, rather than
/// teleporting from the centre spot. Between consecutive highlights the director bridges the recycling
/// passage, and a payload-aware trim drops bridges before it ever drops a goal.
/// </para>
/// </remarks>
public static class HighlightDirector
{
    /// <summary>The version label of this presentation.</summary>
    public const string Version = "replay-v2";

    /// <summary>The pitch coordinate scale.</summary>
    private const int Pitch = 10_000;

    /// <summary>Builds the match's presentation.</summary>
    /// <param name="input">The frozen snapshot, which supplies the eleven and their positions.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="options">How much is worth showing.</param>
    public static MatchPresentationV1 Build(
        MatchInputV1 input,
        MatchResultV1 result,
        HighlightOptionsV1? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);

        var settings = options ?? new HighlightOptionsV1();
        var names = Names(input);

        var candidates = result.Events
            .OrderBy(matchEvent => matchEvent.Sequence)
            .Where(matchEvent => IsWorthShowing(matchEvent, settings))
            .ToList();

        var selected = Trim(candidates, settings);
        var colours = Colours(input);

        var built = selected
            .Select(matchEvent => (Event: matchEvent, Highlight: BuildHighlight(input, matchEvent, names, colours, settings)))
            .ToList();

        // A count cap is not a size cap: twenty-four highlights are small if they are twenty-four tap-ins and
        // large if they are twenty-four long-range efforts with a full set of tracks. The payload budget is
        // what actually protects a phone on a train, so it is applied after the count and it yields the
        // lowest-quality chances first — never a goal.
        while (built.Sum(pair => pair.Highlight.EstimatedPayloadBytes) > settings.PayloadBudgetBytes)
        {
            var least = built
                .Where(pair => !pair.Event.IsGoal)
                .OrderBy(pair => pair.Event.QualityBasisPoints ?? 0)
                .ThenByDescending(pair => pair.Event.Sequence)
                .FirstOrDefault();

            if (least.Highlight is null)
            {
                // Only goals are left. They stay, however large the payload, because a result a manager cannot
                // watch is a worse failure than a large download.
                break;
            }

            built.Remove(least);
        }

        var highlights = built.Select(pair => pair.Highlight).ToList();

        var bridges = BuildBridges(input, built, highlights, settings);

        return new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Highlights = highlights,
            Bridges = bridges,
        };
    }

    /// <summary>
    /// Builds the recycling passages between consecutive highlights (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// A bridge is fitted between a highlight and its successor only when both exist in the trimmed set and
    /// the pair's payload still fits the bridge budget; it is dropped otherwise, and the cut it would have
    /// smoothed remains a cut. No bridge precedes the first highlight or follows the last.
    /// </remarks>
    private static List<BridgeV1> BuildBridges(
        MatchInputV1 input,
        List<(EngineEventV1 Event, HighlightPresentationV1 Highlight)> built,
        List<HighlightPresentationV1> highlights,
        HighlightOptionsV1 settings)
    {
        if (built.Count < 2)
        {
            return [];
        }

        var bridges = new List<BridgeV1>();
        var names = Names(input);
        var budget = settings.BridgeBudgetBytes;

        for (var index = 0; index < built.Count - 1; index++)
        {
            var current = built[index];
            var next = built[index + 1];

            var bridge = BuildBridge(input, current, next, names, settings);

            if (bridge.EstimatedPayloadBytes > budget)
            {
                continue;
            }

            budget -= bridge.EstimatedPayloadBytes;
            bridges.Add(bridge);
        }

        return bridges;
    }

    private static BridgeV1 BuildBridge(
        MatchInputV1 input,
        (EngineEventV1 Event, HighlightPresentationV1 Highlight) current,
        (EngineEventV1 Event, HighlightPresentationV1 Highlight) next,
        Dictionary<Guid, string> names,
        HighlightOptionsV1 settings)
    {
        var duration = int.Clamp(
            (next.Event.Minute - current.Event.Minute) * 500,
            settings.MinBridgeDurationMilliseconds,
            settings.MaxBridgeDurationMilliseconds);

        // The bridge runs from where this passage ended to where the next begins: the ball returns towards
        // the middle third and both teams shift with it, so the cut reads as recycling rather than teleport.
        var from = BallAnchor(current.Event, attackingSide: current.Event.Side);
        var to = BallAnchor(next.Event, attackingSide: next.Event.Side);

        var tracks = new List<HighlightTrackV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var isHome = side == MatchSide.Home;

            foreach (var slot in input.SideOf(side).Slots.OrderBy(slot => slot.SlotNumber))
            {
                var entityId = EntityId(isHome, slot.SlotNumber);

                // The formation anchor the block returns to in a recycling passage, with a small shift of
                // the block towards the end of the pitch the next passage will be fought in.
                var anchor = FormationAnchor(slot, isHome);
                var drift = (to.X - anchor.X) / 8;

                tracks.Add(new HighlightTrackV1(entityId, new[]
                {
                    new HighlightKeyframeV1(0, Clamp(anchor.X), Clamp(anchor.Y)),
                    new HighlightKeyframeV1(duration / 2, Clamp(anchor.X + (drift / 2)), Clamp(anchor.Y + ((to.Y - anchor.Y) / 10))),
                    new HighlightKeyframeV1(duration, Clamp(anchor.X + drift), Clamp(anchor.Y + ((to.Y - anchor.Y) / 5))),
                }));
            }
        }

        // The ball crosses with the recycling pass: out to the middle third, then onwards to where the next
        // passage begins, at ground level.
        var midway = (from.X + to.X) / 2;

        tracks.Add(new HighlightTrackV1(
            BallEntityId,
            new[]
            {
                new HighlightKeyframeV1(0, Clamp(from.X), Clamp(from.Y)),
                new HighlightKeyframeV1(duration / 2, Clamp(midway), Clamp((from.Y + to.Y) / 2)),
                new HighlightKeyframeV1(duration, Clamp(to.X), Clamp(to.Y)),
            }));

        return new BridgeV1(
            next.Event.Sequence,
            duration,
            [.. tracks.OrderBy(track => track.EntityId, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Whether an event is worth a highlight at all, before any cap is applied.
    /// </summary>
    /// <remarks>
    /// Every goal and every penalty, then shots whose goal probability cleared the threshold, and the direct
    /// free kicks the spatial play model struck at goal (`replay-v2`). A penalty award or a free-kick award
    /// is not selected on its own: the goal or the miss that follows it a moment later is the thing worth
    /// watching, and showing both would put the same passage of play on screen twice.
    /// </remarks>
    private static bool IsWorthShowing(EngineEventV1 matchEvent, HighlightOptionsV1 options) => matchEvent.Type switch
    {
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => true,
        EngineEventType.FreeKickShot when options.IncludeFreeKicks => true,
        EngineEventType.Woodwork => options.IncludeWoodwork,
        EngineEventType.ShotSaved or EngineEventType.ShotBlocked or EngineEventType.ShotOffTarget =>
            (matchEvent.QualityBasisPoints ?? 0) >= options.MinQualityForShotBasisPoints,
        _ => false,
    };

    /// <summary>Applies the count cap, keeping every goal and the best of the rest.</summary>
    private static List<EngineEventV1> Trim(List<EngineEventV1> candidates, HighlightOptionsV1 options)
    {
        var goals = candidates.Where(matchEvent => matchEvent.IsGoal).ToList();

        if (candidates.Count <= options.MaxHighlights && goals.Count <= options.MaxHighlights)
        {
            return candidates;
        }

        var others = candidates
            .Where(matchEvent => !matchEvent.IsGoal)
            .OrderByDescending(matchEvent => matchEvent.QualityBasisPoints ?? 0)
            .ThenBy(matchEvent => matchEvent.Sequence)
            .Take(Math.Max(0, options.MaxHighlights - goals.Count));

        // Back into event order, because a presentation that jumped around the match would be unwatchable
        // however good the chances in it were.
        return [.. goals.Concat(others).OrderBy(matchEvent => matchEvent.Sequence)];
    }

    private static HighlightPresentationV1 BuildHighlight(
        MatchInputV1 input,
        EngineEventV1 matchEvent,
        Dictionary<Guid, string> names,
        (string Home, string Away) colours,
        HighlightOptionsV1 options)
    {
        var duration = DurationFor(matchEvent, options);
        var attackingSide = matchEvent.Side;
        var isSetPiece = matchEvent.Type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
            or EngineEventType.FreeKickShot;

        var entities = new List<HighlightEntityV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var isHome = side == MatchSide.Home;

            foreach (var slot in input.SideOf(side).Slots.OrderBy(slot => slot.SlotNumber))
            {
                var participant = input.SideOf(side).Squad
                    .First(candidate => candidate.ParticipantId == slot.ParticipantId);

                var anchor = FormationAnchor(slot, isHome);

                entities.Add(new HighlightEntityV1
                {
                    EntityId = EntityId(isHome, slot.SlotNumber),
                    IsBall = false,
                    Side = side,
                    ParticipantId = participant.ParticipantId,
                    ShirtNumber = participant.ShirtNumber,
                    Family = slot.Family,
                    X = anchor.X,
                    Y = anchor.Y,
                    Name = names.TryGetValue(participant.ParticipantId, out var name) ? name : null,
                    Position = PositionCode(slot),
                });
            }
        }

        // The ball is where the play model put it: the event carries its own pitch location (`engine-v3`).
        var ballX = Clamp(matchEvent.X ?? Pitch / 2);
        var ballY = Clamp(matchEvent.Y ?? Pitch / 2);

        entities.Add(new HighlightEntityV1
        {
            EntityId = BallEntityId,
            IsBall = true,
            X = ballX,
            Y = ballY,
        });

        // One track per entity, keyed by identity. An involved player's track replaces the stationary one rather
        // than joining it: two tracks for one entity would leave a renderer choosing between them, and the
        // player would appear to stand still and move at the same time.
        var tracks = new Dictionary<string, List<HighlightKeyframeV1>>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(entity => !entity.IsBall))
        {
            // Uninvolved players shift as their shape shifts: the block moves as one, towards the ball's
            // location, compressed slightly out of possession (`replay-v2` coordinated movement).
            tracks[entity.EntityId] = ShapeShiftTrack(entity, attackingSide, ballX, ballY, duration);
        }

        // The ball's track is a flight from the passage's build-up point to the strike point: rising, arcing,
        // and landing where the event was located (`replay-v2` ball aerodynamics).
        tracks[BallEntityId] = BallTrack(matchEvent, attackingSide, duration, ballX, ballY);

        // The striker closes on the ball before striking it, which is what a shot looks like from the stands.
        var shooter = entities.FirstOrDefault(
            entity => entity.ParticipantId is not null && entity.ParticipantId == matchEvent.ParticipantId);

        if (shooter is not null)
        {
            var strikeTime = isSetPiece ? 0 : (int)(duration * 0.55);

            tracks[shooter.EntityId] =
            [
                new HighlightKeyframeV1(0, shooter.X, shooter.Y),
                new HighlightKeyframeV1(Math.Max(0, strikeTime - SetPieceRunInMs), Midpoint(shooter.X, ballX), Midpoint(shooter.Y, ballY), Action: "run"),
                new HighlightKeyframeV1(strikeTime, ballX, ballY, Action: ActionFor(matchEvent.Type)),
                new HighlightKeyframeV1(duration, FollowThrough(shooter.X, ballX), FollowThrough(shooter.Y, ballY)),
            ];
        }

        var keeper = entities.FirstOrDefault(
            entity => entity.ParticipantId is not null
                && entity.ParticipantId == matchEvent.SecondaryParticipantId);

        if (keeper is not null)
        {
            tracks[keeper.EntityId] =
            [
                new HighlightKeyframeV1(0, keeper.X, keeper.Y),
                new HighlightKeyframeV1(duration / 2, KeeperDiveX(keeper.X, attackingSide), Midpoint(keeper.Y, ballY), Action: "save"),
                new HighlightKeyframeV1(duration, keeper.X, keeper.Y),
            ];
        }

        return new HighlightPresentationV1
        {
            PresentationVersion = Version,
            SourceEventSequence = matchEvent.Sequence,
            Minute = matchEvent.Minute,
            StoppageMinute = matchEvent.StoppageMinute,
            DurationMilliseconds = duration,
            OutcomeCode = OutcomeCode(matchEvent.Type),
            Narration = Narration(matchEvent, names),
            HomeColour = colours.Home,
            AwayColour = colours.Away,
            Entities = [.. entities.OrderBy(entity => entity.EntityId, StringComparer.Ordinal)],
            Tracks =
            [
                .. tracks
                    .OrderBy(track => track.Key, StringComparer.Ordinal)
                    .Select(track => new HighlightTrackV1(track.Key, track.Value)),
            ],
        };
    }

    /// <summary>
    /// Builds a coordinated shape-shift track for an uninvolved player (`replay-v2`, Stage 3).
    /// </summary>
    /// <remarks>
    /// The 22 shift with the play: the attacking block pushes up behind the ball, the defending block drops
    /// towards its own goal and compresses, each moving a bounded share of the way — enough to read as a
    /// team moving as a unit, never enough to leave a player out of position in the replay's first frame.
    /// </remarks>
    private static List<HighlightKeyframeV1> ShapeShiftTrack(
        HighlightEntityV1 entity,
        MatchSide attackingSide,
        int ballX,
        int ballY,
        int duration)
    {
        var isAttackingSide = entity.Side == attackingSide;
        var pull = isAttackingSide ? 700 : -450;
        var squeeze = isAttackingSide ? 0 : 120;

        var shiftX = (ballX > entity.X) == isAttackingSide ? pull : pull / 2;
        var shiftY = Math.Clamp(ballY - entity.Y, -squeeze, squeeze);

        var shiftToward = new
        {
            X = Clamp(entity.X + shiftX),
            Y = Clamp(entity.Y + shiftY),
        };

        // A three-phase shift: settle, hold the shape as the ball moves, recover. The mid-point holds so the
        // movement reads as a block shifting once, not as continuous noisy jitter.
        return new List<HighlightKeyframeV1>
        {
            new(0, entity.X, entity.Y),
            new(duration / 3, Midpoint(entity.X, shiftToward.X), Midpoint(entity.Y, shiftToward.Y)),
            new((duration * 2) / 3, shiftToward.X, shiftToward.Y),
            new(duration, Midpoint(shiftToward.X, entity.X), Midpoint(shiftToward.Y, entity.Y)),
        };
    }

    /// <summary>
    /// Builds the ball's track: a build-up, a rise, and the strike (`replay-v2`).
    /// </summary>
    /// <remarks>
    /// The trajectory is deterministic: a ground pass from the halfway-line anchor into the passage's fight
    /// point, then — for a strike — an arcing climb and fall resolved with the flight model's own
    /// parabola, so a lofted cross and a driven shot read differently without the client owning physics.
    /// </remarks>
    private static List<HighlightKeyframeV1> BallTrack(
        EngineEventV1 matchEvent,
        MatchSide attackingSide,
        int duration,
        int ballX,
        int ballY)
    {
        var isSetPiece = matchEvent.Type is EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed
            or EngineEventType.FreeKickShot;

        var start = isSetPiece
            ? new HighlightKeyframeV1(0, ballX, ballY)
            : new HighlightKeyframeV1(0, BuildUpX(attackingSide), Clamp(matchEvent.Y ?? (Pitch / 2)));

        var goalLine = attackingSide == MatchSide.Home ? Pitch : 0;
        var peak = PeakFor(matchEvent, isSetPiece);

        var flight = BallPhysics.Trajectory(
            new BallState(start.X, start.Y, 0),
            new BallState(ballX, ballY, 0),
            Math.Max(1, duration - (duration / 3)),
            peak,
            5);

        var track = new List<HighlightKeyframeV1> { start };

        foreach (var (timeMs, position) in flight)
        {
            track.Add(new HighlightKeyframeV1(
                start.TimeMilliseconds + timeMs,
                Clamp(position.X),
                Clamp(position.Y),
                position.Z,
                Speed: 0,
                Action: position.Z > 15 ? "aerial" : null));
        }

        // The ball comes to rest where the event happened, which is where the next bridge picks it up.
        track.Add(new HighlightKeyframeV1(duration, Clamp(ballX), Clamp(ballY)));

        return track;
    }

    private static int PeakFor(EngineEventV1 matchEvent, bool isSetPiece)
    {
        if (isSetPiece)
        {
            return 35;
        }

        return matchEvent.Type switch
        {
            EngineEventType.Goal or EngineEventType.ShotSaved or EngineEventType.Woodwork => 25,
            EngineEventType.ShotBlocked => 12,
            _ => 8,
        };
    }

    /// <summary>The X the build-up starts from: the attacking third's edge, on the attacking side's own axis.</summary>
    private static int BuildUpX(MatchSide attackingSide) =>
        attackingSide == MatchSide.Home ? Pitch / 3 : Pitch - (Pitch / 3);

    /// <summary>Where the ball sits after an event, as the bridge's starting anchor.</summary>
    private static (int X, int Y) BallAnchor(EngineEventV1 matchEvent, MatchSide attackingSide)
    {
        var x = Clamp(matchEvent.X ?? BuildUpX(attackingSide));
        var y = Clamp(matchEvent.Y ?? (Pitch / 2));

        return (x, y);
    }

    /// <summary>The formation anchor for a slot, on the shared pitch, at the scale the resolver speaks.</summary>
    private static SpatialPoint FormationAnchor(MatchSlotV1 slot, bool isHome)
    {
        // The resolver maps the tactics board's own 0..10_000 axes onto the shared pitch and mirrors for
        // the away side, which is the same mapping the simulation used when it located the play.
        return TacticalFormationResolver.ResolvePosition(
            slot,
            isHome,
            hasPossession: false,
            new SpatialPoint(Pitch / 2, SpatialPitch.PitchWidth / 2),
            new MatchInstructionsV1(),
            EngineRulesV2.Default);
    }

    private const string BallEntityId = "ball";
    private const int SetPieceRunInMs = 700;

    private static string EntityId(bool isHome, int slotNumber) => $"{(isHome ? "H" : "A")}{slotNumber}";

    private static int Clamp(int value) => int.Clamp(value, 0, Pitch);

    private static int Midpoint(int from, int to) => (from + to) / 2;

    private static int FollowThrough(int from, int to) => Clamp(from + ((to - from) / 4));

    private static int KeeperDiveX(int keeperX, MatchSide attackingSide)
    {
        // The keeper steps towards the ball's line, one step, and recovers.
        return attackingSide == MatchSide.Home
            ? Clamp(keeperX - 300)
            : Clamp(keeperX + 300);
    }

    private static string ActionFor(EngineEventType type) => type switch
    {
        EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => "penalty",
        EngineEventType.FreeKickShot => "free_kick",
        EngineEventType.Goal => "shot",
        EngineEventType.ShotSaved => "shot",
        EngineEventType.ShotBlocked => "shot",
        EngineEventType.ShotOffTarget => "shot",
        EngineEventType.Woodwork => "shot",
        _ => "pass",
    };

    private static int DurationFor(EngineEventV1 matchEvent, HighlightOptionsV1 options)
    {
        // A goal is worth the longest look. Everything else sits inside the same band so the player's speed
        // control means the same thing on every highlight.
        var length = matchEvent.IsGoal
            ? options.MaxDurationMilliseconds
            : options.MinDurationMilliseconds + ((options.MaxDurationMilliseconds - options.MinDurationMilliseconds) / 2);

        return int.Clamp(length, options.MinDurationMilliseconds, options.MaxDurationMilliseconds);
    }

    private static string OutcomeCode(EngineEventType type) => type switch
    {
        EngineEventType.Goal => "goal",
        EngineEventType.PenaltyGoal => "penalty_goal",
        EngineEventType.PenaltyMissed => "penalty_missed",
        EngineEventType.FreeKickShot => "free_kick_shot",
        EngineEventType.Woodwork => "woodwork",
        EngineEventType.ShotSaved => "saved",
        EngineEventType.ShotBlocked => "blocked",
        EngineEventType.ShotOffTarget => "off_target",
        _ => "chance",
    };

    private static string Narration(EngineEventV1 matchEvent, Dictionary<Guid, string> names)
    {
        var player = matchEvent.ParticipantId is Guid id && names.TryGetValue(id, out var name)
            ? name
            : "the attacker";

        var clock = matchEvent.StoppageMinute > 0
            ? $"{matchEvent.Minute}+{matchEvent.StoppageMinute}"
            : matchEvent.Minute.ToString(CultureInfo.InvariantCulture);

        var outcome = matchEvent.Type switch
        {
            EngineEventType.Goal => "Goal",
            EngineEventType.PenaltyGoal => "Penalty scored",
            EngineEventType.PenaltyMissed => "Penalty missed",
            EngineEventType.FreeKickShot => "Free kick struck",
            EngineEventType.Woodwork => "Shot against the woodwork",
            EngineEventType.ShotSaved => "Shot saved",
            EngineEventType.ShotBlocked => "Shot blocked",
            EngineEventType.ShotOffTarget => "Shot off target",
            _ => "Chance",
        };

        return $"{outcome} — {player}, {clock}.";
    }

    private static string PositionCode(MatchSlotV1 slot) => slot.Family switch
    {
        MatchPositionFamily.Goalkeeper => "GK",
        MatchPositionFamily.Defence => "DF",
        MatchPositionFamily.Midfield => "MF",
        MatchPositionFamily.Attack => "FW",
        _ => string.Empty,
    };

    private static Dictionary<Guid, string> Names(MatchInputV1 input)
    {
        var names = new Dictionary<Guid, string>();

        foreach (var side in new[] { input.Home, input.Away })
        {
            foreach (var participant in side.Squad)
            {
                names[participant.ParticipantId] = participant.DisplayName;
            }
        }

        return names;
    }

    /// <summary>
    /// The two sides' colours, from small generated palettes chosen by club identity.
    /// </summary>
    /// <remarks>
    /// Generated rather than supplied, and drawn from a fixed palette rather than from any real kit: the
    /// engine never has a real club's colours to leak (`WORLD-3`), and the renderer's requirement not to
    /// distinguish teams by colour alone is met downstream by ship numbers and labels.
    /// </remarks>
    private static (string Home, string Away) Colours(MatchInputV1 input) =>
        (Palette[Index(input.Home.ClubId)], Palette[Index(input.Away.ClubId)]);

    private static readonly string[] Palette =
    [
        "#1f4e79",
        "#8c2f39",
        "#2d6a4f",
        "#6a4c93",
        "#b5651d",
        "#2c3e50",
        "#a4133c",
        "#006d77",
    ];

    /// <summary>Maps a club identity onto the palette, stably across runs and platforms.</summary>
    private static int Index(Guid clubId)
    {
        var bytes = clubId.ToByteArray();
        var hash = 2166136261u;

        foreach (var value in bytes)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return (int)(hash % (uint)Palette.Length);
    }
}
