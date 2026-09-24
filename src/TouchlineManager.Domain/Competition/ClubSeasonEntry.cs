namespace TouchlineManager.Domain.Competition;

/// <summary>Who controlled a club when its season entry was created (master plan §6.4).</summary>
/// <remarks>
/// Recorded per season rather than derived, because the answer for a finished season must not change
/// when a manager resigns today. A season is a historical record of who was there at the time.
/// </remarks>
public enum ClubControlType
{
    /// <summary>The club was under AI control.</summary>
    Ai = 0,

    /// <summary>The club was under a human manager's control.</summary>
    Human = 1,
}

/// <summary>Storage and transport representation of <see cref="ClubControlType"/>.</summary>
public static class ClubControlTypes
{
    /// <summary>The code for <see cref="ClubControlType.Ai"/>.</summary>
    public const string AiCode = "ai";

    /// <summary>The code for <see cref="ClubControlType.Human"/>.</summary>
    public const string HumanCode = "human";

    /// <summary>Converts a control type to its stable code.</summary>
    public static string ToCode(this ClubControlType controlType) => controlType switch
    {
        ClubControlType.Ai => AiCode,
        ClubControlType.Human => HumanCode,
        _ => throw new ArgumentOutOfRangeException(nameof(controlType), controlType, "Unknown control type."),
    };

    /// <summary>Parses a stable code back to its control type.</summary>
    public static ClubControlType FromCode(string code) => code switch
    {
        AiCode => ClubControlType.Ai,
        HumanCode => ClubControlType.Human,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown control type code."),
    };
}

/// <summary>
/// A club's membership of one division-season: the immutable record of who played where.
/// </summary>
/// <remarks>
/// <para>
/// This row is what promotion and relegation read and write. It carries <see cref="SeasonId"/> as well
/// as the division-season so that "one club appears in exactly one division per season" is a database
/// constraint rather than a promise — a database cannot express that through the division-season alone,
/// because it has no way to know two division-seasons share a season.
/// </para>
/// <para>
/// A finished entry is never rewritten (`PR-6`). The final rank and the movement flags are written once,
/// at rollover, and kept afterwards as the season's history.
/// </para>
/// </remarks>
public sealed class ClubSeasonEntry
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubSeasonEntry()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the division-season the club played in.</summary>
    public Guid DivisionSeasonId { get; private set; }

    /// <summary>Gets the season, denormalized so one-club-per-season is enforceable in the database.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the club.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets who controlled the club when the entry was created.</summary>
    public ClubControlType InitialControlType { get; private set; }

    /// <summary>Gets the final league position, written at rollover.</summary>
    public int? FinalRank { get; private set; }

    /// <summary>Gets a value indicating whether the club was promoted at the end of this season.</summary>
    public bool IsPromoted { get; private set; }

    /// <summary>Gets a value indicating whether the club was relegated at the end of this season.</summary>
    public bool IsRelegated { get; private set; }

    /// <summary>Gets the club's reputation when the season closed, for history and awards.</summary>
    public int? ClosingReputation { get; private set; }

    /// <summary>Gets the club's cash when the season closed, in minor units (`FIN-1`).</summary>
    public long? ClosingCashMinor { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Enters a club into a division-season.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="initialControlType">Who controls the club at entry.</param>
    /// <param name="now">The current instant.</param>
    public static ClubSeasonEntry Enter(
        Guid id,
        Guid divisionSeasonId,
        Guid seasonId,
        Guid clubId,
        ClubControlType initialControlType,
        DateTimeOffset now)
    {
        return new ClubSeasonEntry
        {
            Id = id,
            DivisionSeasonId = divisionSeasonId,
            SeasonId = seasonId,
            ClubId = clubId,
            InitialControlType = initialControlType,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Records the club's final position and movement. Called once, at rollover (`PR-4`).
    /// </summary>
    /// <param name="finalRank">The final league position, 1-based.</param>
    /// <param name="promoted">Whether the club went up.</param>
    /// <param name="relegated">Whether the club went down.</param>
    /// <param name="reputation">The club's reputation at close.</param>
    /// <param name="cashMinor">The club's cash at close, in minor units.</param>
    /// <param name="now">The current instant.</param>
    public void Close(
        int finalRank,
        bool promoted,
        bool relegated,
        int reputation,
        long cashMinor,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(finalRank, 1);

        if (promoted && relegated)
        {
            throw new InvalidOperationException("A club cannot be promoted and relegated in the same season.");
        }

        FinalRank = finalRank;
        IsPromoted = promoted;
        IsRelegated = relegated;
        ClosingReputation = reputation;
        ClosingCashMinor = cashMinor;

        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
