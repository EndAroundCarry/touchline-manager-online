/**
 * Client-side presentation helpers for the finance screen (`FIN-3`…`FIN-9`, master plan §11.1).
 *
 * Small and pure, so they can be unit tested without a component. The server owns the category code and the
 * signed amount; these helpers name the category, split the totals into money in and money out, and format a
 * signed movement so a debit and a credit never look alike by colour alone.
 */
import {
  FinanceCategoryTotal,
  FinanceOutlook,
  WageImpact,
  WageImpactLevel,
} from './finance.models';
import { ordinalOf } from './sponsor-presentation';

const CATEGORY_LABELS: Record<string, string> = {
  opening_balance: 'Opening balance',
  gate_receipt: 'Gate receipts',
  sponsorship: 'Sponsorship',
  wages: 'Wages',
  operating_cost: 'Operating costs',
  position_award: 'League prize money',
  transfer_payment: 'Transfer fees paid',
  transfer_proceeds: 'Transfer fees received',
  bid_reservation: 'Bid reservations',
  reservation_release: 'Reservations released',
  emergency_grant: 'Emergency grants',
  compensation: 'Compensating entries',
  stadium_construction: 'Stadium construction',
  release_payout: 'Release payouts',
  snack_bar: 'Snack bar',
  merchandise: 'Merchandise',
  structure_upkeep: 'Structure upkeep',
  structure_upgrade: 'Structure upgrades',
  league_tv: 'League TV money',
  sponsor_matchday: 'Sponsor',
  sponsor_bonus: 'Sponsor bonus',
  parachute: 'Parachute payment',
  ground_upkeep: 'Ground upkeep',
  clean_start: 'Clean start',
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

/** How a piece of news weighs: fine, worth a look, or a problem. Always written out too, never colour alone (`§11.3`). */
export type MoneyTone = 'good' | 'warn' | 'bad';

/** The classes a tone is drawn in. */
export function moneyToneClass(tone: MoneyTone): string {
  switch (tone) {
    case 'good':
      return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200';
    case 'bad':
      return 'border-red-500/40 bg-red-500/10 text-red-200';
    default:
      return 'border-amber-500/40 bg-amber-500/10 text-amber-200';
  }
}

/** The banner a club in the red or in administration is shown on its finances page. */
export interface StateBanner {
  readonly tone: MoneyTone;
  readonly title: string;
  readonly body: string;
}

/**
 * The state banner (`DEBT-1`…`DEBT-5`), or null for a healthy club.
 *
 * It says what the state stops and what ends it; the matchdays left are the server's.
 */
export function stateBanner(outlook: FinanceOutlook | null): StateBanner | null {
  if (outlook === null) {
    return null;
  }

  if (outlook.state === 'administration') {
    const left = outlook.administrationMatchdaysLeft ?? 0;

    return {
      tone: 'bad',
      title: 'The club is in administration',
      body: `Cash is past the overdraft limit. The board is selling your highest earners, and bids and building are stopped. If the club is still in administration in ${matchdays(left)}, the manager is dismissed.`,
    };
  }

  if (outlook.state === 'in_the_red') {
    return {
      tone: 'warn',
      title: 'The club is in the red',
      body: 'Cash is below zero, so bids for players and new building are stopped until it is back above zero. There is no interest, and the fans do not mind.',
    };
  }

  return null;
}

function matchdays(count: number): string {
  return count === 1 ? '1 matchday' : `${count} matchdays`;
}

/** One line of the forecast set against the season so far. */
export interface StreamRow {
  readonly category: string;
  readonly label: string;

  /** The season's forecast: positive for income, negative for cost. */
  readonly forecastMinor: number;

  /** What the ledger has moved so far this season under the category. */
  readonly actualMinor: number;
}

/**
 * Sets the forecast by stream against the season's ledger totals (`BUD-1`).
 *
 * A stream the club has neither forecast nor taken anything under is left out, so a club without a sponsor deal
 * does not see an empty row. The order is the server's: income first, then costs.
 */
export function streamRows(
  outlook: FinanceOutlook,
  totals: readonly FinanceCategoryTotal[],
): readonly StreamRow[] {
  return outlook.streams
    .map((stream) => ({
      category: stream.category,
      label: categoryLabel(stream.category),
      forecastMinor: stream.forecastMinor,
      actualMinor: totals.find((total) => total.category === stream.category)?.amountMinor ?? 0,
    }))
    .filter((row) => row.forecastMinor !== 0 || row.actualMinor !== 0);
}

/** The prize that would be paid at the present place, in words: "7th of 18". */
export function placeLabel(place: number, clubs: number): string {
  return `${ordinalOf(place)} of ${clubs}`;
}

/** What the wage gauge draws: how full the bar is and where the budget marker stands, as percentages of its width. */
export interface WageGauge {
  readonly fillPercent: number;
  readonly budgetPercent: number;
  readonly sharePercent: number;
  readonly tone: MoneyTone;
  readonly caption: string;
}

/**
 * The wage-budget gauge (`BUD-2`): the wage bill as a share of the income forecast, with the budget marked.
 *
 * The bar is scaled so the budget marker never sits at the end, which leaves room to show a bill that has gone over it.
 */
export function wageGauge(outlook: FinanceOutlook): WageGauge {
  const share = outlook.wageShareBp / 100;
  const budgetShare =
    outlook.forecastMinor > 0 ? (outlook.wageBudgetMinor * 100) / outlook.forecastMinor : 0;
  const scale = Math.max(100, Math.ceil(Math.max(share, budgetShare) / 10) * 10 + 10);

  return {
    fillPercent: Math.min(100, (share * 100) / scale),
    budgetPercent: Math.min(100, (budgetShare * 100) / scale),
    sharePercent: Math.round(share),
    tone: outlook.overBudget ? (share >= 100 ? 'bad' : 'warn') : 'good',
    caption: outlook.overBudget ? 'Over the wage budget' : 'Within the wage budget',
  };
}

/** The words for a wage-impact level. */
export function wageImpactHeadline(level: WageImpactLevel): string {
  switch (level) {
    case 'fits':
      return 'Fits your budget';
    case 'stretching':
      return 'Stretches your budget';
    case 'danger':
      return 'Danger: you would run out of cash';
    default:
      return 'Administration risk';
  }
}

/** The tone a wage-impact level is drawn in. */
export function wageImpactTone(level: WageImpactLevel): MoneyTone {
  switch (level) {
    case 'fits':
      return 'good';
    case 'stretching':
      return 'warn';
    default:
      return 'bad';
  }
}

/** The sentence a wage impact adds when the club would go into the red this season, or null when it would not. */
export function redFromSentence(impact: WageImpact): string | null {
  if (impact.redFromMatchday === null) {
    return null;
  }

  return impact.level === 'administration_risk'
    ? `You would be in the red from about matchday ${impact.redFromMatchday} and end the season past your overdraft limit.`
    : `You would be in the red from about matchday ${impact.redFromMatchday}.`;
}
