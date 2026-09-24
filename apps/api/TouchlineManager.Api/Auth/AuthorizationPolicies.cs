namespace TouchlineManager.Api.Auth;

/// <summary>
/// Names of the authorization policies the product enforces.
/// </summary>
/// <remarks>
/// <see cref="VerifiedManager"/> is the policy that implements "email verification is required before
/// a club claim, bid, listing, or display-name change" (ADR-0002). Stage 3 onward attach it to those
/// commands rather than re-checking verification per handler.
/// </remarks>
internal static class AuthorizationPolicies
{
    /// <summary>An authenticated account whose email address is verified.</summary>
    public const string VerifiedManager = "VerifiedManager";

    /// <summary>An account holding the <c>admin</c> role.</summary>
    public const string Admin = "Admin";

    /// <summary>An account holding the <c>operator</c> role.</summary>
    public const string Operator = "Operator";

    /// <summary>An account holding the <c>support</c> role.</summary>
    public const string Support = "Support";
}

/// <summary>Names of the rate-limiting policies.</summary>
internal static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the endpoints an attacker would hammer: registration, login, password reset, and
    /// verification mail (ADR-0002, master plan §12.1).
    /// </summary>
    public const string AuthSensitive = "auth-sensitive";
}
