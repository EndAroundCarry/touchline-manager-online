using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.MatchEngine.Tick;

/// <summary>
/// How far a player's skills fall as his condition does (`tick-engine-v1`, tired skills): none while fresh, a tenth of the sheet value at
/// 60%, and a floor of 20% for a spent player.
/// </summary>
/// <remarks>
/// <para>
/// The drop is a share of the <em>sheet</em> value, the 1..20 attribute the manager sees, so a 15 at a 10% drop plays like a 13.5. It
/// is then passed through the engine's skill curve like any other attribute change (<see cref="TickPlayerSkills.Tired"/>). Taking the
/// share off the already curved value would be far harsher: a curved 13 would drop below what a sheet-1 player has.
/// </para>
/// <para>
/// The curve is a table of breakpoints, linear between them and flat below the last one. Mental skills lose half as much as the rest.
/// Pace and Acceleration lose nothing, because the body already slows with tiredness (<see cref="TickPlayerPhysics"/>): they would
/// be counted twice. Everything else, technical, physical and goalkeeping, takes the full drop.
/// </para>
/// <para>
/// All integers, in basis points, so the tick loop stays integer-only and the same on every machine.
/// </para>
/// </remarks>
internal static class TickSkillTiredness
{
    /// <summary>Basis points in the whole: the unit of the condition and of the drop.</summary>
    public const int BasisPoints = 10_000;

    /// <summary>The breakpoints of the drop curve: a condition and the share of the sheet value lost there, both in basis points.</summary>
    private static readonly (int Condition, int Drop)[] Breakpoints =
    [
        (10_000, 0),
        (9_000, 200),
        (8_000, 400),
        (7_000, 700),
        (6_000, 1_000),
        (5_000, 1_400),
        (4_000, 2_000),
    ];

    /// <summary>Gets the share of the sheet value a technical, physical or goalkeeping skill loses at a condition, in basis points.</summary>
    /// <param name="conditionBasisPoints">The player's live condition, 0..10,000.</param>
    public static int FullDropBasisPoints(int conditionBasisPoints)
    {
        if (conditionBasisPoints >= Breakpoints[0].Condition)
        {
            return Breakpoints[0].Drop;
        }

        for (var index = 1; index < Breakpoints.Length; index++)
        {
            var fresher = Breakpoints[index - 1];
            var tireder = Breakpoints[index];

            if (conditionBasisPoints >= tireder.Condition)
            {
                return fresher.Drop
                    + ((fresher.Condition - conditionBasisPoints) * (tireder.Drop - fresher.Drop) / (fresher.Condition - tireder.Condition));
            }
        }

        return Breakpoints[^1].Drop;
    }

    /// <summary>Gets the share of the sheet value an attribute loses at a condition, in basis points.</summary>
    /// <param name="conditionBasisPoints">The player's live condition, 0..10,000.</param>
    /// <param name="attribute">The attribute.</param>
    public static int DropBasisPoints(int conditionBasisPoints, MatchAttributeName attribute) =>
        attribute is MatchAttributeName.Pace or MatchAttributeName.Acceleration
            ? 0
            : DropBasisPoints(conditionBasisPoints, MatchAttributeNames.FamilyOf(attribute));

    /// <summary>Gets the share of the sheet value a family's skills lose at a condition, in basis points (Pace and Acceleration are not told apart here).</summary>
    /// <param name="conditionBasisPoints">The player's live condition, 0..10,000.</param>
    /// <param name="family">The family.</param>
    public static int DropBasisPoints(int conditionBasisPoints, MatchAttributeFamily family) =>
        family == MatchAttributeFamily.Mental ? MentalDropBasisPoints(FullDropBasisPoints(conditionBasisPoints)) : FullDropBasisPoints(conditionBasisPoints);

    /// <summary>Gets the share a mental skill loses when the others lose a given share: half of it.</summary>
    /// <param name="fullDropBasisPoints">The full drop, in basis points.</param>
    public static int MentalDropBasisPoints(int fullDropBasisPoints) => fullDropBasisPoints / 2;
}
