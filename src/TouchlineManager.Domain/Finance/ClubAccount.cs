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
/// Every move is made by a <see cref="Post"/> and recorded as a <see cref="LedgerEntry"/>, so the balances
/// here are the running total of the ledger and nothing else writes them (`FIN-11`, `FIN-12`). Corrections
/// are compensating entries, never edits.
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

    /// <summary>Opens an empty account. Its balance arrives as its first ledger entry, never as a value here.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The owning club.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// The account opens at zero and is funded by a <see cref="LedgerCategory.OpeningBalance"/> posting, so the
    /// ledger — not the row — is where the club's money comes from. A starting value written here would be a
    /// balance the replay could never reproduce (`FIN-18`).
    /// </remarks>
    public static ClubAccount Open(Guid id, Guid clubId, DateTimeOffset now) =>
        new()
        {
            Id = id,
            ClubId = clubId,
            CashMinor = 0,
            ReservedMinor = 0,
            LastLedgerSequence = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>
    /// Applies a posting to the club's balances and records it as the club's next ledger entry.
    /// </summary>
    /// <param name="posting">The move to make.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The entry the account recorded, for the caller to stage.</returns>
    /// <remarks>
    /// This is the only way a balance changes (`FIN-11`, `FIN-12`). The move is refused when it would leave
    /// cash or reserved funds negative, or would reserve more than the cash behind it — which is `FIN-10` and
    /// `FIN-13` expressed once, where every caller passes through. The resulting balances are written onto the
    /// entry, so the ledger states what the club held and not only what moved.
    /// </remarks>
    public LedgerEntry Post(LedgerPosting posting, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);

        if (posting.CashDeltaMinor == 0 && posting.ReservedDeltaMinor == 0)
        {
            throw new InvalidOperationException("A ledger entry must move something (FIN-11).");
        }

        var cash = CashMinor + posting.CashDeltaMinor;

        if (cash < 0)
        {
            throw new InvalidOperationException(
                "A club's cash is never negative, so this posting is refused (FIN-13).");
        }

        var reserved = ReservedMinor + posting.ReservedDeltaMinor;

        if (reserved < 0)
        {
            throw new InvalidOperationException(
                "A club's reserved funds are never negative, so this posting is refused (FIN-13).");
        }

        if (reserved > cash)
        {
            throw new InvalidOperationException(
                "A club cannot reserve money it does not have, so this posting is refused (FIN-10).");
        }

        var sequence = LastLedgerSequence + 1;

        var entry = LedgerEntry.Record(
            posting.EntryId,
            ClubId,
            sequence,
            posting.Category,
            posting.CashDeltaMinor,
            posting.ReservedDeltaMinor,
            cash,
            reserved,
            posting.SourceType,
            posting.SourceId,
            posting.CorrelationId,
            posting.DescriptionTemplate,
            posting.DescriptionParametersJson,
            now,
            posting.ReversesEntryId);

        CashMinor = cash;
        ReservedMinor = reserved;
        LastLedgerSequence = sequence;
        UpdatedAt = now;
        Version++;

        return entry;
    }
}
