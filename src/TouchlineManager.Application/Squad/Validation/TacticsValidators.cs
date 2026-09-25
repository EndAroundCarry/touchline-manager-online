using FluentValidation;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Application.Squad.Validation;

/// <summary>
/// Validates a submitted tactical plan's field-level shape (master plan §10.4; `TAC-7`…`TAC-9`).
/// </summary>
/// <remarks>
/// <para>
/// This checks only what a field can be wrong about — a code that names no enum value, a count, a
/// coordinate outside the pitch. The rules that need to know the squad — whether an assigned player is
/// selectable or available, and whether the eleven slots are full — belong to
/// <see cref="TacticalPlanValidator"/>, which the use case runs afterwards. Splitting them keeps a
/// malformed request from reaching the domain at all, and keeps the pitch's rules in one place.
/// </para>
/// </remarks>
public sealed class SaveTacticalPlanRequestValidator : AbstractValidator<SaveTacticalPlanRequest>
{
    /// <summary>Initializes the validator.</summary>
    public SaveTacticalPlanRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Give the plan a name.")
            .MaximumLength(TacticalPlan.MaxNameLength);

        RuleFor(request => request.FormationPreset)
            .Must(IsFormationPreset)
            .WithMessage("Choose a supported formation.");

        RuleFor(request => request.Mentality).Must(IsMentality).WithMessage("Unknown mentality.");
        RuleFor(request => request.Tempo).Must(IsTempo).WithMessage("Unknown tempo.");
        RuleFor(request => request.Passing).Must(IsPassing).WithMessage("Unknown passing style.");
        RuleFor(request => request.Width).Must(IsWidth).WithMessage("Unknown width.");
        RuleFor(request => request.Pressing).Must(IsPressing).WithMessage("Unknown pressing scheme.");
        RuleFor(request => request.DefensiveLine).Must(IsDefensiveLine).WithMessage("Unknown defensive line.");
        RuleFor(request => request.Tackling).Must(IsTackling).WithMessage("Unknown tackling style.");
        RuleFor(request => request.TimeWasting).Must(IsTimeWasting).WithMessage("Unknown time-wasting setting.");

        When(request => request.Slots is not null, () =>
        {
            RuleFor(request => request.Slots!)
                .Must(slots => slots.Count == FormationLayouts.SlotCount)
                .WithMessage($"A plan has exactly {FormationLayouts.SlotCount} slots (SQ-4).")
                .Must(slots => slots.Select(slot => slot.SlotNumber).Distinct().Count() == slots.Count)
                .WithMessage("Two slots share a number.");

            RuleForEach(request => request.Slots!).ChildRules(slot =>
            {
                slot.RuleFor(candidate => candidate.SlotNumber)
                    .InclusiveBetween(TacticalSlot.FirstSlotNumber, TacticalSlot.LastSlotNumber);
                slot.RuleFor(candidate => candidate.PositionFamily)
                    .Must(IsPositionFamily)
                    .WithMessage("Unknown position family.");
                slot.RuleFor(candidate => candidate.Role).Must(IsRole).WithMessage("Unknown role.");
                slot.RuleFor(candidate => candidate.NormalizedX)
                    .InclusiveBetween(WorldRuleSet.SlotCoordinateMin, WorldRuleSet.SlotCoordinateMax);
                slot.RuleFor(candidate => candidate.NormalizedY)
                    .InclusiveBetween(WorldRuleSet.SlotCoordinateMin, WorldRuleSet.SlotCoordinateMax);
            });
        });

        When(request => request.Lineup is not null, () =>
        {
            // Completeness is deliberately not checked here. Whether eleven slots are filled is a rule about
            // the pitch rather than about a field (SQ-4), and the domain validator answers it with the
            // SELECTION_INCOMPLETE issue the screen can point at, rather than a bare field message.
            RuleFor(request => request.Lineup!)
                .Must(lineup => lineup.Select(entry => entry.SlotNumber).Distinct().Count() == lineup.Count)
                .WithMessage("Two lineup entries name the same slot.");

            RuleForEach(request => request.Lineup!).ChildRules(entry =>
            {
                entry.RuleFor(candidate => candidate.SlotNumber)
                    .InclusiveBetween(TacticalSlot.FirstSlotNumber, TacticalSlot.LastSlotNumber);
                entry.RuleFor(candidate => candidate.PlayerId).NotEmpty();
            });
        });
    }

    private static bool IsFormationPreset(string? code) =>
        IsKnown(FormationPresets.FromCode, code);

    private static bool IsPositionFamily(string? code) =>
        IsKnown(PositionFamilies.FromCode, code);

    private static bool IsRole(string? code) => IsKnown(PlayerRoles.FromCode, code);

    private static bool IsMentality(string? code) => IsKnown(TeamInstructions.MentalityFromCode, code);

    private static bool IsTempo(string? code) => IsKnown(TeamInstructions.TempoFromCode, code);

    private static bool IsPassing(string? code) => IsKnown(TeamInstructions.PassingFromCode, code);

    private static bool IsWidth(string? code) => IsKnown(TeamInstructions.WidthFromCode, code);

    private static bool IsPressing(string? code) => IsKnown(TeamInstructions.PressingFromCode, code);

    private static bool IsDefensiveLine(string? code) => IsKnown(TeamInstructions.LineFromCode, code);

    private static bool IsTackling(string? code) => IsKnown(TeamInstructions.TacklingFromCode, code);

    private static bool IsTimeWasting(string? code) => IsKnown(TeamInstructions.TimeWastingFromCode, code);

    private static bool IsKnown<T>(Func<string, T> parse, string? code)
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
    }
}
