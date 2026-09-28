using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Abstractions.Finance;

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
}
