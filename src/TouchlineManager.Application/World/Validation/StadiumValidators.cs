using FluentValidation;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World.Validation;

/// <summary>
/// Validates a stadium build order's field-level shape (`STAD-4`).
/// </summary>
/// <remarks>
/// Only the stand code and the size of the order are field-level questions. Whether the ground has room for
/// the places and whether the club can pay for them depend on state, so the use case answers those.
/// </remarks>
public sealed class BuildSeatsRequestValidator : AbstractValidator<BuildSeatsRequest>
{
    /// <summary>Initializes the validator.</summary>
    public BuildSeatsRequestValidator()
    {
        RuleFor(request => request.Stand)
            .Must(code => StadiumStands.TryFromCode(code, out _))
            .WithMessage("Choose standing, seating, covered_seating or vip.");

        RuleFor(request => request.Count)
            .InclusiveBetween(1, StadiumRuleSet.MaxSeatsPerOrder)
            .WithMessage($"Order between 1 and {StadiumRuleSet.MaxSeatsPerOrder:N0} places.");
    }
}
