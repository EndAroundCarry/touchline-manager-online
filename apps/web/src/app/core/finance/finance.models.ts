/**
 * The finance module's transport mirror (master plan §10.7; `FIN-3`…`FIN-9`).
 *
 * Hand-written to match `Tactical11Manager.Contracts.Finance`, in minor units of the one canonical currency.
 * Amounts are never divided here; the screens format for display and the server does every piece of
 * arithmetic that matters.
 */

/** A club's money and its season so far. */
export interface FinanceSummary {
  readonly cashMinor: number;
  readonly reservedMinor: number;
  readonly availableMinor: number;
  readonly weeklyWageMinor: number;
  readonly contractedPlayers: number;
  readonly weeklySponsorshipMinor: number;
  readonly weeklyOperatingCostMinor: number;
  readonly tierNumber: number;
  readonly seasonLabel: string | null;
  readonly warnings: readonly FinanceWarning[];
  readonly totals: readonly FinanceCategoryTotal[];
  readonly serverTime: string;

  /** Where the club's money is heading this season, or null before a season exists. */
  readonly outlook: FinanceOutlook | null;
}

/** A club's money state (`DEBT-1`): healthy, in the red, or in administration. */
export type FinancialState = 'healthy' | 'in_the_red' | 'administration';

/**
 * Where a club's money is heading this season (plan toasty-scott sections 2a, 5 and 6).
 *
 * Every figure is the server's, so the screen sets the forecast against the season so far and prints the prize
 * without working either out (`INT-1`).
 */
export interface FinanceOutlook {
  readonly state: FinancialState;

  /** Whether bids and building are refused: the club is in the red or in administration (`DEBT-3`). */
  readonly embargo: boolean;

  /** While in administration, the matchdays left before the manager is dismissed; otherwise null. */
  readonly administrationMatchdaysLeft: number | null;
  readonly matchdaysPlayed: number;
  readonly matchdaysInSeason: number;
  readonly seasonWageMinor: number;
  readonly wageBudgetMinor: number;

  /** The wage bill as a share of the forecast, in basis points. */
  readonly wageShareBp: number;
  readonly forecastMinor: number;
  readonly overdraftLimitMinor: number;
  readonly overBudget: boolean;

  /** The forecast by ledger category: income positive, cost negative. */
  readonly streams: readonly FinanceForecastLine[];

  /** What the league would pay at the club's present place, or null before a table exists. */
  readonly prize: FinancePrize | null;
}

/** One stream of the season forecast, under the ledger category the money is booked as. */
export interface FinanceForecastLine {
  readonly category: string;
  readonly forecastMinor: number;
}

/** The league prize money at the club's present place: "if the season ended today". */
export interface FinancePrize {
  readonly place: number;
  readonly clubs: number;
  readonly amountMinor: number;
}

/** How a signing or a renewal sits with the club's money, computed by the server (`WAGE-1`). */
export type WageImpactLevel = 'fits' | 'stretching' | 'danger' | 'administration_risk';

/**
 * What a deal would do to the viewer's club.
 *
 * A warning, never a block: the deal goes ahead whatever the level, and the form asks for a confirm tick at
 * `danger` and `administration_risk`.
 */
export interface WageImpact {
  readonly level: WageImpactLevel;

  /** What the deal adds to the season's wages: the whole wage of a signing, the rise of a renewal. */
  readonly addedSeasonWageMinor: number;
  readonly wageBillAfterMinor: number;
  readonly wageBudgetMinor: number;

  /** The bill with the deal as a share of the forecast, in basis points. */
  readonly wageShareBp: number;

  /** The cash the club would hold at the season's end after the fee, at today's rates. */
  readonly seasonEndCashMinor: number;

  /** The matchday, 1 to 34, the club would first be in the red, or null when it would not be within the season. */
  readonly redFromMatchday: number | null;
  readonly needsConfirmation: boolean;
}

/** A risk the club is running, as a stable code and a sentence (`FIN-16`, `SQ-2`). */
export interface FinanceWarning {
  readonly code: string;
  readonly message: string;
}

/** One category's net cash movement over the season. */
export interface FinanceCategoryTotal {
  readonly category: string;
  readonly amountMinor: number;
}

/** One page of a club's ledger, newest first. */
export interface FinanceLedgerPage {
  readonly entries: readonly FinanceLedgerEntry[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}

/** One immutable ledger line. */
export interface FinanceLedgerEntry {
  readonly id: string;
  readonly sequence: number;
  readonly category: string;
  readonly cashDeltaMinor: number;
  readonly reservedDeltaMinor: number;
  readonly resultingCashMinor: number;
  readonly resultingReservedMinor: number;
  readonly description: string;
  readonly createdAt: string;
}
