using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Auth;

/// <summary>
/// Issues a fresh set of one-time recovery codes (ADR-0042).
/// </summary>
/// <remarks>
/// A recovery code is a short base32 value a manager writes down, so it is generated from a cryptographic
/// source, shown once, and stored only as a hash.
/// </remarks>
public sealed class MfaRecoveryCodeIssuer
{
    /// <summary>How many codes are issued at a time.</summary>
    public const int CodeCount = 10;

    /// <summary>The length of each code, in base32 characters.</summary>
    public const int CodeLength = 10;

    private readonly ISecureTokenService _secureTokens;

    /// <summary>Initializes the issuer.</summary>
    public MfaRecoveryCodeIssuer(ISecureTokenService secureTokens) => _secureTokens = secureTokens;

    /// <summary>Generates a fresh set of plaintext codes. The caller stores only their hashes.</summary>
    public IReadOnlyList<string> Issue()
    {
        var codes = new List<string>(CodeCount);

        for (var index = 0; index < CodeCount; index++)
        {
            var bytes = _secureTokens.CreateRandomBytes(CodeLength);
            codes.Add(Base32.Encode(bytes)[..CodeLength]);
        }

        return codes;
    }
}
