using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.MatchEngine.Tests;

/// <summary>Formations for tests of a side changing shape, on the tactics board's orientation: depth in X, width in Y.</summary>
internal static class TestShapes
{
    /// <summary>The four-three-three: a lone holding midfielder, two wingers and a centre forward.</summary>
    public static IReadOnlyList<MatchSlotV1> FourThreeThree { get; } =
    [
        Slot(1, MatchPositionFamily.Goalkeeper, MatchRole.Goalkeeper, 500, 5_000),
        Slot(2, MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 8_000),
        Slot(3, MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 6_000),
        Slot(4, MatchPositionFamily.Defence, MatchRole.CentreBack, 1_800, 4_000),
        Slot(5, MatchPositionFamily.Defence, MatchRole.FullBack, 2_000, 2_000),
        Slot(6, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 6_800),
        Slot(7, MatchPositionFamily.Midfield, MatchRole.DefensiveMidfielder, 4_200, 5_000),
        Slot(8, MatchPositionFamily.Midfield, MatchRole.CentralMidfielder, 5_200, 3_200),
        Slot(9, MatchPositionFamily.Attack, MatchRole.Winger, 8_000, 8_500),
        Slot(10, MatchPositionFamily.Attack, MatchRole.Striker, 8_500, 5_000),
        Slot(11, MatchPositionFamily.Attack, MatchRole.Winger, 8_000, 1_500),
    ];

    private static MatchSlotV1 Slot(int number, MatchPositionFamily family, MatchRole role, int x, int y) => new()
    {
        SlotNumber = number,
        Family = family,
        Role = role,
        X = x,
        Y = y,
        ParticipantId = Guid.Empty,
    };
}
