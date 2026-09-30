using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Persistence for accounts' multi-factor credentials and their recovery codes
/// (<c>auth.mfa_credentials</c>, <c>auth.mfa_recovery_codes</c>; ADR-0042).
/// </summary>
/// <remarks>
/// Every lookup loads the account's recovery codes with the credential: a verification needs them, and
/// they are a handful of rows.
/// </remarks>
public interface IMfaCredentialRepository
{
    /// <summary>Finds an account's credential, with its recovery codes, if one exists.</summary>
    Task<MfaCredential?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stages a new credential.</summary>
    void Add(MfaCredential credential);

    /// <summary>Stages a credential for removal, as disabling the factor or an operator reset does.</summary>
    void Remove(MfaCredential credential);
}
