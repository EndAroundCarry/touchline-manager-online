namespace TouchlineManager.Domain.World;

/// <summary>
/// A manager profile: the game-facing identity of an account (`WORLD-7`).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from the account. The auth module owns credentials and knows nothing about
/// clubs; this owns reputation, preferences, and the resignation cooldown, and is the row a
/// <see cref="ClubTenure"/> points at.
/// </para>
/// <para>
/// A manager never owns a club record (`WORLD-7`). Control is expressed entirely by tenures, which is
/// what lets a club outlive the managers who ran it and lets a tenure end without touching club state.
/// </para>
/// </remarks>
public sealed class Manager
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private Manager()
    {
    }

    /// <summary>Gets the manager identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning account. One manager profile per account.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the manager's reputation, on the same 1–100 scale as club reputation.</summary>
    public int Reputation { get; private set; }

    /// <summary>Gets when the resignation cooldown expires, or <see langword="null"/> if none applies.</summary>
    public DateTimeOffset? TakeoverCooldownUntil { get; private set; }

    /// <summary>Gets the manager's preferred locale for formatting, e.g. <c>en-GB</c>.</summary>
    public string Locale { get; private set; } = string.Empty;

    /// <summary>Gets the manager's IANA time zone, used to render deadlines in local time (`CAL-4`).</summary>
    public string TimeZone { get; private set; } = string.Empty;

    /// <summary>Gets when the profile was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the profile was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates a new manager profile with no club and no cooldown.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="userId">The owning account.</param>
    /// <param name="locale">The preferred locale.</param>
    /// <param name="timeZone">The IANA time zone.</param>
    /// <param name="now">The current instant.</param>
    public static Manager Create(
        Guid id,
        Guid userId,
        string locale,
        string timeZone,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        return new Manager
        {
            Id = id,
            UserId = userId,
            Reputation = 0,
            Locale = locale.Trim(),
            TimeZone = timeZone.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets a value indicating whether the cooldown has lapsed, so a claim may be attempted.</summary>
    /// <param name="now">The current instant.</param>
    public bool CanTakeOver(DateTimeOffset now) =>
        TakeoverCooldownUntil is null || TakeoverCooldownUntil <= now;

    /// <summary>
    /// Starts the cooldown that follows a voluntary resignation (`OCC-4`).
    /// </summary>
    /// <param name="cooldown">How long the cooldown lasts.</param>
    /// <param name="now">The current instant.</param>
    public void BeginTakeoverCooldown(TimeSpan cooldown, DateTimeOffset now)
    {
        if (cooldown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cooldown), cooldown, "A cooldown must be positive.");
        }

        TakeoverCooldownUntil = now.Add(cooldown);

        Touch(now);
    }

    /// <summary>Clears any cooldown. Used when a claim succeeds, since taking over ends the wait.</summary>
    /// <param name="now">The current instant.</param>
    public void ClearTakeoverCooldown(DateTimeOffset now)
    {
        if (TakeoverCooldownUntil is null)
        {
            return;
        }

        TakeoverCooldownUntil = null;

        Touch(now);
    }

    /// <summary>Records the outcome of a completed tenure for reputation purposes.</summary>
    /// <param name="reputation">The new reputation value, 1–100.</param>
    /// <param name="now">The current instant.</param>
    public void RecordReputation(int reputation, DateTimeOffset now)
    {
        if (reputation is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reputation),
                reputation,
                "Reputation is on the 1–100 scale.");
        }

        Reputation = reputation;

        Touch(now);
    }

    /// <summary>Changes the manager's formatting preferences.</summary>
    /// <param name="locale">The preferred locale.</param>
    /// <param name="timeZone">The IANA time zone.</param>
    /// <param name="now">The current instant.</param>
    public void ChangePreferences(string locale, string timeZone, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        Locale = locale.Trim();
        TimeZone = timeZone.Trim();

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
