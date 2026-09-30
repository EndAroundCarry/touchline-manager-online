using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Verifies a code against a credential's secret or one-time recovery codes (ADR-0042).
/// </summary>
/// <remarks>
/// Shared by the login challenge, disabling the factor, regenerating recovery codes, and the step-up check
/// on an admin mutation, so every one of them accepts exactly the same proofs.
/// </remarks>
public sealed class MfaAuthenticator
{
    private readonly IMfaSecretProtector _protector;
    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the authenticator.</summary>
    public MfaAuthenticator(IMfaSecretProtector protector, ISecureTokenService secureTokens)
    {
        _protector = protector;
        _secureTokens = secureTokens;
    }

    /// <summary>Verifies a time-based code against the credential's secret.</summary>
    /// <param name="credential">The account's credential.</param>
    /// <param name="code">The code presented.</param>
    /// <param name="now">The current instant.</param>
    public bool VerifyCode(MfaCredential credential, string code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credential);

        try
        {
            var secret = _protector.Unprotect(credential.ProtectedSecret);

            return Totp.Verify(secret, code, now);
        }
        catch (InvalidOperationException)
        {
            // A secret that cannot be recovered (tampered, or an unknown format) simply does not verify.
            return false;
        }
    }

    /// <summary>Consumes an unused recovery code, if the presented value matches one.</summary>
    /// <param name="credential">The account's credential.</param>
    /// <param name="code">The code presented.</param>
    /// <param name="now">The current instant.</param>
    public bool TryConsumeRecoveryCode(MfaCredential credential, string code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var hash = _secureTokens.HashToken(code.Trim());

        var match = credential.RecoveryCodes.FirstOrDefault(candidate =>
            !candidate.IsUsed && string.Equals(candidate.CodeHash, hash, StringComparison.Ordinal));

        if (match is null)
        {
            return false;
        }

        match.Consume(now);

        return true;
    }
}
