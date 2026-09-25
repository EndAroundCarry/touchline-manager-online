using System.Globalization;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Turns a simulated match into replayable highlights (master plan §9.2, `MAT-8`).
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
/// Positions are the same normalized coordinates the tactics board stores, mirrored for the away side, so the
/// renderer can draw a highlight without the client owning a second pitch model.
/// </para>
/// </remarks>
public static class HighlightDirector
{
    /// <summary>The version label of this presentation.</summary>
    public const string Version = "highlights-v1";

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

        return new MatchPresentationV1
        {
            PresentationVersion = Version,
            EngineVersion = result.EngineVersion,
            HomeGoals = result.HomeGoals,
            AwayGoals = result.AwayGoals,
            Highlights = highlights,
        };
    }

    /// <summary>
    /// Whether an event is worth a highlight at all, before any cap is applied.
    /// </summary>
    /// <remarks>
    /// Every goal and every penalty, then shots whose goal probability cleared the threshold. A penalty award
    /// is not selected on its own: the goal or the miss that follows it a moment later is the thing worth
    /// watching, and showing both would put the same passage of play on screen twice.
    /// </remarks>
    private static bool IsWorthShowing(EngineEventV1 matchEvent, HighlightOptionsV1 options) => matchEvent.Type switch
    {
        EngineEventType.Goal or EngineEventType.PenaltyGoal or EngineEventType.PenaltyMissed => true,
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
        IReadOnlyDictionary<Guid, string> names,
        (string Home, string Away) colours,
        HighlightOptionsV1 options)
    {
        var duration = DurationFor(matchEvent, options);
        var attackingSide = matchEvent.Side;

        var entities = new List<HighlightEntityV1>();

        foreach (var side in new[] { MatchSide.Home, MatchSide.Away })
        {
            var isHome = side == MatchSide.Home;

            foreach (var slot in input.SideOf(side).Slots.OrderBy(slot => slot.SlotNumber))
            {
                var participant = input.SideOf(side).Squad
                    .First(candidate => candidate.ParticipantId == slot.ParticipantId);

                var (x, y) = Mirror(slot.X, slot.Y, isHome);

                entities.Add(new HighlightEntityV1
                {
                    EntityId = $"{(isHome ? "H" : "A")}{slot.SlotNumber}",
                    IsBall = false,
                    Side = side,
                    ParticipantId = participant.ParticipantId,
                    ShirtNumber = participant.ShirtNumber,
                    Family = slot.Family,
                    X = x,
                    Y = y,
                });
            }
        }

        var ballStart = new HighlightKeyframeV1(0, Pitch / 2, Pitch / 2);
        var ballEnd = BallEnd(matchEvent, attackingSide, duration);
        var ballMid = new HighlightKeyframeV1(duration / 2, Pitch / 2, HalfwayTo(ballEnd.Y, attackingSide));

        entities.Add(new HighlightEntityV1
        {
            EntityId = "ball",
            IsBall = true,
            X = ballStart.X,
            Y = ballStart.Y,
        });

        // One track per entity, keyed by identity. An involved player's track replaces the stationary one rather
        // than joining it: two tracks for one entity would leave a renderer choosing between them, and the
        // player would appear to stand still and move at the same time.
        var tracks = new Dictionary<string, List<HighlightKeyframeV1>>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(entity => !entity.IsBall))
        {
            // Uninvolved players hold their formation anchor, which is two keyframes rather than one per frame.
            tracks[entity.EntityId] =
            [
                new HighlightKeyframeV1(0, entity.X, entity.Y),
                new HighlightKeyframeV1(duration, entity.X, entity.Y),
            ];
        }

        var shooter = entities.FirstOrDefault(
            entity => entity.ParticipantId is not null && entity.ParticipantId == matchEvent.ParticipantId);

        if (shooter is not null)
        {
            tracks[shooter.EntityId] =
            [
                new HighlightKeyframeV1(0, shooter.X, shooter.Y),
                new HighlightKeyframeV1(duration / 2, ballMid.X, ballMid.Y),
                new HighlightKeyframeV1(duration, shooter.X, shooter.Y),
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
                new HighlightKeyframeV1(duration / 2, ballEnd.X, GoalLine(attackingSide)),
                new HighlightKeyframeV1(duration, keeper.X, keeper.Y),
            ];
        }

        tracks["ball"] = [ballStart, ballMid, ballEnd];

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

    private static int DurationFor(EngineEventV1 matchEvent, HighlightOptionsV1 options)
    {
        // A goal is worth the longest look. Everything else sits inside the same band so the player's speed
        // control means the same thing on every highlight.
        var length = matchEvent.IsGoal
            ? options.MaxDurationMilliseconds
            : options.MinDurationMilliseconds + ((options.MaxDurationMilliseconds - options.MinDurationMilliseconds) / 2);

        return int.Clamp(length, options.MinDurationMilliseconds, options.MaxDurationMilliseconds);
    }

    private static HighlightKeyframeV1 BallEnd(EngineEventV1 matchEvent, MatchSide attackingSide, int duration)
    {
        var goalLine = GoalLine(attackingSide);

        return matchEvent.Zone switch
        {
            ShotZone.WideLeft => new HighlightKeyframeV1(duration, Pitch / 8, goalLine),
            ShotZone.WideRight => new HighlightKeyframeV1(duration, Pitch - (Pitch / 8), goalLine),
            ShotZone.InsideLeft => new HighlightKeyframeV1(duration, Pitch / 3, goalLine),
            ShotZone.InsideRight => new HighlightKeyframeV1(duration, Pitch - (Pitch / 3), goalLine),
            _ => new HighlightKeyframeV1(duration, Pitch / 2, goalLine),
        };
    }

    /// <summary>Gets the goal line the attacking side is shooting at.</summary>
    private static int GoalLine(MatchSide attackingSide) =>
        attackingSide == MatchSide.Home ? Pitch : 0;

    private static int HalfwayTo(int target, MatchSide attackingSide)
    {
        var start = Pitch / 2;

        // The ball's mid-point is between the centre and the goal, so the highlight reads as a move forward
        // rather than a teleport.
        return start + ((target - start) / 2);
    }

    private static (int X, int Y) Mirror(int x, int y, bool isHome) =>
        isHome ? (x, y) : (Pitch - x, Pitch - y);

    private static string OutcomeCode(EngineEventType type) => type switch
    {
        EngineEventType.Goal => "goal",
        EngineEventType.PenaltyGoal => "penalty_goal",
        EngineEventType.PenaltyMissed => "penalty_missed",
        EngineEventType.Woodwork => "woodwork",
        EngineEventType.ShotSaved => "saved",
        EngineEventType.ShotBlocked => "blocked",
        EngineEventType.ShotOffTarget => "off_target",
        _ => "chance",
    };

    private static string Narration(EngineEventV1 matchEvent, IReadOnlyDictionary<Guid, string> names)
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
            EngineEventType.Woodwork => "Shot against the woodwork",
            EngineEventType.ShotSaved => "Shot saved",
            EngineEventType.ShotBlocked => "Shot blocked",
            EngineEventType.ShotOffTarget => "Shot off target",
            _ => "Chance",
        };

        return $"{outcome} — {player}, {clock}.";
    }

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
