namespace TouchlineManager.Domain.Finance;

/// <summary>
/// A club's money: spendable cash and the part of it currently committed to open bids.
/// </summary>
/// <remarks>
/// <para>
/// Money is an integer count of minor units of one canonical in-game currency (`FIN-1`, `FIN-2`).
/// There is no floating-point money anywhere in this project, which is why these are <see cref="long"/>
/// and not <c>decimal</c>.
/// </para>
/// <para>
/// The two balances answer different questions. <see cref="CashMinor"/> is what the club has;
/// <see cref="ReservedMinor"/> is what it has already promised to open bids. Affordability is therefore
/// a question about the difference, never about the raw balance (`FIN-10`) — otherwise a manager could
/// commit the same money to two auctions.
/// </para>
/// <para>
/// Moves are made by ledger postings in Stage 9; this type keeps the invariants that make those postings
/// safe. Corrections are compensating entries, never edits (`FIN-12`).
/// </para>
/// </remarks>
public sealed class ClubAccount
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private ClubAccount()
    {
    }

    /// <summary>Gets the account identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club that owns the money.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the club's cash, in minor units.</summary>
    public long CashMinor { get; private set; }

    /// <summary>Gets the part of the cash already committed to open bids, in minor units.</summary>
    public long ReservedMinor { get; private set; }

    /// <summary>Gets the sequence number of the last ledger entry posted to this account.</summary>
    public long LastLedgerSequence { get; private set; }

    /// <summary>Gets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the optimistic concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets what the club can still commit, after existing reservations (`FIN-10`).</summary>
    public long AvailableMinor => CashMinor - ReservedMinor;

    /// <summary>Opens an account with a starting balance and no reservations.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="openingCashMinor">The opening cash, in minor units. Never negative.</param>
    /// <param name="now">The current instant.</param>
    public static ClubAccount Open(Guid id, Guid clubId, long openingCashMinor, DateTimeOffset now)
    {
        if (openingCashMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(openingCashMinor),
                openingCashMinor,
                "Cash is never negative (FIN-13).");
        }

        return new ClubAccount
        {
            Id = id,
            ClubId = clubId,
            CashMinor = openingCashMinor,
            ReservedMinor = 0,
            LastLedgerSequence = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }
}
