using FluentValidation;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Squad.Validation;

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
        RuleFor(request => request.PassFocus).Must(IsPassFocus).WithMessage("Unknown pass focus.");
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

        When(request => request.Bench is not null, () =>
        {
            // As with the lineup, whether seven places are filled and whether one is a goalkeeper are rules
            // about the squad, so the domain validator answers them with issues the screen can point at.
            RuleFor(request => request.Bench!)
                .Must(bench => bench.Select(entry => entry.SlotNumber).Distinct().Count() == bench.Count)
                .WithMessage("Two bench entries name the same slot.");

            RuleForEach(request => request.Bench!).ChildRules(entry =>
            {
                entry.RuleFor(candidate => candidate.SlotNumber)
                    .InclusiveBetween(TacticalBenchSlot.FirstSlotNumber, TacticalBenchSlot.LastSlotNumber);
                entry.RuleFor(candidate => candidate.PlayerId).NotEmpty();
            });
        });

        When(request => request.MatchInstructions is not null, () =>
        {
            // Only what a field can be wrong about: a code that names nothing, a name that is empty or too long, a list
            // that is too long. Whether a minute is in range, an event has the parameter it needs, or a named player is in
            // the plan's lineup is the domain validator's, so a club's AI plan is held to the same rules (INS-12).
            RuleFor(request => request.MatchInstructions!)
                .Must(instructions => instructions.Count <= MatchInstruction.MaxPerPlan)
                .WithMessage($"A plan has at most {MatchInstruction.MaxPerPlan} match instructions.");

            RuleForEach(request => request.MatchInstructions!).ChildRules(instruction =>
            {
                instruction.RuleFor(candidate => candidate.Name)
                    .NotEmpty().WithMessage("Give the instruction a name.")
                    .MaximumLength(MatchInstruction.MaxNameLength);
                instruction.RuleFor(candidate => candidate.Score).Must(IsScore).WithMessage("Unknown score condition.");
                instruction.RuleFor(candidate => candidate.Event).Must(IsEvent).WithMessage("Unknown event.");
                instruction.RuleFor(candidate => candidate.EventFamily).Must(IsPositionFamilyOrNone)
                    .WithMessage("Unknown position family.");
                instruction.RuleFor(candidate => candidate.Formation).Must(IsFormationPresetOrNone)
                    .WithMessage("Choose a supported formation.");

                instruction.When(candidate => candidate.Changes is not null, () =>
                {
                    instruction.RuleFor(candidate => candidate.Changes.Mentality).Must(IsMentalityOrNone)
                        .WithMessage("Unknown mentality.");
                    instruction.RuleFor(candidate => candidate.Changes.Tempo).Must(IsTempoOrNone).WithMessage("Unknown tempo.");
                    instruction.RuleFor(candidate => candidate.Changes.Passing).Must(IsPassingOrNone)
                        .WithMessage("Unknown passing style.");
                    instruction.RuleFor(candidate => candidate.Changes.Width).Must(IsWidthOrNone).WithMessage("Unknown width.");
                    instruction.RuleFor(candidate => candidate.Changes.PassFocus).Must(IsPassFocusOrNone)
                        .WithMessage("Unknown pass focus.");
                    instruction.RuleFor(candidate => candidate.Changes.Pressing).Must(IsPressingOrNone)
                        .WithMessage("Unknown pressing scheme.");
                    instruction.RuleFor(candidate => candidate.Changes.DefensiveLine).Must(IsDefensiveLineOrNone)
                        .WithMessage("Unknown defensive line.");
                    instruction.RuleFor(candidate => candidate.Changes.Tackling).Must(IsTacklingOrNone)
                        .WithMessage("Unknown tackling style.");
                    instruction.RuleFor(candidate => candidate.Changes.TimeWasting).Must(IsTimeWastingOrNone)
                        .WithMessage("Unknown time-wasting setting.");
                });

                instruction.When(candidate => candidate.Substitutions is not null, () =>
                {
                    instruction.RuleForEach(candidate => candidate.Substitutions).ChildRules(substitution =>
                    {
                        substitution.RuleFor(candidate => candidate.Off).Must(IsOff).WithMessage("Unknown selector.");
                        substitution.RuleFor(candidate => candidate.On).Must(IsOn).WithMessage("Unknown selector.");
                        substitution.RuleFor(candidate => candidate.OffFamily).Must(IsPositionFamilyOrNone)
                            .WithMessage("Unknown position family.");
                        substitution.RuleFor(candidate => candidate.OnFamily).Must(IsPositionFamilyOrNone)
                            .WithMessage("Unknown position family.");
                    });
                });
            });
        });
    }

    private static bool IsScore(string? code) => IsKnown(MatchInstructionCodes.ScoreFromCode, code);

    private static bool IsEvent(string? code) => IsKnown(MatchInstructionCodes.EventFromCode, code);

    private static bool IsOff(string? code) => IsKnown(MatchInstructionCodes.OffFromCode, code);

    private static bool IsOn(string? code) => IsKnown(MatchInstructionCodes.OnFromCode, code);

    private static bool IsPositionFamilyOrNone(string? code) => code is null || IsPositionFamily(code);

    private static bool IsFormationPresetOrNone(string? code) => code is null || IsFormationPreset(code);

    private static bool IsMentalityOrNone(string? code) => code is null || IsMentality(code);

    private static bool IsTempoOrNone(string? code) => code is null || IsTempo(code);

    private static bool IsPassingOrNone(string? code) => code is null || IsPassing(code);

    private static bool IsWidthOrNone(string? code) => code is null || IsWidth(code);

    private static bool IsPassFocusOrNone(string? code) => code is null || IsPassFocus(code);

    private static bool IsPressingOrNone(string? code) => code is null || IsPressing(code);

    private static bool IsDefensiveLineOrNone(string? code) => code is null || IsDefensiveLine(code);

    private static bool IsTacklingOrNone(string? code) => code is null || IsTackling(code);

    private static bool IsTimeWastingOrNone(string? code) => code is null || IsTimeWasting(code);

    private static bool IsFormationPreset(string? code) =>
        IsKnown(FormationPresets.FromCode, code);

    private static bool IsPositionFamily(string? code) =>
        IsKnown(PositionFamilies.FromCode, code);

    private static bool IsRole(string? code) => IsKnown(PlayerRoles.FromCode, code);

    private static bool IsMentality(string? code) => IsKnown(TeamInstructions.MentalityFromCode, code);

    private static bool IsTempo(string? code) => IsKnown(TeamInstructions.TempoFromCode, code);

    private static bool IsPassing(string? code) => IsKnown(TeamInstructions.PassingFromCode, code);

    private static bool IsWidth(string? code) => IsKnown(TeamInstructions.WidthFromCode, code);

    private static bool IsPassFocus(string? code) => IsKnown(TeamInstructions.PassFocusFromCode, code);

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
