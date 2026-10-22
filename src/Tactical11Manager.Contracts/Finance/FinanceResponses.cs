namespace Tactical11Manager.Contracts.Finance;

/// <summary>A club's money and its season so far (master plan §10.7, `FIN-3`…`FIN-9`).</summary>
/// <param name="CashMinor">The club's cash, in minor units.</param>
/// <param name="ReservedMinor">The part already committed to open bids.</param>
/// <param name="AvailableMinor">What the club can still commit, after reservations (`FIN-10`).</param>
/// <param name="WeeklyWageMinor">The club's active contracts' wage bill for one weekly run (`FIN-7`): three matchdays of the season wages.</param>
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
/// <param name="Outlook">Where the club's money is heading this season: its state, its forecast by stream, its wage budget and the prize at its present place. Null before a season exists.</param>
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
    DateTimeOffset ServerTime,
    FinanceOutlookResponse? Outlook = null);

/// <summary>Where a club's money is heading this season (master plan §10.7; plan toasty-scott sections 2a, 5 and 6).</summary>
/// <remarks>
/// Every figure is the server's, so the screen sets the forecast against the season so far and prints the prize without working either out
/// (`INT-1`). The forecast leaves out bonuses and prize money, which are upside.
/// </remarks>
/// <param name="State">The club's financial state: <c>healthy</c>, <c>in_the_red</c> or <c>administration</c> (`DEBT-1`).</param>
/// <param name="Embargo">Whether a transfer and building embargo is on: the club is in the red or in administration (`DEBT-3`).</param>
/// <param name="AdministrationMatchdaysLeft">While in administration, the matchdays left before the manager is dismissed (`DEBT-5`); otherwise null.</param>
/// <param name="MatchdaysPlayed">The matchdays the club's division has published this season, 0 to 34.</param>
/// <param name="MatchdaysInSeason">The matchdays in a season.</param>
/// <param name="SeasonWageMinor">The season wage bill of the active contracts.</param>
/// <param name="WageBudgetMinor">The wage bill the club should stay within: 70% of the forecast (`BUD-2`).</param>
/// <param name="WageShareBp">The wage bill as a share of the forecast, in basis points.</param>
/// <param name="ForecastMinor">The season income forecast at the present fans and ground (`BUD-1`).</param>
/// <param name="OverdraftLimitMinor">How far below zero the cash may go before administration (`DEBT-1`).</param>
/// <param name="OverBudget">Whether the wage bill is over the wage budget.</param>
/// <param name="Streams">The forecast by ledger category: income as a positive amount, cost as a negative one, wages included.</param>
/// <param name="Prize">What the league would pay at the club's present place, or null before a table exists.</param>
public sealed record FinanceOutlookResponse(
    string State,
    bool Embargo,
    int? AdministrationMatchdaysLeft,
    int MatchdaysPlayed,
    int MatchdaysInSeason,
    long SeasonWageMinor,
    long WageBudgetMinor,
    int WageShareBp,
    long ForecastMinor,
    long OverdraftLimitMinor,
    bool OverBudget,
    IReadOnlyList<FinanceForecastLineResponse> Streams,
    FinancePrizeResponse? Prize);

/// <summary>One stream of the season forecast, under the ledger category the money is booked as.</summary>
/// <param name="Category">The ledger category, as a stable lowercase code (e.g. <c>gate_receipt</c>).</param>
/// <param name="ForecastMinor">The season's forecast: positive for income, negative for cost.</param>
public sealed record FinanceForecastLineResponse(string Category, long ForecastMinor);

/// <summary>The league prize money at the club's present place: "if the season ended today" (`FIN-5`).</summary>
/// <param name="Place">The club's place in the table now.</param>
/// <param name="Clubs">The clubs in the table.</param>
/// <param name="AmountMinor">What the league pays that place at the club's tier.</param>
public sealed record FinancePrizeResponse(int Place, int Clubs, long AmountMinor);

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

/// <summary>How a signing or a renewal sits with the club's money, computed by the server for the viewer's club (`WAGE-1`, plan toasty-scott section 5).</summary>
/// <remarks>
/// A warning, never a block: the deal goes ahead whatever the level, and the form asks for a confirm tick at <c>danger</c> and
/// <c>administration_risk</c>. Only the cash for the fee itself (`FIN-10`) and the embargo while the club is in the red stop a bid.
/// </remarks>
/// <param name="Level">One of <c>fits</c>, <c>stretching</c>, <c>danger</c> and <c>administration_risk</c>.</param>
/// <param name="AddedSeasonWageMinor">What the deal adds to the season's wages: the whole wage of a signing, the rise of a renewal.</param>
/// <param name="WageBillAfterMinor">The season wage bill with the deal, leading bids included.</param>
/// <param name="WageBudgetMinor">The budget the bill is held against: 70% of the season income forecast.</param>
/// <param name="WageShareBp">The bill with the deal as a share of the forecast, in basis points.</param>
/// <param name="SeasonEndCashMinor">The cash the club would hold at the season's end after the fee, at today's rates.</param>
/// <param name="RedFromMatchday">The matchday, 1 to 34, the club would first be in the red, or null when it would not be within the season.</param>
/// <param name="NeedsConfirmation">Whether the form asks for a confirm tick.</param>
public sealed record WageImpactResponse(
    string Level,
    long AddedSeasonWageMinor,
    long WageBillAfterMinor,
    long WageBudgetMinor,
    int WageShareBp,
    long SeasonEndCashMinor,
    int? RedFromMatchday,
    bool NeedsConfirmation);
