using FluentValidation;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application.World.Validation;

/// <summary>The shared field rules for world and onboarding requests.</summary>
public static class WorldValidationRules
{
    /// <summary>Maximum accepted locale length, e.g. <c>en-GB</c>.</summary>
    public const int LocaleMaxLength = 16;

    /// <summary>Maximum accepted IANA time-zone length, e.g. <c>America/Argentina/Buenos_Aires</c>.</summary>
    public const int TimeZoneMaxLength = 64;

    /// <summary>Applies the locale rules: a language tag with a region, which is what the client renders dates with.</summary>
    public static IRuleBuilderOptions<T, string> LocaleRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("A locale is required.")
        .MaximumLength(LocaleMaxLength)
        .Matches("^[a-z]{2,3}-[A-Z]{2}$")
        .WithMessage("Use a locale such as en-GB.");

    /// <summary>
    /// Applies the time-zone rules.
    /// </summary>
    /// <remarks>
    /// The zone is checked against the runtime's own database rather than against a pattern, because a
    /// deadline rendered in a zone that does not exist would silently fall back to UTC and a manager would
    /// see the wrong lock time without anything reporting it (`CAL-4`).
    /// </remarks>
    public static IRuleBuilderOptions<T, string> TimeZoneRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("A time zone is required.")
        .MaximumLength(TimeZoneMaxLength)
        .Must(zone => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
        .WithMessage("Use an IANA time zone such as Europe/London.");
}

/// <summary>Validates <see cref="CreateManagerProfileRequest"/>.</summary>
public sealed class CreateManagerProfileRequestValidator : AbstractValidator<CreateManagerProfileRequest>
{
    /// <summary>Initializes the validator.</summary>
    public CreateManagerProfileRequestValidator()
    {
        RuleFor(request => request.Locale).LocaleRules();
        RuleFor(request => request.TimeZone).TimeZoneRules();
    }
}

/// <summary>Validates <see cref="ClaimClubRequest"/>.</summary>
public sealed class ClaimClubRequestValidator : AbstractValidator<ClaimClubRequest>
{
    /// <summary>Initializes the validator.</summary>
    public ClaimClubRequestValidator() =>
        RuleFor(request => request.ClubId)
            .NotEmpty()
            .WithMessage("Choose the club you want to manage.");
}
