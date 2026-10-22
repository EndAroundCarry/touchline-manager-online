using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Squad;

/// <summary>
/// Everything a renewal quote is derived from (`CON-3`).
/// </summary>
/// <remarks>
/// The facts the rule names — skill, age, playing time, morale and remaining term — gathered once, so the quote is a pure function of them
/// rather than of several reads that could disagree. The hidden potential is not an input (the wage reads what the market takes the player to
/// be, as his value does), and neither is the club's tier: a player costs the same wherever he plays.
/// </remarks>
/// <param name="AbilityTenths">The mean of the player's attributes in tenths of a point, so 115 is 11.5.</param>
/// <param name="Age">The player's age in game years.</param>
/// <param name="Appearances">Matches the player has taken the pitch in this season.</param>
/// <param name="MoraleBp">The player's morale in basis points (`TRN-7`).</param>
/// <param name="RemainingSeasons">Full seasons left on the current contract after the current one.</param>
public sealed record ContractRenewalInput(
    int AbilityTenths,
    int Age,
    int Appearances,
    int MoraleBp,
    int RemainingSeasons);

/// <summary>The terms a renewal quote proposes.</summary>
/// <param name="Seasons">The length of the proposed contract, 1–3 seasons (`CON-1`).</param>
/// <param name="SeasonWageMinor">The proposed wage for a season, in minor units. It is paid in 34 equal parts, one on each matchday.</param>
public sealed record ContractRenewalTerms(int Seasons, long SeasonWageMinor);

/// <summary>
/// The deterministic renewal quote the server offers (`CON-3`).
/// </summary>
/// <remarks>
/// <para>
/// A pure function, so the same player at the same point in a season always produces the same offer and a manager cannot reroll it by asking
/// twice. The base is the wage the player's skill commands (two parts what he is, one part what the market takes him to be), and bounded
/// factors move it: age, playing time, morale, the term being asked for, and how much of the current deal is left.
/// </para>
/// <para>
/// The base is the same wage curve the generator uses, so a generated squad and a renewed one are priced by one scale rather than two. Every
/// factor is a basis-point multiplier with a fixed ceiling, so the wage can never run away from the player it describes, and it never falls
/// under the wage floor.
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

        if (input.AbilityTenths is < WorldRuleSet.AttributeMin * 10 or > WorldRuleSet.AttributeMax * 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.AbilityTenths,
                $"A skill is between {WorldRuleSet.AttributeMin} and {WorldRuleSet.AttributeMax} (TRN-4).");
        }

        var baseWage = EconomyRuleSet.SeasonWageMinorForSkill(EconomyRuleSet.WageSkillTenths(input.AbilityTenths, input.Age));

        // Divided in steps so the intermediate product stays well inside long and each factor truncates on
        // its own rather than blending into one rounding.
        var wage = baseWage
            * WorldRuleSet.RenewalAgeFactorBp(input.Age) / 10_000
            * WorldRuleSet.RenewalAppearancesFactorBp(input.Appearances) / 10_000
            * WorldRuleSet.RenewalMoraleFactorBp(input.MoraleBp) / 10_000
            * WorldRuleSet.RenewalTermFactorBp(seasons) / 10_000
            * WorldRuleSet.RenewalRemainingTermFactorBp(input.RemainingSeasons) / 10_000;

        return new ContractRenewalTerms(seasons, Math.Max(EconomyRuleSet.SeasonWageFloorMinor, wage));
    }
}
