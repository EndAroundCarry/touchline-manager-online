namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>
/// Recorded audit actions for the world and onboarding module.
/// </summary>
/// <remarks>
/// Onboarding events are audited because they are ownership events. "Which manager held this club on
/// this date, and who decided that" has to be answerable years later, and a tenure row alone cannot say
/// who made the request or from where (`OCC-7`, master plan §12.3).
/// </remarks>
public static class WorldAuditActions
{
    /// <summary>A world was seeded from a generation seed.</summary>
    public const string WorldSeeded = "world.seeded";

    /// <summary>A manager paid for new places in the club's stadium (`STAD-4`).</summary>
    public const string StadiumSeatsBuilt = "world.club_stadium.seats_built";

    /// <summary>A manager profile was created.</summary>
    public const string ManagerProfileCreated = "world.manager_profile.created";

    /// <summary>Manager preferences were changed.</summary>
    public const string ManagerPreferencesChanged = "world.manager_profile.preferences_changed";

    /// <summary>A club was taken over.</summary>
    public const string ClubClaimed = "world.club_tenure.claimed";

    /// <summary>A manager resigned from a club.</summary>
    public const string ClubResigned = "world.club_tenure.resigned";

    /// <summary>A manager chose the colours their club plays in.</summary>
    public const string ClubColoursChanged = "world.club.colours_changed";

    /// <summary>A takeover was refused because the country was out of capacity.</summary>
    public const string ClaimRefusedAtCapacity = "world.club_claim.refused_capacity";

    /// <summary>A next tier was requested because a country's lowest tier filled with humans.</summary>
    public const string ProvisioningRequested = "world.division_provisioning.requested";

    /// <summary>A worker began generating a requested tier.</summary>
    public const string ProvisioningStarted = "world.division_provisioning.started";

    /// <summary>A tier was generated, validated, backfilled, and activated (`PYR-8`).</summary>
    public const string ProvisioningCompleted = "world.division_provisioning.completed";

    /// <summary>A provisioning run failed validation or generation and needs an operator (`PYR-14`).</summary>
    public const string ProvisioningFailed = "world.division_provisioning.failed";

    /// <summary>An inactivity warning was sent to a manager (`OCC-1`).</summary>
    public const string InactivityWarningSent = "world.club_tenure.inactivity_warned";

    /// <summary>A tenure was marked inactive and the AI began assisting (`OCC-2`).</summary>
    public const string TenureBecameInactive = "world.club_tenure.became_inactive";

    /// <summary>A tenure was closed because the manager stayed away (`OCC-3`).</summary>
    public const string TenureClosedForInactivity = "world.club_tenure.closed_inactivity";

    /// <summary>An operator handed a club back to full AI control before the standard inactivity period (`OCC-6`).</summary>
    public const string ClubAssignedToAi = "world.club_tenure.assigned_ai";

    /// <summary>A manager's notification preferences changed (Stage 11).</summary>
    public const string NotificationPreferencesChanged = "world.manager_profile.notifications_changed";

    /// <summary>A season was frozen for rollover and passed preflight (`PR-4`).</summary>
    public const string RolloverStarted = "world.season_rollover.started";

    /// <summary>A season rollover failed and awaits an operator (`PR-4`, ADR-0031).</summary>
    public const string RolloverFailed = "world.season_rollover.failed";

    /// <summary>A season rolled over: the closing season is sealed and the next one is active (`PR-4`, `PR-6`).</summary>
    public const string RolloverCompleted = "world.season_rollover.completed";

    /// <summary>The closing season's contracts were resolved at rollover (`CON-6`, `CON-8`).</summary>
    public const string SquadsSettled = "world.season_rollover.squads_settled";

    /// <summary>An operator resumed a failed rollover, with a reason (ADR-0031, ADR-0034).</summary>
    public const string RolloverResumed = "world.season_rollover.resumed";
}
