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

    /// <summary>A manager profile was created.</summary>
    public const string ManagerProfileCreated = "world.manager_profile.created";

    /// <summary>Manager preferences were changed.</summary>
    public const string ManagerPreferencesChanged = "world.manager_profile.preferences_changed";

    /// <summary>A club was taken over.</summary>
    public const string ClubClaimed = "world.club_tenure.claimed";

    /// <summary>A manager resigned from a club.</summary>
    public const string ClubResigned = "world.club_tenure.resigned";

    /// <summary>A takeover was refused because the country was out of capacity.</summary>
    public const string ClaimRefusedAtCapacity = "world.club_claim.refused_capacity";

    /// <summary>A next tier was requested because a country's lowest tier filled with humans.</summary>
    public const string ProvisioningRequested = "world.division_provisioning.requested";
}
