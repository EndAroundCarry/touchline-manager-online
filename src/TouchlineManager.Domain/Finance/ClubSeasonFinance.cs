namespace TouchlineManager.Domain.Finance;

/// <summary>
/// One club's finance summary for one season: opening and closing cash plus a line per category
/// (master plan §6.8).
/// </summary>
/// <remarks>
/// <para>
/// A reporting projection written once, at rollover, so a finished season's money can be read back without
/// replaying the ledger and without the ledger changing underneath it. It is deliberately immutable after
/// it is written: a correction belongs in the ledger, not in a summary of it (`FIN-12`).
/// </para>
/// <para>
/// <c>closing_cash_minor</c> is opening plus the sum of the lines, and the lines are the season's ledger
/// movement grouped by <see cref="LedgerCategory"/>. The window is the season's own — from its
/// <c>starts_at</c> to the end of its rollover — so a settlement award posted at rollover is the first
/// money of the next season rather than part of this summary.
/// </para>
/// </remarks>
public sealed class ClubSeasonFinance
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubSeasonFinance()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club the summary belongs to.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the season summarized.</summary>
    public Guid SeasonId { get; private set; }

    /// <summary>Gets the club's cash when the season opened, in minor units (`FIN-1`).</summary>
    public long OpeningCashMinor { get; private set; }

    /// <summary>Gets the club's cash when the season's window closed, in minor units.</summary>
    public long ClosingCashMinor { get; private set; }

    /// <summary>Gets when the summary was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the summary was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Records a club's season finance summary.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club.</param>
    /// <param name="seasonId">The season.</param>
    /// <param name="openingCashMinor">The opening cash, in minor units.</param>
    /// <param name="closingCashMinor">The closing cash, in minor units.</param>
    /// <param name="now">The current instant.</param>
    public static ClubSeasonFinance Record(
        Guid id,
        Guid clubId,
        Guid seasonId,
        long openingCashMinor,
        long closingCashMinor,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openingCashMinor);
        ArgumentOutOfRangeException.ThrowIfNegative(closingCashMinor);

        return new ClubSeasonFinance
        {
            Id = id,
            ClubId = clubId,
            SeasonId = seasonId,
            OpeningCashMinor = openingCashMinor,
            ClosingCashMinor = closingCashMinor,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }
}
