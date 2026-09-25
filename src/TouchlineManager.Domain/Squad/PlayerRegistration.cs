namespace TouchlineManager.Domain.Squad;

/// <summary>The lifecycle state of a player registration (`SQ-6`, `SQ-7`).</summary>
public enum RegistrationStatus
{
    /// <summary>The player may be selected for the club. A player has at most one of these (`SQ-6`).</summary>
    Active = 0,

    /// <summary>The registration has ended.</summary>
    Ended = 1,
}

/// <summary>Stable codes and storage representation for <see cref="RegistrationStatus"/>.</summary>
public static class RegistrationStatuses
{
    /// <summary>The code for <see cref="RegistrationStatus.Active"/>.</summary>
    public const string ActiveCode = "active";

    /// <summary>The code for <see cref="RegistrationStatus.Ended"/>.</summary>
    public const string EndedCode = "ended";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 6;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The registration status.</param>
    public static string ToCode(this RegistrationStatus status) => status switch
    {
        RegistrationStatus.Active => ActiveCode,
        RegistrationStatus.Ended => EndedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown registration status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static RegistrationStatus FromCode(string code) => code switch
    {
        ActiveCode => RegistrationStatus.Active,
        EndedCode => RegistrationStatus.Ended,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown registration status code."),
    };
}

/// <summary>
/// A player's eligibility to represent a club from a specific fixture boundary (`SQ-6`, `SQ-7`).
/// </summary>
/// <remarks>
/// <para>
/// Registration is separate from the contract because eligibility is bounded by a <em>fixture</em>, not
/// by time: a player signed after a fixture's snapshot locks may play only in later fixtures (`SQ-7`).
/// The contract says who owes whom money; the registration says who may be selected, and from when.
/// </para>
/// <para>
/// `SQ-6` requires the active registration and the active contract to agree on the club. A partial
/// unique index can only say "one active row per player", so the agreement itself is enforced in the
/// application transaction and by an invariant test — the same arrangement
/// `data-model.md` §3.2 records.
/// </para>
/// </remarks>
public sealed class PlayerRegistration
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private PlayerRegistration()
    {
    }

    /// <summary>Gets the registration identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the registered player.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Gets the club the player is registered to.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season the registration takes effect in.</summary>
    public Guid EffectiveSeasonId { get; private set; }

    /// <summary>
    /// Gets the matchday round the registration takes effect from. Zero means it was effective before the
    /// season's first fixture, which is the seeded and transferred-between-seasons case.
    /// </summary>
    public int EffectiveFixtureBoundaryRound { get; private set; }

    /// <summary>Gets the lifecycle state.</summary>
    public RegistrationStatus Status { get; private set; }

    /// <summary>Gets when the registration was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the registration was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets whether the player may currently be selected. A player has at most one of these (`SQ-6`).</summary>
    public bool IsActive => Status == RegistrationStatus.Active;

    /// <summary>Registers a player to a club from a fixture boundary (`SQ-7`).</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="playerId">The registered player.</param>
    /// <param name="clubId">The club the player is registered to.</param>
    /// <param name="effectiveSeasonId">The season the registration takes effect in.</param>
    /// <param name="effectiveFixtureBoundaryRound">
    /// The round the registration takes effect from, or zero for effective from before the first fixture.
    /// </param>
    /// <param name="now">The current instant.</param>
    public static PlayerRegistration Register(
        Guid id,
        Guid playerId,
        Guid clubId,
        Guid effectiveSeasonId,
        int effectiveFixtureBoundaryRound,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(effectiveFixtureBoundaryRound);

        return new PlayerRegistration
        {
            Id = id,
            PlayerId = playerId,
            ClubId = clubId,
            EffectiveSeasonId = effectiveSeasonId,
            EffectiveFixtureBoundaryRound = effectiveFixtureBoundaryRound,
            Status = RegistrationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Ends the registration, for example when a transfer moves the player (`CON-5`).</summary>
    /// <param name="now">The current instant.</param>
    public void End(DateTimeOffset now)
    {
        if (Status == RegistrationStatus.Ended)
        {
            throw new InvalidOperationException("A registration cannot be ended twice.");
        }

        Status = RegistrationStatus.Ended;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
