using FluentValidation;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad.Validation;

/// <summary>
/// Validates a submitted training plan's field-level shape (master plan §10.4; `TRN-1`).
/// </summary>
/// <remarks>
/// Only the intensity code needs checking: everything else about a plan is server-decided (its club, its
/// version, its effective date). Whether a code names a real intensity is a field-level question, so it is
/// answered here rather than in the use case.
/// </remarks>
public sealed class SaveTrainingRequestValidator : AbstractValidator<SaveTrainingRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SaveTrainingRequestValidator()
    {
        RuleFor(request => request.Intensity)
            .Must(IsKnownIntensity)
            .WithMessage("Choose a supported training intensity.");
    }

    private static bool IsKnownIntensity(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            TrainingPlans.IntensityFromCode(code);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

/// <summary>
/// Validates a submitted training programme's field-level shape (`TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// A null or empty programme is valid: it clears the override. Anything else must name a programme in the
/// catalogue.
/// </remarks>
public sealed class SetPlayerTrainingProgrammeRequestValidator : AbstractValidator<SetPlayerTrainingProgrammeRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SetPlayerTrainingProgrammeRequestValidator()
    {
        RuleFor(request => request.Programme)
            .Must(IsClearOrKnownProgramme)
            .WithMessage("Choose a supported training programme, or send nothing to use the position default (TRN-1).");
    }

    private static bool IsClearOrKnownProgramme(string? code) =>
        string.IsNullOrWhiteSpace(code) || TrainingProgrammes.TryFromCode(code, out _);
}
