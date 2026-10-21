using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad.Generation;

namespace Tactical11Manager.Domain.Squad.Training;

/// <summary>
/// How far a player can develop: which attributes grow, and the limit his potential and position set on them
/// (`TRN-4`).
/// </summary>
/// <remarks>
/// <para>
/// The limit is the generator's own formula for an adult of that potential and position, `potential + the
/// position's bonus` on each attribute, capped at the scale (see <c>PlayerGenerator</c>), so a trained
/// youth finishes as the generator would have made him as an adult. Development stops when the sum of his
/// developing attributes reaches the sum of that formula, which is the overall rule, and no attribute may pass
/// its own formula value by more than <see cref="CeilingHeadroom"/>, so a programme can tilt a player without
/// letting one skill run away.
/// </para>
/// <para>
/// Both are derived from the potential and the primary position, which never change, so they need no storage.
/// </para>
/// </remarks>
public static class DevelopmentLimit
{
    /// <summary>How many points one attribute may go above its formula value (`TRN-4`).</summary>
    public const int CeilingHeadroom = 1;

    private static readonly IReadOnlyList<AttributeName> OutfieldAttributes =
    [
        .. AttributeNames.All.Where(name => AttributeNames.FamilyOf(name) != AttributeFamily.Goalkeeping),
    ];

    /// <summary>Gets whether a player of a position develops an attribute.</summary>
    /// <remarks>An outfielder's goalkeeping attributes never grow; a keeper develops all 28.</remarks>
    /// <param name="position">The primary position.</param>
    /// <param name="name">The attribute.</param>
    public static bool Develops(PlayerPosition position, AttributeName name) =>
        position == PlayerPosition.Goalkeeper || AttributeNames.FamilyOf(name) != AttributeFamily.Goalkeeping;

    /// <summary>Gets the attributes a player of a position develops, in canonical order.</summary>
    /// <param name="position">The primary position.</param>
    public static IReadOnlyList<AttributeName> DevelopingAttributes(PlayerPosition position) =>
        position == PlayerPosition.Goalkeeper ? AttributeNames.All : OutfieldAttributes;

    /// <summary>Gets what the generator makes one attribute of an adult of this potential and position.</summary>
    /// <param name="position">The primary position.</param>
    /// <param name="potential">The hidden potential, on the displayed scale.</param>
    /// <param name="name">The attribute.</param>
    public static int FormulaValue(PlayerPosition position, int potential, AttributeName name) =>
        Math.Clamp(
            potential + PlayerAttributeProfiles.BonusFor(position, name),
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

    /// <summary>Gets the highest an attribute may be trained to: its formula value plus the headroom.</summary>
    /// <param name="position">The primary position.</param>
    /// <param name="potential">The hidden potential, on the displayed scale.</param>
    /// <param name="name">The attribute.</param>
    public static int LimitOf(PlayerPosition position, int potential, AttributeName name) =>
        Math.Clamp(
            potential + PlayerAttributeProfiles.BonusFor(position, name) + CeilingHeadroom,
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

    /// <summary>
    /// Gets the sum of the formula values over the developing attributes: development stops when the player's
    /// own sum reaches it. A sum rather than an average, so the rule is exact with no rounding.
    /// </summary>
    /// <param name="position">The primary position.</param>
    /// <param name="potential">The hidden potential, on the displayed scale.</param>
    public static int CapSum(PlayerPosition position, int potential) =>
        DevelopingAttributes(position).Sum(name => FormulaValue(position, potential, name));

    /// <summary>
    /// Gets every attribute's limit in canonical order, with zero for an attribute the player never develops.
    /// </summary>
    /// <param name="position">The primary position.</param>
    /// <param name="potential">The hidden potential, on the displayed scale.</param>
    public static int[] Limits(PlayerPosition position, int potential)
    {
        var limits = new int[AttributeNames.Count];

        foreach (var name in DevelopingAttributes(position))
        {
            limits[(int)name] = LimitOf(position, potential, name);
        }

        return limits;
    }
}
