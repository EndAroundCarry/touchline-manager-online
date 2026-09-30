namespace TouchlineManager.Domain.Finance;

/// <summary>
/// One immutable line of a club's ledger: a signed move in cash and in reserved funds (master plan §6.8,
/// `FIN-11`).
/// </summary>
/// <remarks>
/// <para>
/// The ledger is append-only and is the truth the club account is a projection of (`FIN-12`, `FIN-18`):
/// <c>club_accounts.cash_minor</c> and <c>reserved_minor</c> are the running totals of these rows, which is
/// why an entry records both the deltas it applied and the balances they produced. The resulting balances
/// are stored rather than derived so a replay can be checked against what the account actually held at the
/// time, not only against today's sum.
/// </para>
/// <para>
/// There is nothing to mutate: an entry is history. A correction is a new <see cref="LedgerCategory.Compensation"/>
/// entry that reverses its effect, never an edit (`FIN-12`).
/// </para>
/// <para>
/// The description is a stable template key and its parameters rather than finished prose, the same contract
/// the match commentary and the inbox use (`MAT-8`, master plan §8.6), so the same line can be rendered in
/// another language later without being rewritten.
/// </para>
/// </remarks>
public sealed class LedgerEntry
{
    /// <summary>The longest correlation key an entry may carry, matching its column.</summary>
    public const int MaxCorrelationIdLength = 80;

    /// <summary>The longest description template key an entry may carry.</summary>
    public const int MaxDescriptionTemplateLength = 64;

    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private LedgerEntry()
    {
    }

    /// <summary>Gets the entry identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the club whose money moved.</summary>
    public Guid ClubId { get; private set; }

    /// <summary>Gets the entry's position in the club's ledger, starting at one (`FIN-11`).</summary>
    public long Sequence { get; private set; }

    /// <summary>Gets what the entry is, which is its durable reason.</summary>
    public LedgerCategory Category { get; private set; }

    /// <summary>Gets the signed change to the club's cash, in minor units.</summary>
    public long CashDeltaMinor { get; private set; }

    /// <summary>Gets the signed change to the club's reserved funds, in minor units.</summary>
    public long ReservedDeltaMinor { get; private set; }

    /// <summary>Gets the cash the club held after this entry was applied, in minor units.</summary>
    public long ResultingCashMinor { get; private set; }

    /// <summary>Gets the reserved funds the club held after this entry was applied, in minor units.</summary>
    public long ResultingReservedMinor { get; private set; }

    /// <summary>Gets the kind of workflow that produced the entry.</summary>
    public LedgerSourceType SourceType { get; private set; }

    /// <summary>Gets the identity of the thing that produced it — a fixture, a job, a listing — or null.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>Gets the correlation key the whole operation shares, which is what makes it idempotent (`FIN-17`).</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the stable template key that describes the entry to a reader.</summary>
    public string DescriptionTemplate { get; private set; } = string.Empty;

    /// <summary>Gets the template's parameters, as the stored JSON document.</summary>
    public string DescriptionParametersJson { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the identity of the entry this one corrects, when it is an operator's compensating entry
    /// (`FIN-12`), or null for an ordinary entry.
    /// </summary>
    public Guid? ReversesEntryId { get; private set; }

    /// <summary>Gets when the entry was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Records a ledger entry. Called by <see cref="ClubAccount.Post"/> with the balances the account
    /// actually holds after the move, so an entry cannot claim a result the account did not reach.
    /// </summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="clubId">The club whose money moved.</param>
    /// <param name="sequence">The entry's position in the club's ledger, starting at one.</param>
    /// <param name="category">What the entry is.</param>
    /// <param name="cashDeltaMinor">The signed change to cash.</param>
    /// <param name="reservedDeltaMinor">The signed change to reserved funds.</param>
    /// <param name="resultingCashMinor">The cash the club holds after the move.</param>
    /// <param name="resultingReservedMinor">The reserved funds the club holds after the move.</param>
    /// <param name="sourceType">The kind of workflow that produced it.</param>
    /// <param name="sourceId">The thing that produced it, or null.</param>
    /// <param name="correlationId">The operation's correlation key.</param>
    /// <param name="descriptionTemplate">The stable template key.</param>
    /// <param name="descriptionParametersJson">The template's parameters, as a stored document.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="reversesEntryId">The entry this one corrects, when it is a compensating entry (`FIN-12`).</param>
    public static LedgerEntry Record(
        Guid id,
        Guid clubId,
        long sequence,
        LedgerCategory category,
        long cashDeltaMinor,
        long reservedDeltaMinor,
        long resultingCashMinor,
        long resultingReservedMinor,
        LedgerSourceType sourceType,
        Guid? sourceId,
        string correlationId,
        string descriptionTemplate,
        string descriptionParametersJson,
        DateTimeOffset now,
        Guid? reversesEntryId = null)
    {
        if (clubId == Guid.Empty)
        {
            throw new ArgumentException("An entry belongs to a club.", nameof(clubId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionParametersJson);

        if (correlationId.Length > MaxCorrelationIdLength)
        {
            throw new ArgumentException(
                $"A correlation key is at most {MaxCorrelationIdLength} characters.",
                nameof(correlationId));
        }

        if (descriptionTemplate.Length > MaxDescriptionTemplateLength)
        {
            throw new ArgumentException(
                $"A description template is at most {MaxDescriptionTemplateLength} characters.",
                nameof(descriptionTemplate));
        }

        // The rules the database also enforces (FIN-13): a balance is never negative and reserved funds
        // never exceed the cash behind them. Checked here so a caller cannot even build the row.
        if (resultingCashMinor < 0 || resultingReservedMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingCashMinor),
                resultingCashMinor,
                "A balance is never negative (FIN-13).");
        }

        if (resultingReservedMinor > resultingCashMinor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultingReservedMinor),
                resultingReservedMinor,
                "Reserved funds may never exceed the cash behind them (FIN-10, FIN-13).");
        }

        // A compensating entry names the entry it corrects; only a compensation may carry the link, and it
        // cannot name itself (FIN-12).
        if (reversesEntryId == Guid.Empty || reversesEntryId == id)
        {
            throw new ArgumentException(
                "A compensating entry names a different entry it corrects (FIN-12).",
                nameof(reversesEntryId));
        }

        if (reversesEntryId is not null && category != LedgerCategory.Compensation)
        {
            throw new ArgumentException(
                "Only a compensating entry may name the entry it corrects (FIN-12).",
                nameof(reversesEntryId));
        }

        return new LedgerEntry
        {
            Id = id,
            ClubId = clubId,
            Sequence = sequence,
            Category = category,
            CashDeltaMinor = cashDeltaMinor,
            ReservedDeltaMinor = reservedDeltaMinor,
            ResultingCashMinor = resultingCashMinor,
            ResultingReservedMinor = resultingReservedMinor,
            SourceType = sourceType,
            SourceId = sourceId,
            CorrelationId = correlationId,
            DescriptionTemplate = descriptionTemplate,
            DescriptionParametersJson = descriptionParametersJson,
            ReversesEntryId = reversesEntryId,
            CreatedAt = now,
        };
    }
}
