namespace TouchlineManager.Domain.Auth;

/// <summary>
/// The roles an account can hold (master plan §6.2, ADR-0002).
/// </summary>
/// <remarks>
/// Roles live in the domain because they decide authorization, which is a correctness concern, not
/// a presentation one. The <see cref="MfaRequired"/> set is the groundwork for the Stage 14
/// requirement that support, operator, and admin accounts authenticate with a second factor.
/// </remarks>
public static class UserRoles
{
    /// <summary>An ordinary manager.</summary>
    public const string Player = "player";

    /// <summary>Customer support. Requires MFA before production launch.</summary>
    public const string Support = "support";

    /// <summary>Game operations. Requires MFA before production launch.</summary>
    public const string Operator = "operator";

    /// <summary>Full administration. Requires MFA before production launch.</summary>
    public const string Admin = "admin";

    /// <summary>Every role the product recognises.</summary>
    public static IReadOnlyList<string> All { get; } = [Player, Support, Operator, Admin];

    /// <summary>Roles for which multi-factor authentication is mandatory in production.</summary>
    public static IReadOnlyList<string> MfaRequired { get; } = [Support, Operator, Admin];

    /// <summary>Reports whether a role name is one this product knows.</summary>
    public static bool IsKnown(string role) => All.Contains(role, StringComparer.Ordinal);
}
