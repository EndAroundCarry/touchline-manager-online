namespace TouchlineManager.Contracts.Auth;

/// <summary>
/// What a manager needs to finish enrolling an authenticator. The secret and the recovery codes are shown
/// exactly once (ADR-0042).
/// </summary>
/// <param name="Secret">The base32 shared secret, for manual entry.</param>
/// <param name="OtpAuthUri">The provisioning URI an authenticator scans.</param>
/// <param name="RecoveryCodes">The one-time recovery codes.</param>
public sealed record MfaEnrolmentResponse(
    string Secret,
    string OtpAuthUri,
    IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// The result of a password step that requires a second factor: the password was accepted, but no session
/// is issued until the challenge is completed (ADR-0042).
/// </summary>
/// <param name="ChallengeToken">The signed challenge to present back with the code.</param>
/// <param name="ExpiresAt">When the challenge stops being accepted.</param>
/// <param name="Message">A human-readable explanation of the next step.</param>
public sealed record MfaChallengeResponse(
    string ChallengeToken,
    DateTimeOffset ExpiresAt,
    string Message);

/// <summary>A freshly issued set of recovery codes, shown exactly once.</summary>
/// <param name="RecoveryCodes">The one-time recovery codes.</param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
