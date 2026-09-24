namespace TouchlineManager.Contracts.Auth;

/// <summary>The public projection of an account. Never carries a hash, stamp, or hidden field.</summary>
/// <param name="Id">The account identity.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The public display name.</param>
/// <param name="EmailVerified">Whether the address has been verified.</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="Roles">The roles the account holds.</param>
/// <param name="LastLoginAt">When the account last authenticated, if ever.</param>
/// <param name="CreatedAt">When the account was created.</param>
public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// The result of a successful login or refresh. The refresh token itself is never in the body: it
/// travels only as an HttpOnly cookie (ADR-0002).
/// </summary>
/// <param name="AccessToken">The short-lived bearer token, held in memory by the client only.</param>
/// <param name="AccessTokenExpiresAt">When the access token expires.</param>
/// <param name="ServerTime">The server's current instant, so client clock drift is visible.</param>
/// <param name="User">The authenticated account.</param>
public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset ServerTime,
    UserProfileResponse User);

/// <summary>The result of a registration request.</summary>
/// <param name="UserId">The new account identity, used to build the verification link.</param>
/// <param name="Email">The registered email address.</param>
/// <param name="VerificationEmailSent">Whether the verification email was dispatched.</param>
public sealed record RegistrationAcceptedResponse(Guid UserId, string Email, bool VerificationEmailSent);

/// <summary>
/// A generic acknowledgement for operations that must not reveal whether an account exists
/// (login, forgot-password, resend-verification).
/// </summary>
/// <param name="Message">A neutral, non-enumerating message.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record RequestAcceptedResponse(string Message, DateTimeOffset ServerTime);

/// <summary>The result of a profile change, including its new concurrency version.</summary>
/// <param name="User">The updated account.</param>
/// <param name="Version">The new version, to be used as an ETag on the next conditional write.</param>
public sealed record ProfileUpdatedResponse(UserProfileResponse User, long Version);
