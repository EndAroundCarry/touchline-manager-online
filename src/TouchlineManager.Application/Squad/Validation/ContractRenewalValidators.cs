using FluentValidation;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Squad.Validation;

/// <summary>
/// Validates the length a renewal quote was asked for (`CON-1`, `CON-3`).
/// </summary>
/// <remarks>
/// The only field-level fact is the term: the contract, the player, and the price are all server-decided.
/// </remarks>
public sealed class RenewalQuoteRequestValidator : AbstractValidator<RenewalQuoteRequest>
{
    /// <summary>Initializes the validator.</summary>
    public RenewalQuoteRequestValidator() =>
        RuleFor(request => request.Seasons)
            .InclusiveBetween(WorldRuleSet.ContractMinSeasons, WorldRuleSet.ContractMaxSeasons)
            .WithMessage(
                $"A contract is between {WorldRuleSet.ContractMinSeasons} and {WorldRuleSet.ContractMaxSeasons} game seasons (CON-1).");
}

/// <summary>Validates the length a renewal would sign (`CON-1`, `CON-4`).</summary>
public sealed class RenewContractRequestValidator : AbstractValidator<RenewContractRequest>
{
    /// <summary>Initializes the validator.</summary>
    public RenewContractRequestValidator() =>
        RuleFor(request => request.Seasons)
            .InclusiveBetween(WorldRuleSet.ContractMinSeasons, WorldRuleSet.ContractMaxSeasons)
            .WithMessage(
                $"A contract is between {WorldRuleSet.ContractMinSeasons} and {WorldRuleSet.ContractMaxSeasons} game seasons (CON-1).");
}
