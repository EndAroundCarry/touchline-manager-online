namespace TouchlineManager.Contracts.World;

/// <summary>
/// Stable machine-readable error codes for the world and onboarding module (master plan §10).
/// </summary>
/// <remarks>
/// Clients branch on these codes, never on the human-readable text. The onboarding codes are the ones
/// master plan §7.6 requires to be distinguishable rather than collapsed into a generic failure: a
/// manager who cannot claim a club needs to know whether the club was taken, whether their own account
/// already holds one, or whether the country is simply still provisioning (`PYR-10`).
/// </remarks>
public static class WorldErrorCodes
{
    /// <summary>No world has been seeded yet, so there is nothing to onboard into.</summary>
    public const string WorldNotSeeded = "WORLD_NOT_SEEDED";

    /// <summary>The requested country does not exist in this world.</summary>
    public const string CountryNotFound = "COUNTRY_NOT_FOUND";

    /// <summary>The requested club does not exist in this world.</summary>
    public const string ClubNotFound = "CLUB_NOT_FOUND";

    /// <summary>
    /// The club cannot be taken over: it is not in the country's lowest active tier, or it is not
    /// active (`WORLD-8`).
    /// </summary>
    public const string ClubNotClaimable = "CLUB_NOT_CLAIMABLE";

    /// <summary>Another manager already holds the club (master plan §7.6).</summary>
    public const string ClubAlreadyClaimed = "CLUB_ALREADY_CLAIMED";

    /// <summary>The account has no manager profile yet.</summary>
    public const string ManagerProfileRequired = "MANAGER_PROFILE_REQUIRED";

    /// <summary>The account already has a manager profile. One profile per account.</summary>
    public const string ManagerProfileExists = "MANAGER_PROFILE_EXISTS";

    /// <summary>The manager already controls a club, so cannot claim another (`OCC-9`).</summary>
    public const string ManagerHasActiveClub = "MANAGER_HAS_ACTIVE_CLUB";

    /// <summary>The manager is still serving the cooldown that follows a resignation (`OCC-4`).</summary>
    public const string ManagerInCooldown = "MANAGER_IN_COOLDOWN";

    /// <summary>The manager holds no club, so there is nothing to resign from.</summary>
    public const string NoActiveTenure = "NO_ACTIVE_TENURE";

    /// <summary>
    /// Every club in the country's lowest active tier is held by a human and the next tier is still
    /// being generated (`PYR-10`). The response carries the request status and a polling hint.
    /// </summary>
    public const string CapacityProvisioning = "CAPACITY_PROVISIONING";

    /// <summary>A command that must not execute twice was sent without an <c>Idempotency-Key</c>.</summary>
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";

    /// <summary>The idempotency key was already used for a different command.</summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";

    /// <summary>The world is frozen, so onboarding is closed while play continues.</summary>
    public const string WorldNotAcceptingClaims = "WORLD_NOT_ACCEPTING_CLAIMS";
}
