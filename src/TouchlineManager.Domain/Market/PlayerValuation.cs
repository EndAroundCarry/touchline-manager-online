using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Market;

/// <summary>Everything a transfer valuation is derived from (`TRF-12`).</summary>
/// <remarks>
/// The server-only facts the valuation reads — ability, hidden potential, age, and the tier a club plays
/// in — gathered once, so a value is a pure function of them rather than of several reads that could
/// disagree. <see cref="Potential"/> is class C2 and never leaves the server; it is an input here and
/// never an output (`MAT-11`).
/// </remarks>
/// <param name="Ability">The player's current ability, on the 1–20 scale.</param>
/// <param name="Potential">The hidden development ceiling, on the 1–20 scale (`TRN-9`).</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="Tier">The tier the valuing club plays in, which scales the base like the other baselines.</param>
public sealed record PlayerValuationInput(int Ability, int Potential, int Age, int Tier);

/// <summary>
/// The deterministic transfer value of a player (`TRF-12`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function, so the AI's asking price and bid ceiling are reproducible from the facts
/// alone. It is derived from the wage scale the generator and the renewal quote already use rather than
/// from a second, disconnected money scale: a player's fee is a versioned number of weeks of the wage they
/// command, moved by bounded age and potential factors. That keeps a squad, a renewal, and a transfer
/// priced by one family of rules.
/// </para>
/// <para>
/// The value is server-only (class C2): it is an input to the AI's decisions and the basis of a listing's
/// asking price, and it never appears in a manager-facing response (`MAT-11`).
/// </para>
/// </remarks>
public static class PlayerValuation
{
    /// <summary>The version label, so a change to the valuation is a named rule change (`FIC-8`, `RULE-3`).</summary>
    public const string Version = "player-valuation-v1";

    /// <summary>Estimates a player's transfer value, in minor units.</summary>
    /// <param name="input">The facts the value is derived from.</param>
    /// <exception cref="ArgumentOutOfRangeException">When a fact is off its scale.</exception>
    public static long Estimate(PlayerValuationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        EnsureAttributeScale(input.Ability, nameof(input));
        EnsureAttributeScale(input.Potential, nameof(input));
        ArgumentOutOfRangeException.ThrowIfNegative(input.Age);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Tier, 1);

        // Ability weighted above potential, exactly as the renewal quote weighs them, so a proven player is
        // worth more than an unfulfilled one of the same ceiling.
        var effectiveAbility = Math.Clamp(
            ((input.Ability * 2) + input.Potential) / 3,
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

        var baseWage = WorldRuleSet.GeneratedWeeklyWageMinorFor(effectiveAbility, input.Tier);

        // Divided in steps so the intermediate product stays well inside long and each factor truncates on
        // its own rather than blending into one rounding (the same shape ContractRenewalQuote uses).
        var value = baseWage
            * WorldRuleSet.PlayerValuationWeeksOfWage
            * WorldRuleSet.PlayerValuationAgeFactorBp(input.Age) / 10_000
            * WorldRuleSet.PlayerValuationPotentialFactorBp(input.Potential) / 10_000;

        return Math.Max(1, value);
    }

    private static void EnsureAttributeScale(int value, string parameterName)
    {
        if (value is < WorldRuleSet.AttributeMin or > WorldRuleSet.AttributeMax)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"An ability or potential value is between {WorldRuleSet.AttributeMin} and {WorldRuleSet.AttributeMax} (TRN-4).");
        }
    }
}
