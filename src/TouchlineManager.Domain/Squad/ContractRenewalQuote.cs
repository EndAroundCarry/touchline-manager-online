using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad;

/// <summary>
/// Everything a renewal quote is derived from (`CON-3`).
/// </summary>
/// <remarks>
/// The server-only facts the rule names — ability, potential, age, playing time, morale, tier, and remaining
/// term — gathered once, so the quote is a pure function of them rather than of four reads that could
/// disagree. <see cref="Potential"/> is class C2 and never leaves the server; it is an input here and never
/// an output.
/// </remarks>
/// <param name="Ability">The player's current ability, on the 1–20 scale.</param>
/// <param name="Potential">The hidden development ceiling, on the 1–20 scale (`TRN-9`).</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="Appearances">Matches the player has taken the pitch in this season.</param>
/// <param name="MoraleBp">The player's morale in basis points (`TRN-7`).</param>
/// <param name="Tier">The tier the club plays in, which scales the base wage.</param>
/// <param name="RemainingSeasons">Full seasons left on the current contract after the current one.</param>
public sealed record ContractRenewalInput(
    int Ability,
    int Potential,
    int Age,
    int Appearances,
    int MoraleBp,
    int Tier,
    int RemainingSeasons);

/// <summary>The terms a renewal quote proposes.</summary>
/// <param name="Seasons">The length of the proposed contract, 1–3 seasons (`CON-1`).</param>
/// <param name="WeeklyWageMinor">The proposed weekly wage, in minor units.</param>
public sealed record ContractRenewalTerms(int Seasons, long WeeklyWageMinor);

/// <summary>
/// The deterministic renewal quote the server offers (`CON-3`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function, so the same player at the same point in a season always produces the same offer and a
/// manager cannot reroll it by asking twice. Ability is weighted above potential — a proven player commands
/// more than an unfulfilled one — and four bounded factors move the base wage: age, playing time, morale, and
/// the term being asked for, plus how much of the current deal is left.
/// </para>
/// <para>
/// The base is the same wage scale the generator uses, so a generated squad and a renewed one are priced by
/// one scale rather than two. Every factor is a basis-point multiplier with a fixed ceiling, so the wage can
/// never run away from the player it describes.
/// </para>
/// </remarks>
public static class ContractRenewalQuote
{
    /// <summary>Calculates the terms for a proposed contract length.</summary>
    /// <param name="input">The facts the quote is derived from.</param>
    /// <param name="seasons">The length the manager is asking for, 1–3 seasons.</param>
    /// <exception cref="ArgumentOutOfRangeException">When a fact is off its scale.</exception>
    public static ContractRenewalTerms Calculate(ContractRenewalInput input, int seasons)
    {
        ArgumentNullException.ThrowIfNull(input);

        EnsureAttributeScale(input.Ability, nameof(input));
        EnsureAttributeScale(input.Potential, nameof(input));

        var effectiveAbility = Math.Clamp(
            ((input.Ability * 2) + input.Potential) / 3,
            WorldRuleSet.AttributeMin,
            WorldRuleSet.AttributeMax);

        var baseWage = WorldRuleSet.GeneratedWeeklyWageMinorFor(effectiveAbility, input.Tier);

        // Divided in steps so the intermediate product stays well inside long and each factor truncates on
        // its own rather than blending into one rounding.
        var wage = baseWage
            * WorldRuleSet.RenewalAgeFactorBp(input.Age) / 10_000
            * WorldRuleSet.RenewalAppearancesFactorBp(input.Appearances) / 10_000
            * WorldRuleSet.RenewalMoraleFactorBp(input.MoraleBp) / 10_000
            * WorldRuleSet.RenewalTermFactorBp(seasons) / 10_000
            * WorldRuleSet.RenewalRemainingTermFactorBp(input.RemainingSeasons) / 10_000;

        return new ContractRenewalTerms(seasons, Math.Max(1, wage));
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
