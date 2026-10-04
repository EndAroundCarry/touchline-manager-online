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

    /// <summary>A player's training programme override was set (`TRN-1`, `TRN-2`).</summary>
    public const string PlayerTrainingProgrammeSet = "squad.player_training_programme.set";

    /// <summary>A player's training programme override was cleared (`TRN-1`, `TRN-2`).</summary>
    public const string PlayerTrainingProgrammeCleared = "squad.player_training_programme.cleared";

    /// <summary>A club prepared or replaced its side for a fixture (`SQ-4`, `CAL-3`).</summary>
    public const string FixtureTeamSheetSaved = "squad.fixture_team_sheet.saved";

    /// <summary>A player was re-signed, closing the old contract and opening a new one (`CON-3`, `CON-4`).</summary>
    public const string ContractRenewed = "squad.contract.renewed";

    /// <summary>
    /// An emergency replacement was created because a club fell below the minimum squad (`SQ-8`).
    /// </summary>
    /// <remarks>The safety net is audited because it is a repair rather than a normal way to build a squad.</remarks>
    public const string EmergencyReplacement = "squad.emergency_replacement.created";
}
