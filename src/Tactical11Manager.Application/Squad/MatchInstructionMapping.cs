using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Squad;

/// <summary>
/// Converts a plan's match instructions between their transport shape and the domain's (`MAT-6`).
/// </summary>
/// <remarks>
/// The inbound conversion uses the throwing parsers because the request validator has already checked every code; a use case is only ever
/// reached with a request that passed it. The outbound one is total.
/// </remarks>
public static class MatchInstructionMapping
{
    /// <summary>Reads a submitted instruction as the domain value the validator and the entity work on.</summary>
    /// <param name="model">The submitted instruction.</param>
    public static MatchInstructionDefinition ToDefinition(this MatchInstructionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var changes = model.Changes ?? new MatchInstructionChangesModel();

        return new MatchInstructionDefinition
        {
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            FromMinute = model.FromMinute,
            UntilMinute = model.UntilMinute,
            Score = MatchInstructionCodes.ScoreFromCode(model.Score),
            ScoreMargin = model.ScoreMargin,
            Event = MatchInstructionCodes.EventFromCode(model.Event),
            EventFamily = model.EventFamily is { } family ? PositionFamilies.FromCode(family) : null,
            EventThreshold = model.EventThreshold,
            Formation = model.Formation is { } formation ? FormationPresets.FromCode(formation) : null,
            Overrides = [.. (model.Overrides ?? []).Select(placement => new MatchInstructionOverride(placement.PlayerId, placement.SlotNumber))],
            Changes = new MatchInstructionChanges
            {
                Mentality = changes.Mentality is { } mentality ? TeamInstructions.MentalityFromCode(mentality) : null,
                MentalitySteps = changes.MentalitySteps,
                Tempo = changes.Tempo is { } tempo ? TeamInstructions.TempoFromCode(tempo) : null,
                Passing = changes.Passing is { } passing ? TeamInstructions.PassingFromCode(passing) : null,
                Width = changes.Width is { } width ? TeamInstructions.WidthFromCode(width) : null,
                PassFocus = changes.PassFocus is { } passFocus ? TeamInstructions.PassFocusFromCode(passFocus) : null,
                Pressing = changes.Pressing is { } pressing ? TeamInstructions.PressingFromCode(pressing) : null,
                DefensiveLine = changes.DefensiveLine is { } line ? TeamInstructions.LineFromCode(line) : null,
                Tackling = changes.Tackling is { } tackling ? TeamInstructions.TacklingFromCode(tackling) : null,
                TimeWasting = changes.TimeWasting is { } timeWasting ? TeamInstructions.TimeWastingFromCode(timeWasting) : null,
                CounterAttack = changes.CounterAttack,
            },
            Substitutions =
            [
                .. (model.Substitutions ?? []).Select(substitution => new MatchInstructionSubstitution(
                    MatchInstructionCodes.OffFromCode(substitution.Off),
                    substitution.OffPlayerId,
                    substitution.OffFamily is { } offFamily ? PositionFamilies.FromCode(offFamily) : null,
                    MatchInstructionCodes.OnFromCode(substitution.On),
                    substitution.OnPlayerId,
                    substitution.OnFamily is { } onFamily ? PositionFamilies.FromCode(onFamily) : null)),
            ],
        };
    }

    /// <summary>Projects an instruction as the transport shape.</summary>
    /// <param name="definition">The instruction.</param>
    public static MatchInstructionModel ToModel(this MatchInstructionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var changes = definition.Changes ?? MatchInstructionChanges.None;

        return new MatchInstructionModel
        {
            Name = definition.Name,
            IsEnabled = definition.IsEnabled,
            FromMinute = definition.FromMinute,
            UntilMinute = definition.UntilMinute,
            Score = definition.Score.ToCode(),
            ScoreMargin = definition.ScoreMargin,
            Event = definition.Event.ToCode(),
            EventFamily = definition.EventFamily?.ToCode(),
            EventThreshold = definition.EventThreshold,
            Formation = definition.Formation?.ToCode(),
            Overrides =
            [
                .. definition.Overrides.Select(placement => new MatchInstructionOverrideModel
                {
                    PlayerId = placement.PlayerId,
                    SlotNumber = placement.SlotNumber,
                }),
            ],
            Changes = new MatchInstructionChangesModel
            {
                Mentality = changes.Mentality?.ToCode(),
                MentalitySteps = changes.MentalitySteps,
                Tempo = changes.Tempo?.ToCode(),
                Passing = changes.Passing?.ToCode(),
                Width = changes.Width?.ToCode(),
                PassFocus = changes.PassFocus?.ToCode(),
                Pressing = changes.Pressing?.ToCode(),
                DefensiveLine = changes.DefensiveLine?.ToCode(),
                Tackling = changes.Tackling?.ToCode(),
                TimeWasting = changes.TimeWasting?.ToCode(),
                CounterAttack = changes.CounterAttack,
            },
            Substitutions =
            [
                .. definition.Substitutions.Select(substitution => new MatchInstructionSubstitutionModel
                {
                    Off = substitution.Off.ToCode(),
                    OffPlayerId = substitution.OffPlayerId,
                    OffFamily = substitution.OffFamily?.ToCode(),
                    On = substitution.On.ToCode(),
                    OnPlayerId = substitution.OnPlayerId,
                    OnFamily = substitution.OnFamily?.ToCode(),
                }),
            ],
        };
    }

    /// <summary>Projects one warning.</summary>
    /// <param name="warning">The warning.</param>
    public static MatchInstructionWarningResponse ToResponse(this MatchInstructionWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return new MatchInstructionWarningResponse(warning.Code.ToCode(), warning.Ordinal, warning.PlayerId);
    }

    /// <summary>Projects one issue in the vocabulary the plan's other issues use.</summary>
    /// <param name="issue">The issue.</param>
    public static TacticalPlanIssueResponse ToResponse(this MatchInstructionIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return new TacticalPlanIssueResponse(issue.Code.ToCode(), null, issue.PlayerId, issue.Ordinal);
    }
}
