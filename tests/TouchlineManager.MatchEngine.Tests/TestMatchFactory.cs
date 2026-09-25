using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
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
    public static EngineRulesV1 Rules { get; } = EngineRulesV1.Default;

    /// <summary>The configuration hash a valid snapshot must carry.</summary>
    public static string ConfigurationHash { get; } = EngineConfiguration.HashOf(Rules);

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
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
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
