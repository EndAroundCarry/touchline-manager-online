namespace TouchlineManager.Domain.Finance;

/// <summary>
/// One category's total within a club's season finance summary (master plan §6.8).
/// </summary>
/// <remarks>
/// A child row rather than a column per category, so the summary stays correct as the ledger's categories
/// grow and so a season's income and expenses are queryable by category without a schema change. It is the
/// physical form of the "category totals" the plan names.
/// </remarks>
public sealed class ClubSeasonFinanceLine
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubSeasonFinanceLine()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the summary this line belongs to.</summary>
    public Guid ClubSeasonFinanceId { get; private set; }

    /// <summary>Gets the ledger category the total is for.</summary>
    public LedgerCategory Category { get; private set; }

    /// <summary>Gets the signed total the category moved over the season, in minor units.</summary>
    public long CashDeltaMinor { get; private set; }

    /// <summary>Records one category's season total.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubSeasonFinanceId">The parent summary.</param>
    /// <param name="category">The ledger category.</param>
    /// <param name="cashDeltaMinor">The signed total, in minor units.</param>
    /// <param name="now">The current instant.</param>
    public static ClubSeasonFinanceLine Record(
        Guid id,
        Guid clubSeasonFinanceId,
        LedgerCategory category,
        long cashDeltaMinor,
        DateTimeOffset now)
    {
        return new ClubSeasonFinanceLine
        {
            Id = id,
            ClubSeasonFinanceId = clubSeasonFinanceId,
            Category = category,
            CashDeltaMinor = cashDeltaMinor,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Gets when the line was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the line was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }
}
