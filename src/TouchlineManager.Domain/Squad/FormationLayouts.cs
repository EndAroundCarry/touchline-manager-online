using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// One slot's default shape in a formation preset (`TAC-1`…`TAC-9`).
/// </summary>
/// <remarks>
/// The blueprint a <see cref="TacticalSlot"/> is placed from. Coordinates are the same scaled
/// 0–10,000 axis the entity stores (`TAC-9`), so a preset and a manager's dragged slot are the same
/// kind of value and a plan laid out from a preset needs no conversion.
/// </remarks>
/// <param name="SlotNumber">The slot number, 1–11. Slot 1 is the goalkeeper.</param>
/// <param name="PositionFamily">The family the slot asks for (`TAC-8`).</param>
/// <param name="Role">The role the slot asks for (`TAC-8`).</param>
/// <param name="NormalizedX">The normalized depth from the club's own goal line, 0–10,000.</param>
/// <param name="NormalizedY">The normalized position across the pitch, 0–10,000.</param>
public sealed record FormationSlot(
    int SlotNumber,
    PositionFamily PositionFamily,
    PlayerRole Role,
    int NormalizedX,
    int NormalizedY);

/// <summary>
/// The eleven default slots of each formation preset (`TAC-1`…`TAC-6`).
/// </summary>
/// <remarks>
/// <para>
/// A preset is a starting shape, not a cage: the manager may drag a slot afterwards
/// (<see cref="TacticalSlot.MoveTo"/>), so these tables are what a new plan is laid out from and what
/// the client renders as the preset's own arrangement. Keeping them here rather than in the API or the
/// web client means the server stays the authority on what a preset means, and the same numbers reach
/// the renderer, the snapshot hash, and the engine (`TAC-9`).
/// </para>
/// <para>
/// Coordinates use <c>x</c> for depth along the pitch — 0 at the club's own goal line, 10,000 at the
/// opponent's — and <c>y</c> across it. Slot 1 is always the goalkeeper, which is the invariant
/// <see cref="TacticalSlot.FirstSlotNumber"/> names.
/// </para>
/// </remarks>
public static class FormationLayouts
{
    /// <summary>The number of slots every preset names (`SQ-4`).</summary>
    public const int SlotCount = WorldRuleSet.TeamSheetStarters;

    private static readonly FormationSlot[] FourFourTwo =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 8_000),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 6_000),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 4_000),
        new(5, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 2_000),
        new(6, PositionFamily.Attack, PlayerRole.Winger, 5_800, 8_300),
        new(7, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_200, 6_200),
        new(8, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_200, 3_800),
        new(9, PositionFamily.Attack, PlayerRole.Winger, 5_800, 1_700),
        new(10, PositionFamily.Attack, PlayerRole.Striker, 8_200, 6_200),
        new(11, PositionFamily.Attack, PlayerRole.Striker, 8_200, 3_800),
    ];

    private static readonly FormationSlot[] FourThreeThree =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 8_000),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 6_000),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 4_000),
        new(5, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 2_000),
        new(6, PositionFamily.Midfield, PlayerRole.DefensiveMidfielder, 4_200, 5_000),
        new(7, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_600, 6_800),
        new(8, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_600, 3_200),
        new(9, PositionFamily.Attack, PlayerRole.Winger, 8_200, 8_200),
        new(10, PositionFamily.Attack, PlayerRole.Striker, 8_600, 5_000),
        new(11, PositionFamily.Attack, PlayerRole.Winger, 8_200, 1_800),
    ];

    private static readonly FormationSlot[] FourTwoThreeOne =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 8_000),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 6_000),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 4_000),
        new(5, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 2_000),
        new(6, PositionFamily.Midfield, PlayerRole.DefensiveMidfielder, 4_000, 6_200),
        new(7, PositionFamily.Midfield, PlayerRole.DefensiveMidfielder, 4_000, 3_800),
        new(8, PositionFamily.Midfield, PlayerRole.AttackingMidfielder, 6_400, 8_200),
        new(9, PositionFamily.Midfield, PlayerRole.AttackingMidfielder, 6_400, 5_000),
        new(10, PositionFamily.Midfield, PlayerRole.AttackingMidfielder, 6_400, 1_800),
        new(11, PositionFamily.Attack, PlayerRole.Striker, 8_600, 5_000),
    ];

    private static readonly FormationSlot[] FourOneFourOne =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 8_000),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 6_000),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 4_000),
        new(5, PositionFamily.Defence, PlayerRole.FullBack, 2_000, 2_000),
        new(6, PositionFamily.Midfield, PlayerRole.DefensiveMidfielder, 4_200, 5_000),
        new(7, PositionFamily.Attack, PlayerRole.Winger, 6_000, 8_500),
        new(8, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 6_200),
        new(9, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 3_800),
        new(10, PositionFamily.Attack, PlayerRole.Winger, 6_000, 1_500),
        new(11, PositionFamily.Attack, PlayerRole.Striker, 8_400, 5_000),
    ];

    private static readonly FormationSlot[] ThreeFiveTwo =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 7_200),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_700, 5_000),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 2_800),
        new(5, PositionFamily.Defence, PlayerRole.WingBack, 4_200, 9_200),
        new(6, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 6_600),
        new(7, PositionFamily.Midfield, PlayerRole.DefensiveMidfielder, 5_000, 5_000),
        new(8, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 3_400),
        new(9, PositionFamily.Defence, PlayerRole.WingBack, 4_200, 800),
        new(10, PositionFamily.Attack, PlayerRole.Striker, 8_300, 6_200),
        new(11, PositionFamily.Attack, PlayerRole.Striker, 8_300, 3_800),
    ];

    private static readonly FormationSlot[] FiveThreeTwo =
    [
        new(1, PositionFamily.Goalkeeper, PlayerRole.Goalkeeper, 500, 5_000),
        new(2, PositionFamily.Defence, PlayerRole.WingBack, 2_600, 9_000),
        new(3, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 6_600),
        new(4, PositionFamily.Defence, PlayerRole.CentreBack, 1_700, 5_000),
        new(5, PositionFamily.Defence, PlayerRole.CentreBack, 1_800, 3_400),
        new(6, PositionFamily.Defence, PlayerRole.WingBack, 2_600, 1_000),
        new(7, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 6_600),
        new(8, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_200, 5_000),
        new(9, PositionFamily.Midfield, PlayerRole.CentralMidfielder, 5_400, 3_400),
        new(10, PositionFamily.Attack, PlayerRole.Striker, 8_300, 6_200),
        new(11, PositionFamily.Attack, PlayerRole.Striker, 8_300, 3_800),
    ];

    static FormationLayouts()
    {
        // Authoring guard: a preset that is not eleven slots, that repeats a slot number, that puts a
        // role in the wrong family, or that strays outside the coordinate range would fail deep inside a
        // save with a confusing message. Failing here names the preset instead (TAC-7, TAC-8, TAC-9).
        foreach (var preset in FormationPresets.All)
        {
            var slots = DefaultSlots(preset);

            if (slots.Count != SlotCount)
            {
                throw new InvalidOperationException(
                    $"Formation preset {preset.ToCode()} names {slots.Count} slots; TAC-1\u2013TAC-6 require {SlotCount}.");
            }

            if (slots.Select(slot => slot.SlotNumber).Distinct().Count() != SlotCount)
            {
                throw new InvalidOperationException(
                    $"Formation preset {preset.ToCode()} repeats a slot number (TAC-8).");
            }

            foreach (var slot in slots)
            {
                if (PlayerRoles.FamilyOf(slot.Role) != slot.PositionFamily)
                {
                    throw new InvalidOperationException(
                        $"Formation preset {preset.ToCode()} puts role {slot.Role.ToCode()} in the "
                        + $"{slot.PositionFamily.ToCode()} family; TAC-8 requires the two to agree.");
                }

                if (slot.NormalizedX is < WorldRuleSet.SlotCoordinateMin or > WorldRuleSet.SlotCoordinateMax
                    || slot.NormalizedY is < WorldRuleSet.SlotCoordinateMin or > WorldRuleSet.SlotCoordinateMax)
                {
                    throw new InvalidOperationException(
                        $"Formation preset {preset.ToCode()} places a slot outside the pitch (TAC-9).");
                }
            }
        }
    }

    /// <summary>Gets the eleven default slots of a preset, ordered by slot number.</summary>
    /// <param name="preset">The formation preset.</param>
    public static IReadOnlyList<FormationSlot> DefaultSlots(FormationPreset preset) => preset switch
    {
        FormationPreset.FourFourTwo => FourFourTwo,
        FormationPreset.FourThreeThree => FourThreeThree,
        FormationPreset.FourTwoThreeOne => FourTwoThreeOne,
        FormationPreset.FourOneFourOne => FourOneFourOne,
        FormationPreset.ThreeFiveTwo => ThreeFiveTwo,
        FormationPreset.FiveThreeTwo => FiveThreeTwo,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown formation preset."),
    };
}
