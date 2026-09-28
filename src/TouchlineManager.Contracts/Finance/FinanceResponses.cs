namespace TouchlineManager.Contracts.Finance;

/// <summary>A club's money and its season so far (master plan §10.7, `FIN-3`…`FIN-9`).</summary>
/// <param name="CashMinor">The club's cash, in minor units.</param>
/// <param name="ReservedMinor">The part already committed to open bids.</param>
/// <param name="AvailableMinor">What the club can still commit, after reservations (`FIN-10`).</param>
/// <param name="WeeklyWageMinor">The club's active contracts' weekly wage bill (`FIN-7`).</param>
/// <param name="ContractedPlayers">How many active contracts that bill covers.</param>
/// <param name="WeeklySponsorshipMinor">The weekly sponsorship credit the club's tier earns (`FIN-4`).</param>
/// <param name="WeeklyOperatingCostMinor">The weekly operating cost the club's tier pays (`FIN-9`).</param>
/// <param name="TierNumber">The tier the club plays in, which scales the credit and the cost.</param>
/// <param name="SeasonLabel">The season the totals cover, or null before a season exists.</param>
/// <param name="Warnings">
/// The risks the club is running, in a stable order: expiring contracts, payroll risk, and a squad below the
/// minimum (`FIN-16`, `SQ-2`). Empty when there is nothing to warn about.
/// </param>
/// <param name="Totals">The season's ledger totals by category, in a stable category order.</param>
/// <param name="ServerTime">The server's current instant, so client clock drift is visible.</param>
public sealed record FinanceSummaryResponse(
    long CashMinor,
    long ReservedMinor,
    long AvailableMinor,
    long WeeklyWageMinor,
    int ContractedPlayers,
    long WeeklySponsorshipMinor,
    long WeeklyOperatingCostMinor,
    int TierNumber,
    string? SeasonLabel,
    IReadOnlyList<FinanceWarningResponse> Warnings,
    IReadOnlyList<FinanceCategoryTotalResponse> Totals,
    DateTimeOffset ServerTime);

/// <summary>A risk the club is running, as a stable code and a sentence (master plan §11.1).</summary>
/// <remarks>
/// The code is what a client branches and localises on; the message is the current English rendering, the
/// same token-plus-text contract the inbox and the commentary use.
/// </remarks>
/// <param name="Code">The stable warning code.</param>
/// <param name="Message">The English sentence describing it.</param>
public sealed record FinanceWarningResponse(string Code, string Message);

/// <summary>One category's total for the season, as a stable code and a signed amount.</summary>
/// <param name="Category">The ledger category, as a stable lowercase code (e.g. <c>gate_receipt</c>).</param>
/// <param name="AmountMinor">The net cash the category moved this season, in minor units.</param>
public sealed record FinanceCategoryTotalResponse(string Category, long AmountMinor);

/// <summary>One page of a club's ledger, newest first (master plan §10.7, `FIN-12`).</summary>
/// <param name="Entries">The page's entries, newest first.</param>
/// <param name="NextCursor">The cursor that continues the walk, or null at the end of the ledger.</param>
/// <param name="ServerTime">The server's current instant.</param>
public sealed record FinanceLedgerResponse(
    IReadOnlyList<FinanceLedgerEntryResponse> Entries,
    string? NextCursor,
    DateTimeOffset ServerTime);

/// <summary>One immutable ledger line, as a manager reads it (`FIN-11`, `FIN-12`).</summary>
/// <remarks>
/// The description is rendered from the row's template and parameters rather than stored, so the wording
/// stays a presentation concern the way a commentated match and an inbox message do (`MAT-8`).
/// </remarks>
/// <param name="Id">The entry identity.</param>
/// <param name="Sequence">The entry's position in the club's ledger, starting at one.</param>
/// <param name="Category">What the entry is, as a stable lowercase code.</param>
/// <param name="CashDeltaMinor">The signed change to cash.</param>
/// <param name="ReservedDeltaMinor">The signed change to reserved funds.</param>
/// <param name="ResultingCashMinor">The cash the club held after the move.</param>
/// <param name="ResultingReservedMinor">The reserved funds the club held after the move.</param>
/// <param name="Description">The rendered one-line description.</param>
/// <param name="CreatedAt">When the entry was written.</param>
public sealed record FinanceLedgerEntryResponse(
    Guid Id,
    long Sequence,
    string Category,
    long CashDeltaMinor,
    long ReservedDeltaMinor,
    long ResultingCashMinor,
    long ResultingReservedMinor,
    string Description,
    DateTimeOffset CreatedAt);
