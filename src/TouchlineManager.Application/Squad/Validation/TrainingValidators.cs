using FluentValidation;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad.Validation;

/// <summary>
/// Validates a submitted training plan's field-level shape (master plan §10.4; `TRN-1`).
/// </summary>
/// <remarks>
/// Only the two codes need checking: everything else about a plan is server-decided (its club, its version,
/// its effective date). Whether a code names a real focus or intensity is a field-level question, so it is
/// answered here rather than in the use case.
/// </remarks>
public sealed class SaveTrainingRequestValidator : AbstractValidator<SaveTrainingRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SaveTrainingRequestValidator()
    {
        RuleFor(request => request.TeamFocus)
            .Must(IsKnown(TrainingPlans.FromCode))
            .WithMessage("Choose a supported training focus (TRN-1).");

        RuleFor(request => request.Intensity)
            .Must(IsKnown(TrainingPlans.IntensityFromCode))
            .WithMessage("Choose a supported training intensity.");
    }

    private static Func<string?, bool> IsKnown<T>(Func<string, T> parse) => code =>
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            parse(code);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    };
}

/// <summary>
/// Validates a submitted individual training focus's field-level shape (`TRN-2`).
/// </summary>
/// <remarks>
/// A null or empty family is valid: it clears the focus. Anything else must name one attribute family.
/// </remarks>
public sealed class SetPlayerTrainingFocusRequestValidator : AbstractValidator<SetPlayerTrainingFocusRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SetPlayerTrainingFocusRequestValidator()
    {
        RuleFor(request => request.FocusFamily)
            .Must(IsClearOrKnownFamily)
            .WithMessage("Choose a supported attribute family, or send nothing to clear the focus (TRN-2).");
    }

    private static bool IsClearOrKnownFamily(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return true;
        }

        try
        {
            AttributeFamilies.FromCode(code);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
