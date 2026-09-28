/**
 * Client-side presentation helpers for the finance screen (`FIN-3`…`FIN-9`, master plan §11.1).
 *
 * Small and pure, so they can be unit tested without a component. The server owns the category code and the
 * signed amount; these helpers name the category, split the totals into money in and money out, and format a
 * signed movement so a debit and a credit never look alike by colour alone.
 */
import { FinanceCategoryTotal } from './finance.models';

const CATEGORY_LABELS: Record<string, string> = {
  opening_balance: 'Opening balance',
  gate_receipt: 'Gate receipts',
  sponsorship: 'Sponsorship',
  wages: 'Wages',
  operating_cost: 'Operating costs',
  position_award: 'Position awards',
  transfer_payment: 'Transfer fees paid',
  transfer_proceeds: 'Transfer fees received',
  bid_reservation: 'Bid reservations',
  reservation_release: 'Reservations released',
  emergency_grant: 'Emergency grants',
  compensation: 'Compensating entries',
};

/** One labelled total for display. */
export interface LabelledTotal {
  readonly label: string;
  readonly amountMinor: number;
}

/** Names a ledger category, falling back to the code so a new one still renders. */
export function categoryLabel(code: string): string {
  return CATEGORY_LABELS[code] ?? code;
}

/** The season's money in: categories whose net movement is a credit, in a stable order. */
export function creditsOf(totals: readonly FinanceCategoryTotal[]): readonly LabelledTotal[] {
  return totals
    .filter((total) => total.amountMinor > 0)
    .map((total) => ({ label: categoryLabel(total.category), amountMinor: total.amountMinor }));
}

/** The season's money out: categories whose net movement is a debit, in a stable order. */
export function debitsOf(totals: readonly FinanceCategoryTotal[]): readonly LabelledTotal[] {
  return totals
    .filter((total) => total.amountMinor < 0)
    .map((total) => ({ label: categoryLabel(total.category), amountMinor: total.amountMinor }));
}

/**
 * Formats a signed movement as text that carries its own sign.
 *
 * A bare number in a colour would be unreadable to anyone who cannot distinguish the colour (`§11.3`), so a
 * debit is prefixed with a minus and a credit with a plus, and a movement that moved nothing is a dash.
 */
export function signedAmount(minorUnits: number, format: (value: number) => string): string {
  if (minorUnits === 0) {
    return '—';
  }

  return `${minorUnits > 0 ? '+' : '−'}${format(Math.abs(minorUnits))}`;
}
