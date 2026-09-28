using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>Why a renewal quote or a renewal succeeded or was refused (`CON-3`, `CON-4`).</summary>
public enum ContractRenewalOutcome
{
    /// <summary>A quote was produced.</summary>
    Quoted = 0,

    /// <summary>The player was re-signed.</summary>
    Renewed = 1,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 2,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 3,

    /// <summary>The manager holds no club.</summary>
    NoClub = 4,

    /// <summary>The club an existing contract belongs to no longer exists.</summary>
    ClubNotFound = 5,

    /// <summary>No active contract with that identity belongs to the caller's club.</summary>
    ContractNotFound = 6,

    /// <summary>The requested length is not one to three game seasons (`CON-1`).</summary>
    InvalidTerm = 7,

    /// <summary>The command is conditional and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 8,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 9,
}

/// <summary>The result of asking for a renewal quote.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Quote">The quoted terms, when one was produced.</param>
public sealed record RequestRenewalQuoteResult(ContractRenewalOutcome Outcome, RenewalQuoteResponse? Quote);

/// <summary>The result of accepting a renewal.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Contract">The new contract, when the player was re-signed.</param>
public sealed record RenewContractResult(ContractRenewalOutcome Outcome, ContractRenewalResponse? Contract);

/// <summary>Shared helpers for the two renewal use cases, so neither restates the other's rules.</summary>
internal static class ContractRenewal
{
    /// <summary>Whether a proposed length is one the contract aggregate accepts (`CON-1`).</summary>
    public static bool IsLegalTerm(int seasons) =>
        seasons is >= WorldRuleSet.ContractMinSeasons and <= WorldRuleSet.ContractMaxSeasons;

    /// <summary>Turns the server-only context into the quote's pure input (`CON-3`).</summary>
    public static ContractRenewalInput ToInput(ContractRenewalContext context) =>
        new(
            context.Ability,
            context.Potential,
            context.Age,
            context.Appearances,
            context.MoraleBp,
            context.TierNumber,
            context.RemainingSeasons);

    /// <summary>Carries an ownership refusal over into the renewal vocabulary.</summary>
    public static ContractRenewalOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => ContractRenewalOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => ContractRenewalOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => ContractRenewalOutcome.NoClub,
        _ => ContractRenewalOutcome.ClubNotFound,
    };
}
