using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Abstractions.Finance;

/// <summary>One club's cash movement in a category over a window (master plan §6.8).</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Category">The ledger category.</param>
/// <param name="CashDeltaMinor">The signed total, in minor units.</param>
public sealed record ClubCategoryTotal(Guid ClubId, LedgerCategory Category, long CashDeltaMinor);

/// <summary>
/// Persistence for the club ledger, the append-only record every balance is the running total of
/// (master plan §6.8, `FIN-11`).
/// </summary>
/// <remarks>
/// <para>
/// An entry is staged, never edited, so the port offers no update or delete: a correction is a new
/// compensating entry the caller posts through <see cref="ClubAccount"/> (`FIN-12`). It is staged rather than
/// saved so a workflow that moves money and writes other state — a publication, a wage run, a transfer
/// resolution — commits the two together.
/// </para>
/// <para>
/// The entries are written by the same use case that moves the account balance, which is why this sits beside
/// <see cref="IClubAccountRepository"/>: a balance change that skipped the ledger would be the hole the
/// finance module exists to prevent.
/// </para>
/// </remarks>
public interface ILedgerRepository
{
    /// <summary>Stages one ledger entry.</summary>
    /// <param name="entry">The entry.</param>
    void Add(LedgerEntry entry);

    /// <summary>
    /// Reports which of the given correlation keys already have an entry, so a retried operation can skip the
    /// work it already did instead of colliding with its own rows (`FIN-17`).
    /// </summary>
    /// <remarks>
    /// The unique index is still the guarantee against a concurrent double-write; this read is what makes a
    /// sequential retry a clean no-op rather than a failed insert.
    /// </remarks>
    /// <param name="correlationIds">The operation keys to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subset of the keys that already appear in the ledger.</returns>
    Task<IReadOnlySet<string>> FindExistingCorrelationIdsAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sums cash movement by category over a window, for a season summary (master plan §6.8).
    /// </summary>
    /// <param name="clubIds">The clubs to summarize.</param>
    /// <param name="fromInclusive">The window's start, inclusive.</param>
    /// <param name="toExclusive">The window's end, exclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One total per club and category present in the window.</returns>
    Task<IReadOnlyList<ClubCategoryTotal>> LoadCategoryTotalsAsync(
        IReadOnlyCollection<Guid> clubIds,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads each club's cash balance just before an instant, the opening cash of the season that follows it.
    /// </summary>
    /// <param name="clubIds">The clubs.</param>
    /// <param name="instant">The instant the balance is measured before.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each club's cash in minor units, zero for a club with no earlier entries.</returns>
    Task<IReadOnlyDictionary<Guid, long>> LoadCashBalanceBeforeAsync(
        IReadOnlyCollection<Guid> clubIds,
        DateTimeOffset instant,
        CancellationToken cancellationToken);
}
