using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Finance;

/// <summary>
/// A club's money: spendable cash, the part of it currently committed to open bids, and how far into the red it may go.
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
/// <para>
/// Cash may be below zero, but only through the recurring costs (<see cref="LedgerCategories.MayOverdraw"/>), and never below the account's
/// <see cref="OverdraftFloorMinor"/> (`DEBT-1`). A fee, a building and a release still need cash, and reserved funds never exceed the cash
/// above zero. The account also remembers which state its money is in and since when, so the settlement that moves it is the only thing
/// that has to be run to know (`DEBT-2`).
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

    /// <summary>Gets where the club's money stands: healthy, in the red or in administration (`DEBT-2`).</summary>
    public FinancialStateKind FinancialState { get; private set; }

    /// <summary>Gets the matchday ordinal the account has been in <see cref="FinancialState"/> since, as the settlement counts them.</summary>
    public int StateSinceMatchday { get; private set; }

    /// <summary>Gets the overdraft limit for the season, in minor units: a quarter of the season's income forecast. Zero until a sponsor deal is signed.</summary>
    public long OverdraftLimitMinor { get; private set; }

    /// <summary>
    /// Gets how far below zero the cash may ever go, in minor units: twice the overdraft limit, which is where the emergency grant steps in as the
    /// integrity net (`FIN-16`). The database refuses a balance below it.
    /// </summary>
    public long OverdraftFloorMinor { get; private set; }

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

    /// <summary>Gets whether the club's cash is below zero.</summary>
    public bool IsInTheRed => CashMinor < 0;

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
            FinancialState = FinancialStateKind.Healthy,
            StateSinceMatchday = 0,
            OverdraftLimitMinor = 0,
            OverdraftFloorMinor = 0,
            LastLedgerSequence = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>Sets how far into the red the club may go this season (`DEBT-1`).</summary>
    /// <param name="overdraftLimitMinor">The overdraft limit, a quarter of the season's income forecast.</param>
    /// <param name="now">The current instant.</param>
    /// <remarks>
    /// The floor follows the limit (<see cref="EconomyRuleSet.SafetyNetFloorMinor"/>). A limit cannot be lowered so far that the balance the
    /// account already holds is below the new floor.
    /// </remarks>
    public void SetOverdraft(long overdraftLimitMinor, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overdraftLimitMinor);

        var floor = -EconomyRuleSet.SafetyNetFloorMinor(overdraftLimitMinor);

        if (CashMinor < -floor)
        {
            throw new InvalidOperationException(
                "The overdraft floor cannot be set above the cash the club already owes (DEBT-1).");
        }

        OverdraftLimitMinor = overdraftLimitMinor;
        OverdraftFloorMinor = floor;
        Touch(now);
    }

    /// <summary>Records where the club's money stands after a settlement reads it (`DEBT-2`).</summary>
    /// <param name="step">What <see cref="ClubFinancialState.Next"/> read.</param>
    /// <param name="now">The current instant.</param>
    public void RecordState(FinancialStep step, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentOutOfRangeException.ThrowIfNegative(step.SinceMatchday);

        FinancialState = step.State;
        StateSinceMatchday = step.SinceMatchday;
        Touch(now);
    }

    /// <summary>
    /// Puts the account back to healthy after a clean start has set its cash to the opening cash, so the debt, the state it was in and the
    /// administration clock go with it (`TKO-3`).
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <exception cref="InvalidOperationException">When the club still owes money or still holds reserved funds, so the reset would not be clean.</exception>
    public void RestoreHealth(DateTimeOffset now)
    {
        if (CashMinor < 0 || ReservedMinor != 0)
        {
            throw new InvalidOperationException("An account is only restored to health once its debt is cleared and its bids are released (TKO-3).");
        }

        FinancialState = FinancialStateKind.Healthy;
        StateSinceMatchday = 0;
        Touch(now);
    }

    /// <summary>
    /// Applies a posting to the club's balances and records it as the club's next ledger entry.
    /// </summary>
    /// <param name="posting">The move to make.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The entry the account recorded, for the caller to stage.</returns>
    /// <remarks>
    /// This is the only way a balance changes (`FIN-11`, `FIN-12`). The move is refused when it would take
    /// cash below zero (unless it is a recurring cost and the balance stays above the overdraft floor), leave
    /// reserved funds negative, or reserve more than the cash above zero behind them — which is `FIN-10`,
    /// `FIN-13` and `DEBT-1` expressed once, where every caller passes through. A posting that adds to cash
    /// is always allowed while the club is in the red, since it is a way out. The resulting balances are
    /// written onto the entry, so the ledger states what the club held and not only what moved.
    /// </remarks>
    public LedgerEntry Post(LedgerPosting posting, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);

        if (posting.CashDeltaMinor == 0 && posting.ReservedDeltaMinor == 0)
        {
            throw new InvalidOperationException("A ledger entry must move something (FIN-11).");
        }

        var cash = CashMinor + posting.CashDeltaMinor;

        if (cash < 0 && posting.CashDeltaMinor < 0)
        {
            if (!posting.Category.MayOverdraw())
            {
                throw new InvalidOperationException(
                    "Only a recurring cost may take a club's cash below zero, so this posting is refused (FIN-13, DEBT-1).");
            }

            if (cash < -OverdraftFloorMinor)
            {
                throw new InvalidOperationException(
                    "A club's cash may not go below its overdraft floor, so this posting is refused (FIN-13, DEBT-1).");
            }
        }

        var reserved = ReservedMinor + posting.ReservedDeltaMinor;

        if (reserved < 0)
        {
            throw new InvalidOperationException(
                "A club's reserved funds are never negative, so this posting is refused (FIN-13).");
        }

        if (reserved > Math.Max(0, cash))
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
        Touch(now);

        return entry;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
