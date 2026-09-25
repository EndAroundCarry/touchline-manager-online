using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The pure squad-composition rules (`SQ-2`, `SQ-3`).
/// </summary>
/// <remarks>
/// Gathering them in one place means the generator, the invariant tests, and — in Stage 8 — the AI's
/// own squad maintenance all answer "is this squad legal?" the same way, which is what `INS-12` requires:
/// humans and AI are validated by the identical rule set, with no bypass.
/// </remarks>
public static class SquadLegality
{
    /// <summary>Gets whether a registered count is within the allowed senior-squad bounds (`SQ-2`, `SQ-3`).</summary>
    /// <param name="registeredCount">The number of registered senior players.</param>
    public static bool IsWithinBounds(int registeredCount) =>
        registeredCount is >= WorldRuleSet.SquadMinimumRegistered and <= WorldRuleSet.SquadMaximumRegistered;

    /// <summary>Gets whether a registered count reaches the minimum (`SQ-2`).</summary>
    /// <param name="registeredCount">The number of registered senior players.</param>
    public static bool MeetsMinimum(int registeredCount) =>
        registeredCount >= WorldRuleSet.SquadMinimumRegistered;

    /// <summary>Gets whether a squad contains enough goalkeepers (`SQ-2`).</summary>
    /// <param name="families">The position family of each registered player.</param>
    public static bool HasMinimumGoalkeepers(IEnumerable<PositionFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);

        return families.Count(family => family == PositionFamily.Goalkeeper)
            >= WorldRuleSet.MinimumGoalkeepers;
    }

    /// <summary>Gets whether a squad is legal: within bounds and carrying enough goalkeepers.</summary>
    /// <param name="registeredCount">The number of registered senior players.</param>
    /// <param name="families">The position family of each registered player.</param>
    public static bool IsLegal(int registeredCount, IEnumerable<PositionFamily> families) =>
        IsWithinBounds(registeredCount) && HasMinimumGoalkeepers(families);
}
