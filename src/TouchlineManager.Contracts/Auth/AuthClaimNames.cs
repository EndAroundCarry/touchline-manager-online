namespace TouchlineManager.Contracts.Auth;

/// <summary>
/// Claim names used in access tokens. Part of the contract between the issuer and the validator.
/// </summary>
/// <remarks>
/// Short names are used deliberately, with inbound claim mapping turned off at the validator, so a
/// token's claim set is what the issuer wrote and nothing is silently renamed to a Microsoft URI.
/// </remarks>
public static class AuthClaimNames
{
    /// <summary>The subject: the account identity.</summary>
    public const string Subject = "sub";

    /// <summary>The security stamp embedded in the token and compared with the stored one.</summary>
    public const string SecurityStamp = "stamp";

    /// <summary>A role held by the account. Repeated once per role.</summary>
    public const string Role = "role";

    /// <summary>
    /// <c>true</c> once the address is verified. Used by the policy that guards write actions which
    /// require a verified account (ADR-0002).
    /// </summary>
    public const string EmailVerified = "email_verified";

    /// <summary>The token identity, unique per issued token.</summary>
    public const string TokenId = "jti";
}
