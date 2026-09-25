using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.SimulationBenchmarks;

/// <summary>
/// Builds the squads the laboratory simulates.
/// </summary>
/// <remarks>
/// <para>
/// The benchmark needs snapshots and cannot use the test project's factory, so this is the tool's own. It is
/// deliberately more parameterised than a test fixture: tuning needs to vary ability, shape, and instructions
/// and watch what happens to the distributions, which is the whole point of the laboratory.
/// </para>
/// <para>
/// The squads here are synthetic and uniform — every player in a side has the same attributes. That is not
/// what the world generator produces, but it is the right baseline for balance work: it isolates the engine's
/// own formulas, so a change in the goals-per-match figure is a change the engine caused and not a change in
/// how it generated the players.
/// </para>
/// </remarks>
internal static class LaboratoryFixtures
{
    /// <summary>Builds two evenly matched sides.</summary>
    /// <param name="seed">The match seed.</param>
    public static MatchInputV1 EvenlyMatched(ulong seed) =>
        Build(seed, 13, 13, new MatchInstructionsV1(), new MatchInstructionsV1());

    /// <summary>Builds a snapshot with the given ability, shape, and instructions.</summary>
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
        MatchInstructionsV1 away) =>
        new()
        {
            FixtureId = Guid.Parse("018f0000-0000-7000-8000-0000000000f1"),
            WorldId = Guid.Parse("018f0000-0000-7000-8000-0000000000f2"),
            SeasonId = Guid.Parse("018f0000-0000-7000-8000-0000000000f3"),
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            HomeAdvantageBasisPoints = EngineRulesV1.Default.HomeAdvantageBasisPoints,
            FormulaConfigurationHash = EngineConfiguration.HashOf(EngineRulesV1.Default),
            Seed = seed,
            Home = Side(1, "Home", homeAbility, home),
            Away = Side(2, "Away", awayAbility, away),
        };

    private static MatchSideV1 Side(int clubSeed, string name, int ability, MatchInstructionsV1 instructions)
    {
        var clubId = Identity(clubSeed * 1_000);
        var squad = new List<MatchParticipantV1>();
        var slots = new List<MatchSlotV1>();

        for (var slotNumber = 1; slotNumber <= MatchInputV1.StartersOnPitch; slotNumber++)
        {
            var shape = StartingShape(slotNumber);
            var participantId = Identity((clubSeed * 100) + slotNumber);

            squad.Add(Participant(clubId, participantId, clubSeed, slotNumber, shape.Position, ability));

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
            var position = benchNumber == 1 ? MatchPosition.Goalkeeper : MatchPosition.CentralMidfielder;

            squad.Add(Participant(
                clubId,
                Identity((clubSeed * 100) + 50 + benchNumber),
                clubSeed,
                50 + benchNumber,
                position,
                ability));
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

    private static MatchParticipantV1 Participant(
        Guid clubId,
        Guid participantId,
        int clubSeed,
        int number,
        MatchPosition position,
        int ability) =>
        new()
        {
            ParticipantId = participantId,
            PlayerId = Identity((clubSeed * 10_000) + number),
            ClubId = clubId,
            DisplayName = $"P{number}",
            ShirtNumber = number,
            Position = position,
            SecondaryPositions = [],
            Attributes = PlayerAttributesV1.Uniform(ability),
            State = PlayerMatchStateV1.Uniform(8_000),
        };

    private static Guid Identity(int value) => Guid.Parse($"018f0000-0000-7000-8000-{value:D12}");

    private static Shape StartingShape(int slotNumber) => slotNumber switch
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

    private sealed record Shape(
        MatchPosition Position,
        MatchPositionFamily Family,
        MatchRole Role,
        int X,
        int Y);
}
