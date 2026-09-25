namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// Recorded audit actions for the squad module.
/// </summary>
/// <remarks>
/// A tactic is a manager's decision about how their club plays, and a default plan is what the engine
/// will use when nothing more specific exists. Which shape was in force, and who saved it, has to be
/// answerable when a result is questioned (`INS-11`, master plan §12.3), so every write is audited.
/// </remarks>
public static class SquadAuditActions
{
    /// <summary>A tactical plan was created.</summary>
    public const string TacticalPlanCreated = "squad.tactical_plan.created";

    /// <summary>A tactical plan was revised.</summary>
    public const string TacticalPlanUpdated = "squad.tactical_plan.updated";

    /// <summary>A tactical plan became the club's default.</summary>
    public const string TacticalPlanMadeDefault = "squad.tactical_plan.made_default";

    /// <summary>A club's training plan was set for the first time.</summary>
    public const string TrainingPlanCreated = "squad.training_plan.created";

    /// <summary>A club's training plan was revised.</summary>
    public const string TrainingPlanUpdated = "squad.training_plan.updated";

    /// <summary>A player's individual training focus was set (`TRN-2`).</summary>
    public const string PlayerTrainingFocusSet = "squad.player_training_focus.set";

    /// <summary>A player's individual training focus was cleared (`TRN-2`).</summary>
    public const string PlayerTrainingFocusCleared = "squad.player_training_focus.cleared";

    /// <summary>A club prepared or replaced its side for a fixture (`SQ-4`, `CAL-3`).</summary>
    public const string FixtureTeamSheetSaved = "squad.fixture_team_sheet.saved";
}
