namespace TouchlineManager.Domain.Auth;

/// <summary>
/// A single-use, purpose-scoped email token (<c>auth.email_tokens</c>).
/// </summary>
/// <remarks>
/// Only the hash is stored (ADR-0002). Issuing a new token for a user and purpose supersedes the
/// older ones, so an old email cannot be replayed after the manager requests a fresh link.
/// </remarks>
public sealed class EmailToken
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private EmailToken()
    {
    }

    /// <summary>Gets the token identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the account the token belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets what the token authorizes.</summary>
    public EmailTokenPurpose Purpose { get; private set; }

    /// <summary>Gets the hash of the token. The raw value is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Gets when the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the token was consumed, if it has been.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Whether the token may still be used.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    /// <summary>Issues a single-use token.</summary>
    public static EmailToken Issue(
        Guid id,
        Guid userId,
        EmailTokenPurpose purpose,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "A token lifetime must be positive.");
        }

        return new EmailToken
        {
            Id = id,
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
    }

    /// <summary>Consumes the token. Throws when it is expired or already used.</summary>
    /// <param name="now">The current instant.</param>
    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("The token has expired or has already been used.");
        }

        ConsumedAt = now;
    }

    /// <summary>
    /// Invalidates the token because a newer one was issued for the same account and purpose.
    /// </summary>
    /// <remarks>
    /// Superseding is deliberately not an error when the token is already spent: requesting a fresh
    /// link twice in a row must be harmless.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    public void Supersede(DateTimeOffset now) => ConsumedAt ??= now;
}
