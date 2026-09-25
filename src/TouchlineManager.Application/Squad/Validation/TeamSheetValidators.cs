using FluentValidation;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad.Validation;

/// <summary>
/// Validates a submitted fixture team sheet's field-level shape (master plan §10.4; `SQ-4`).
/// </summary>
/// <remarks>
/// This checks only what a field can be wrong about — a missing selection, a slot number outside 1–18, a
/// repeated slot, an empty player. The rules that need to know the squad — whether a named player is
/// selectable or available, and whether the eleven starting slots are full — belong to
/// <see cref="FixtureTeamSheetValidator"/>, which the use case runs afterwards. Splitting them keeps a
/// malformed request from reaching the domain at all, and keeps the side's rules in one place.
/// </remarks>
public sealed class SaveFixtureTeamSheetRequestValidator : AbstractValidator<SaveFixtureTeamSheetRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SaveFixtureTeamSheetRequestValidator()
    {
        RuleFor(request => request.Selection)
            .NotNull().WithMessage("Send a selection.")
            .Must(selection => selection is { Count: > 0 }).WithMessage("A side needs at least a starting eleven.")
            .Must(selection => selection is null
                || selection.Select(entry => entry.SlotNumber).Distinct().Count() == selection.Count)
            .WithMessage("Two entries name the same slot.");

        RuleForEach(request => request.Selection).ChildRules(entry =>
        {
            entry.RuleFor(candidate => candidate.SlotNumber)
                .InclusiveBetween(TeamSheetEntry.FirstSlotNumber, TeamSheetEntry.LastSlotNumber)
                .WithMessage($"A slot number is between {TeamSheetEntry.FirstSlotNumber} and {TeamSheetEntry.LastSlotNumber} (SQ-4).");
            entry.RuleFor(candidate => candidate.PlayerId).NotEmpty();
        });
    }
}
