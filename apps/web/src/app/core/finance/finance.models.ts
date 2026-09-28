/**
 * The finance module's transport mirror (master plan §10.7; `FIN-3`…`FIN-9`).
 *
 * Hand-written to match `TouchlineManager.Contracts.Finance`, in minor units of the one canonical currency.
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
