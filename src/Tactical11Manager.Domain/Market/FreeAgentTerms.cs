using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Market;

/// <summary>What a free agent costs and what he signs for (`FA-6`, `FA-7`).</summary>
/// <param name="MinimumFeeMinor">The least a bid can be, in minor units. The fee leaves the game.</param>
/// <param name="SeasonWageMinor">The wage for a season the signing club pays, frozen on the listing (`CON-5`). It is paid in 34 equal parts.</param>
/// <param name="ContractSeasons">The length of the contract, in game seasons.</param>
/// <param name="SquadStatus">The standing the player joins the squad with.</param>
public sealed record FreeAgentOffer(
    long MinimumFeeMinor,
    long SeasonWageMinor,
    int ContractSeasons,
    SquadStatus SquadStatus);

/// <summary>
/// The terms of a free agent, a pure function of the ability he shows and his age (`FA-6`, `FA-7`).
/// </summary>
/// <remarks>
/// <para>
/// A free agent's hidden potential is never an input, and neither is a tier: the valuation reads only the skill and the age, so a hidden gem
/// costs and earns exactly what a normal player of the same ability and age does, and the price cannot give him away.
/// </para>
/// <para>
/// A released player is a former club player, listed like any club's player, so his fee is his full valuation, as a club listing's asking
/// price is (`FA-6`); a generated free agent's is a quarter of it. The wage uses the renewal quote with neutral facts: no appearances, neutral
/// morale, and a term that is neither running out nor long.
/// </para>
/// </remarks>
public static class FreeAgentTerms
{
    /// <summary>Calculates the terms a free agent is listed on.</summary>
    /// <param name="ability">The ability he shows, on the 1–20 scale.</param>
    /// <param name="age">His age in game years.</param>
    public static FreeAgentOffer Calculate(int ability, int age) => Calculate(ability, age, WorldRuleSet.FreeAgentMinimumFeeBp);

    /// <summary>Calculates the terms a released player is listed on: his full valuation.</summary>
    /// <param name="ability">The ability he shows, on the 1–20 scale.</param>
    /// <param name="age">His age in game years.</param>
    public static FreeAgentOffer CalculateReleased(int ability, int age) => Calculate(ability, age, WorldRuleSet.FreeAgentReleasedFeeBp);

    private static FreeAgentOffer Calculate(int ability, int age, int feeBp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ability, WorldRuleSet.AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ability, WorldRuleSet.AttributeMax);
        ArgumentOutOfRangeException.ThrowIfNegative(age);

        var valuation = PlayerValuation.Estimate(PlayerValuationInput.FromAbility(ability, age));

        var minimumFee = Math.Max(
            WorldRuleSet.MinimumBidIncrementMinor,
            valuation * feeBp / 10_000);

        var seasons = WorldRuleSet.FreeAgentContractSeasonsFor(age);

        var wage = ContractRenewalQuote.Calculate(
            new ContractRenewalInput(
                ability * 10,
                age,
                Appearances: 0,
                PlayerState.NeutralBasisPoints,
                RemainingSeasons: 2),
            seasons);

        return new FreeAgentOffer(
            minimumFee,
            wage.SeasonWageMinor,
            seasons,
            SquadStatusFor(age));
    }

    /// <summary>The standing a free agent of this age joins a squad with (`FA-7`).</summary>
    /// <param name="age">His age in game years.</param>
    public static SquadStatus SquadStatusFor(int age) =>
        age <= WorldRuleSet.FreeAgentProspectAgeMax ? SquadStatus.Prospect : SquadStatus.Rotation;
}
