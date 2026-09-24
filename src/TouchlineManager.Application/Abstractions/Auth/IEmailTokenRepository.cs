using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Application.Abstractions.Auth;

/// <summary>
/// Persistence for single-use email tokens (<c>auth.email_tokens</c>).
/// </summary>
/// <remarks>
/// Only the newest active token per account and purpose is valid, so a leaked older link stops
/// working as soon as the manager requests a new one (ADR-0002).
/// </remarks>
public interface IEmailTokenRepository
{
    /// <summary>Finds a token by its purpose and hash.</summary>
    Task<EmailToken?> FindByTokenHashAsync(
        EmailTokenPurpose purpose,
        string tokenHash,
        CancellationToken cancellationToken);

    /// <summary>Invalidates every usable token for an account and purpose.</summary>
    Task SupersedeActiveAsync(
        Guid userId,
        EmailTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Stages a new token.</summary>
    void Add(EmailToken token);
}
