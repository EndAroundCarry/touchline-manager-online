using FluentValidation;
using TouchlineManager.Contracts.Market;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Application.Market.Validation;

/// <summary>Validates a request to list a player (`TRF-1`, `CON-1`).</summary>
public sealed class CreateListingRequestValidator : AbstractValidator<CreateListingRequest>
{
    /// <summary>Initializes the validator.</summary>
    public CreateListingRequestValidator()
    {
        RuleFor(request => request.PlayerId)
            .NotEmpty()
            .WithMessage("Name the player to list.");

        RuleFor(request => request.MinimumFeeMinor)
            .GreaterThan(0)
            .WithMessage("A minimum fee must be positive (TRF-1).");

        RuleFor(request => request.Seasons)
            .InclusiveBetween(WorldRuleSet.ContractMinSeasons, WorldRuleSet.ContractMaxSeasons)
            .WithMessage($"A contract is between {WorldRuleSet.ContractMinSeasons} and {WorldRuleSet.ContractMaxSeasons} game seasons (CON-1).");
    }
}

/// <summary>Validates a request to place or raise a bid (`TRF-4`).</summary>
public sealed class PlaceBidRequestValidator : AbstractValidator<PlaceBidRequest>
{
    /// <summary>Initializes the validator.</summary>
    public PlaceBidRequestValidator() =>
        RuleFor(request => request.AmountMinor)
            .GreaterThan(0)
            .WithMessage("A bid must be a positive amount (TRF-4).");
}

/// <summary>Validates a request to shortlist a player (`SCT-3`).</summary>
public sealed class ShortlistRequestValidator : AbstractValidator<ShortlistRequest>
{
    /// <summary>Initializes the validator.</summary>
    public ShortlistRequestValidator() =>
        RuleFor(request => request.Notes)
            .MaximumLength(WorldRuleSet.ShortlistNotesMaxLength)
            .When(request => request.Notes is not null)
            .WithMessage($"A shortlist note is at most {WorldRuleSet.ShortlistNotesMaxLength} characters (SCT-3).");
}
