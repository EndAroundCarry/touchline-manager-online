using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Domain.Market;

/// <summary>What a free agent costs and what he signs for (`FA-6`, `FA-7`).</summary>
/// <param name="MinimumFeeMinor">The least a bid can be, in minor units. The fee leaves the game.</param>
/// <param name="WeeklyWageMinor">The weekly wage the signing club pays, frozen on the listing (`CON-5`).</param>
/// <param name="ContractSeasons">The length of the contract, in game seasons.</param>
/// <param name="SquadStatus">The standing the player joins the squad with.</param>
public sealed record FreeAgentOffer(
    long MinimumFeeMinor,
    long WeeklyWageMinor,
    int ContractSeasons,
    SquadStatus SquadStatus);

/// <summary>
/// The terms of a free agent, a pure function of the ability he shows, his age and the reference tier
/// (`FA-6`, `FA-7`).
/// </summary>
/// <remarks>
/// <para>
/// A generated free agent's hidden potential is never an input. The valuation and the wage are both priced with
/// potential set equal to ability, so a hidden gem costs and earns exactly what a normal player of the same
/// ability and age does, and the price cannot give him away.
/// </para>
/// <para>
/// A released player is a different case: he is a former club player, listed like any club's player, so his fee is
/// his full valuation with his real potential, as a club listing's asking price is (`FA-6`). His wage is still
/// priced on ability alone.
/// </para>
/// <para>
/// The tier is the reference tier, the lowest active tier of the player's country, because a free agent has no
/// club to price him against. The wage uses the renewal quote with neutral facts: no appearances, neutral
/// morale, and a term that is neither running out nor long.
/// </para>
/// </remarks>
public static class FreeAgentTerms
{
    /// <summary>Calculates the terms a free agent is listed on.</summary>
    /// <param name="ability">The ability he shows, on the 1–20 scale.</param>
    /// <param name="age">His age in game years.</param>
    /// <param name="referenceTier">The reference tier, which is 1 or more.</param>
    public static FreeAgentOffer Calculate(int ability, int age, int referenceTier) =>
        Calculate(ability, ability, age, referenceTier, WorldRuleSet.FreeAgentMinimumFeeBp);

    /// <summary>Calculates the terms a released player is listed on: his full valuation, with his real potential.</summary>
    /// <param name="ability">The ability he shows, on the 1–20 scale.</param>
    /// <param name="potential">His hidden potential, on the 1–20 scale. It prices his fee and nothing else.</param>
    /// <param name="age">His age in game years.</param>
    /// <param name="referenceTier">The reference tier, which is 1 or more.</param>
    public static FreeAgentOffer CalculateReleased(int ability, int potential, int age, int referenceTier) =>
        Calculate(ability, potential, age, referenceTier, WorldRuleSet.FreeAgentReleasedFeeBp);

    private static FreeAgentOffer Calculate(int ability, int potential, int age, int referenceTier, int feeBp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ability, WorldRuleSet.AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ability, WorldRuleSet.AttributeMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(potential, WorldRuleSet.AttributeMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(potential, WorldRuleSet.AttributeMax);
        ArgumentOutOfRangeException.ThrowIfNegative(age);
        ArgumentOutOfRangeException.ThrowIfLessThan(referenceTier, 1);

        var valuation = PlayerValuation.Estimate(new PlayerValuationInput(ability, potential, age, referenceTier));

        var minimumFee = Math.Max(
            WorldRuleSet.MinimumBidIncrementMinor,
            valuation * feeBp / 10_000);

        var seasons = WorldRuleSet.FreeAgentContractSeasonsFor(age);

        var wage = ContractRenewalQuote.Calculate(
            new ContractRenewalInput(
                ability,
                ability,
                age,
                Appearances: 0,
                PlayerState.NeutralBasisPoints,
                referenceTier,
                RemainingSeasons: 2),
            seasons);

        return new FreeAgentOffer(
            minimumFee,
            wage.WeeklyWageMinor,
            seasons,
            SquadStatusFor(age));
    }

    /// <summary>The standing a free agent of this age joins a squad with (`FA-7`).</summary>
    /// <param name="age">His age in game years.</param>
    public static SquadStatus SquadStatusFor(int age) =>
        age <= WorldRuleSet.FreeAgentProspectAgeMax ? SquadStatus.Prospect : SquadStatus.Rotation;
}
