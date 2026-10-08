using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Builds valid match snapshots for tests.
/// </summary>
/// <remarks>
/// The engine's front door refuses a malformed snapshot, so nearly every test needs a well-formed one to
/// start from and then vary one thing. Keeping that construction in one place is also what stops a test from
/// accidentally asserting against a lineup that no longer matches the validation rules — a change to the
/// shape of a legal side would break one factory rather than thirty tests.
/// </remarks>
internal static class TestMatchFactory
{
    /// <summary>The rules every test snapshot is frozen against unless it says otherwise.</summary>
    public static EngineRulesV2 Rules { get; } = EngineRulesV2.Default;

    /// <summary>The configuration hash a valid snapshot must carry.</summary>
    public static string ConfigurationHash { get; } = EngineConfiguration.HashOf(Rules, EngineVersions.LegacyRuleSetLabel);

    /// <summary>Builds a snapshot with two evenly matched sides.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="homeAbility">The home side's uniform attribute value.</param>
    /// <param name="awayAbility">The away side's uniform attribute value.</param>
    public static MatchInputV1 Even(
        ulong seed = 20_260_925,
        int homeAbility = 13,
        int awayAbility = 13) =>
        Build(seed, homeAbility, awayAbility, new MatchInstructionsV1());

    /// <summary>Builds a snapshot in which the home side is markedly better.</summary>
    /// <param name="seed">The match seed.</param>
    public static MatchInputV1 Mismatched(ulong seed = 20_260_925) =>
        Build(seed, homeAbility: 16, awayAbility: 9, new MatchInstructionsV1());

    /// <summary>Builds a snapshot with explicit instructions for each side.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="home">The home side's instructions.</param>
    /// <param name="away">The away side's instructions.</param>
    public static MatchInputV1 WithInstructions(
        MatchInstructionsV1 home,
        MatchInstructionsV1 away,
        ulong seed = 20_260_925) =>
        Build(seed, 13, 13, home, away);

    /// <summary>Builds a snapshot with a chosen seed and instruction set.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="instructions">Both sides' instructions.</param>
    public static MatchInputV1 WithSeed(ulong seed, MatchInstructionsV1? instructions = null) =>
        Build(seed, 13, 13, instructions ?? new MatchInstructionsV1());

    /// <summary>Builds a well-formed snapshot.</summary>
    /// <param name="seed">The match seed.</param>
    /// <param name="homeAbility">The home side's uniform attribute value.</param>
    /// <param name="awayAbility">The away side's uniform attribute value.</param>
    /// <param name="home">The home side's instructions.</param>
    /// <param name="away">The away side's instructions.</param>
    public static MatchInputV1 Build(
        ulong seed,
        int homeAbility,
        int awayAbility,
        MatchInstructionsV1 home,
        MatchInstructionsV1? away = null)
    {
        var fixtureId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
        var worldId = Guid.Parse("018f0000-0000-7000-8000-000000000002");
        var seasonId = Guid.Parse("018f0000-0000-7000-8000-000000000003");

        return new MatchInputV1
        {
            FixtureId = fixtureId,
            WorldId = worldId,
            SeasonId = seasonId,
            EngineVersion = EngineVersions.LegacyEngineLabel,
            RuleSetVersion = EngineVersions.LegacyRuleSetLabel,
            HomeAdvantageBasisPoints = Rules.HomeAdvantageBasisPoints,
            FormulaConfigurationHash = ConfigurationHash,
            Seed = seed,
            Home = Side(101, "Alpha Town", homeAbility, home),
            Away = Side(202, "Beta Rovers", awayAbility, away ?? new MatchInstructionsV1()),
        };
    }

    /// <summary>Builds one side's squad and slots.</summary>
    /// <param name="clubSeed">A number that makes the club's identities distinct.</param>
    /// <param name="name">The club's name.</param>
    /// <param name="ability">The uniform attribute value for every player.</param>
    /// <param name="instructions">The side's instructions.</param>
    public static MatchSideV1 Side(int clubSeed, string name, int ability, MatchInstructionsV1 instructions)
    {
        var clubId = Identity(clubSeed);
        var squad = new List<MatchParticipantV1>();
        var slots = new List<MatchSlotV1>();

        for (var slotNumber = 1; slotNumber <= MatchInputV1.StartersOnPitch; slotNumber++)
        {
            var shape = Shape(slotNumber);
            var participantId = Identity((clubSeed * 100) + slotNumber);

            squad.Add(new MatchParticipantV1
            {
                ParticipantId = participantId,
                PlayerId = Identity((clubSeed * 1_000) + slotNumber),
                ClubId = clubId,
                DisplayName = $"{name.Split(' ')[0]} {slotNumber}",
                ShirtNumber = slotNumber,
                Position = shape.Position,
                SecondaryPositions = shape.SecondaryValue,
                Attributes = PlayerAttributesV1.Uniform(ability),
                State = PlayerMatchStateV1.Uniform(8_000),
            });

            slots.Add(new MatchSlotV1
            {
                SlotNumber = slotNumber,
                Family = shape.Family,
                Role = shape.Role,
                X = shape.X,
                Y = shape.Y,
                ParticipantId = participantId,
            });
        }

        for (var benchNumber = 1; benchNumber <= MatchInputV1.MaxSubstitutesOnBench; benchNumber++)
        {
            var shape = BenchShape(benchNumber);

            squad.Add(new MatchParticipantV1
            {
                ParticipantId = Identity((clubSeed * 100) + 50 + benchNumber),
                PlayerId = Identity((clubSeed * 1_000) + 50 + benchNumber),
                ClubId = clubId,
                DisplayName = $"{name.Split(' ')[0]} Sub{benchNumber}",
                ShirtNumber = 20 + benchNumber,
                Position = shape.Position,
                SecondaryPositions = shape.SecondaryValue,
                Attributes = PlayerAttributesV1.Uniform(ability),
                State = PlayerMatchStateV1.Uniform(8_000),
            });
        }

        return new MatchSideV1
        {
            ClubId = clubId,
            ClubName = name,
            Squad = squad,
            Slots = slots,
            Instructions = instructions,
        };
    }

    /// <summary>
    /// Simulates a snapshot with both replay recorders attached and builds its presentation.
    /// </summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="options">The film's pacing and caps, or the defaults.</param>
    /// <remarks>
    /// The one way a test gets a presentation: the passages must be recorded from the same run the result
    /// came from, so the film and the result cannot describe two different matches.
    /// </remarks>
    public static (MatchResultV1 Result, MatchPresentationV1 Presentation) Play(
        MatchInputV1 input,
        HighlightOptionsV1? options = null)
    {
        var liveMetrics = new PlayerLiveMetricsRecorder();
        var passages = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, Rules, liveMetrics, passages);
        var presentation = ReplayDirector.Build(input, result, passages.Passages, options, liveMetrics.Metrics);

        return (result, presentation);
    }

    /// <summary>
    /// Simulates a snapshot with both replay recorders attached and builds its presentation along with the
    /// measurements of the film it describes.
    /// </summary>
    /// <param name="input">The snapshot.</param>
    /// <param name="options">The film's pacing and caps, or the defaults.</param>
    public static (MatchResultV1 Result, FilmBuild Build) Analyse(
        MatchInputV1 input,
        HighlightOptionsV1? options = null)
    {
        var liveMetrics = new PlayerLiveMetricsRecorder();
        var passages = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, Rules, liveMetrics, passages);

        return (result, ReplayDirector.Analyse(input, result, passages.Passages, options, liveMetrics.Metrics));
    }

    /// <summary>
    /// Simulates a snapshot and returns what the film is going to show, before it is timed or moved.
    /// </summary>
    /// <param name="input">The snapshot.</param>
    public static (MatchResultV1 Result, IReadOnlyList<MatchPassageV1> Passages, FilmScriptResult Script) Script(MatchInputV1 input)
    {
        var passages = new MatchPassageRecorder();
        var result = MatchSimulator.Simulate(input, Rules, null, passages);

        return (result, passages.Passages, ReplayDirector.ScriptOf(input, result, passages.Passages));
    }

    /// <summary>The template key that narrates the outcome of an event in a passage's commentary, or null for one that is not narrated there.</summary>
    /// <param name="type">The event type.</param>
    public static string? OutcomeTemplate(EngineEventType type) => type switch
    {
        EngineEventType.Goal => "match.goal",
        EngineEventType.PenaltyGoal => "match.penalty.goal",
        EngineEventType.PenaltyMissed => "match.penalty.missed",
        EngineEventType.ShotSaved => "match.shot.saved",
        EngineEventType.ShotBlocked => "match.shot.blocked",
        EngineEventType.ShotOffTarget => "match.shot.off_target",
        EngineEventType.Woodwork => "match.shot.woodwork",
        EngineEventType.FreeKickShot => "match.free_kick.struck",
        _ => null,
    };

    /// <summary>
    /// Gets the film time an event is shown at: the start of its passage plus the moment its line is read.
    /// </summary>
    /// <param name="presentation">The built presentation.</param>
    /// <param name="matchEvent">The event.</param>
    /// <returns>The film time in milliseconds, or null when the event is in no passage.</returns>
    /// <param name="events">
    /// The match's events. A passage can hold several strikes of one kind, and a line does not say which event it
    /// narrates, so the event's place among its kind in the passage picks its line; without them the first line is read.
    /// </param>
    public static int? MomentOf(MatchPresentationV1 presentation, EngineEventV1 matchEvent, IReadOnlyList<EngineEventV1>? events = null)
    {
        for (var index = 0; index < presentation.Passages.Count; index++)
        {
            var passage = presentation.Passages[index];

            if (!passage.EventSequences.Contains(matchEvent.Sequence))
            {
                continue;
            }

            var key = OutcomeTemplate(matchEvent.Type);
            var lines = key is null ? [] : passage.Commentary.Where(candidate => candidate.TemplateKey == key).OrderBy(candidate => candidate.TimeMilliseconds).ToList();
            var rank = events is null
                ? 0
                : events.Count(other => other.Type == matchEvent.Type && other.Sequence < matchEvent.Sequence && passage.EventSequences.Contains(other.Sequence));
            var line = lines.Count == 0 ? null : lines[Math.Min(rank, lines.Count - 1)];

            return presentation.Playback[index].StartMilliseconds + (line?.TimeMilliseconds ?? 0);
        }

        return null;
    }

    /// <summary>
    /// Whether the reel shows a chance: a clip that is running at the moment its outcome is read.
    /// </summary>
    /// <param name="presentation">The built presentation.</param>
    /// <param name="matchEvent">The chance.</param>
    public static bool ReelCovers(MatchPresentationV1 presentation, EngineEventV1 matchEvent)
    {
        var moment = MomentOf(presentation, matchEvent);

        return moment is int at
            && presentation.Reel.Any(clip => clip.StartMilliseconds <= at && clip.EndMilliseconds >= at);
    }

    /// <summary>Gets the match second a passage's clock reads at a moment inside it.</summary>
    /// <param name="passage">The passage.</param>
    /// <param name="relativeMilliseconds">The moment, from the passage's start.</param>
    public static int ClockAt(PassageV1 passage, int relativeMilliseconds)
    {
        var clock = passage.Clock;

        if (relativeMilliseconds <= clock[0].TimeMilliseconds)
        {
            return clock[0].MatchSecond;
        }

        for (var index = 1; index < clock.Count; index++)
        {
            if (relativeMilliseconds > clock[index].TimeMilliseconds)
            {
                continue;
            }

            var from = clock[index - 1];
            var to = clock[index];
            var span = Math.Max(1, to.TimeMilliseconds - from.TimeMilliseconds);

            return from.MatchSecond + (int)((long)(to.MatchSecond - from.MatchSecond) * (relativeMilliseconds - from.TimeMilliseconds) / span);
        }

        return clock[^1].MatchSecond;
    }

    /// <summary>
    /// Stands both sides where the tactics board does: the domain's four-four-two, with depth in X and width in Y
    /// (`replay-v6`).
    /// </summary>
    /// <remarks>
    /// <see cref="Shape"/> has X across and Y down the pitch, which the engine tests and the goldens are built on.
    /// The film reads the board's way round, so a test of how the film arranges the players wants this, and a test of
    /// the simulation does not.
    /// </remarks>
    /// <param name="input">The snapshot.</param>
    public static MatchInputV1 OnTheBoard(MatchInputV1 input)
    {
        // The domain's first preset: slot number, family, role, depth from the own goal line, and width.
        (MatchPositionFamily Family, MatchRole Role, int X, int Y)[] board =
        [
            (MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
            (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 8_000),
            (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_000),
            (MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 4_000),
            (MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_000),
            (MatchPositionFamily.Attack, MatchRole.Winger, 5_800, 8_300),
            (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 6_200),
            (MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 3_800),
            (MatchPositionFamily.Attack, MatchRole.Winger, 5_800, 1_700),
            (MatchPositionFamily.Attack, MatchRole.Striker, 8_200, 6_200),
            (MatchPositionFamily.Attack, MatchRole.Striker, 8_200, 3_800),
        ];

        MatchSideV1 Restand(MatchSideV1 side) => side with
        {
            Slots =
            [
                .. side.Slots.Select(slot =>
                {
                    var place = board[slot.SlotNumber - 1];

                    return slot with { Family = place.Family, Role = place.Role, X = place.X, Y = place.Y };
                }),
            ],
        };

        return input with { Home = Restand(input.Home), Away = Restand(input.Away) };
    }

    /// <summary>A four-four-two, which is the shape the domain's first preset describes.</summary>
    internal static SlotShape Shape(int slotNumber) => slotNumber switch
    {
        1 => new(MatchPosition.Goalkeeper, MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 5_000, 800),
        2 => new(MatchPosition.RightBack, MatchPositionFamily.Defence, MatchRole.FullBack, 8_000, 2_600),
        3 => new(MatchPosition.CentreBack, MatchPositionFamily.Defence, MatchRole.CentreBack, 6_200, 2_100),
        4 => new(MatchPosition.CentreBack, MatchPositionFamily.Defence, MatchRole.CentreBack, 3_800, 2_100),
        5 => new(MatchPosition.LeftBack, MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_600),
        6 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 8_200, 4_900),
        7 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 6_000, 4_300),
        8 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 4_000, 4_300),
        9 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 1_800, 4_900),
        10 => new(MatchPosition.Striker, MatchPositionFamily.Attack, MatchRole.Striker, 6_000, 7_200),
        11 => new(MatchPosition.Striker, MatchPositionFamily.Attack, MatchRole.Striker, 4_000, 7_200),
        _ => throw new ArgumentOutOfRangeException(nameof(slotNumber), slotNumber, "Unknown slot."),
    };

    private static SlotShape BenchShape(int benchNumber) => benchNumber switch
    {
        1 => new(MatchPosition.Goalkeeper, MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 5_000, 800),
        2 => new(MatchPosition.CentreBack, MatchPositionFamily.Defence, MatchRole.CentreBack, 6_000, 2_100),
        3 => new(MatchPosition.LeftBack, MatchPositionFamily.Defence, MatchRole.FullBack, 2_500, 2_600),
        4 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_000, 4_300),
        5 => new(MatchPosition.CentralMidfielder, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_000, 4_300),
        6 => new(MatchPosition.Striker, MatchPositionFamily.Attack, MatchRole.Striker, 5_000, 7_200),
        7 => new(MatchPosition.LeftWinger, MatchPositionFamily.Attack, MatchRole.Winger, 2_000, 6_500),
        _ => throw new ArgumentOutOfRangeException(nameof(benchNumber), benchNumber, "Unknown bench slot."),
    };

    /// <summary>A stable identifier for a test identity, in the same shape a real one takes.</summary>
    /// <param name="value">A number.</param>
    public static Guid Identity(int value) =>
        Guid.Parse($"018f0000-0000-7000-8000-{value:D12}");

    /// <summary>One slot's shape, so the factory and its assertions agree on where everybody stands.</summary>
    /// <param name="Position">The player's natural position.</param>
    /// <param name="Family">The family the slot belongs to.</param>
    /// <param name="Role">The role the slot asks for.</param>
    /// <param name="X">Position across the pitch.</param>
    /// <param name="Y">Position down the pitch.</param>
    /// <param name="Secondary">The player's further positions.</param>
    internal sealed record SlotShape(
        MatchPosition Position,
        MatchPositionFamily Family,
        MatchRole Role,
        int X,
        int Y,
        IReadOnlyList<MatchPosition>? Secondary = null)
    {
        /// <summary>Gets the secondary positions, never null.</summary>
        public IReadOnlyList<MatchPosition> SecondaryValue => Secondary ?? [];
    }
}
