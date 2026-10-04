import { attributeLabel } from './training-presentation';
import type {
  PlayerTrainingAttributeChange,
  PlayerTrainingDay,
  PlayerTrainingSummary,
} from './training.models';

/**
 * Data shaping for the player Training tab's chart (`TRN-17`).
 *
 * Pure functions, so every choice the manager makes on the chart controls (which regime, daily or
 * cumulative, how far back) is unit tested without a canvas. The component draws what these return, and the
 * same rows feed the accessible data table, so the chart and its table cannot disagree.
 */

/** The value of the regime select's "all regimes" choice. */
export const ALL_REGIMES = '';

/** Whether a series plots each day's growth, or the running total of what that regime has delivered. */
export type ChartMode = 'daily' | 'cumulative';

/** How far back the chart reaches, in calendar days from the latest recorded day, or the whole history. */
export type ChartRange = '30' | '90' | 'all';

/** What the manager has chosen on the chart controls. */
export interface ChartFilter {
  /** A programme code, or {@link ALL_REGIMES}. */
  readonly regime: string;
  readonly mode: ChartMode;
  readonly range: ChartRange;
}

/** The controls as the Training tab opens: every regime, day by day, over the last 90 days. */
export const DEFAULT_CHART_FILTER: ChartFilter = {
  regime: ALL_REGIMES,
  mode: 'daily',
  range: '90',
};

/** One regime's line. */
export interface ChartSeries {
  readonly programme: string;
  readonly label: string;

  /** The line and point colour. Never the only way to tell a series apart: see `dash` and `pointStyle`. */
  readonly color: string;

  /** The line's dash pattern, empty for solid. */
  readonly dash: readonly number[];

  /** The Chart.js point style. */
  readonly pointStyle: string;

  /** One value per label, or null on a day the player trained a different regime. */
  readonly data: readonly (number | null)[];

  /** One note per label, e.g. "+1 Finishing", or null when no attribute moved that day. */
  readonly notes: readonly (string | null)[];
}

/** One plotted point, for the data table. */
export interface ChartRow {
  /** The progression day, as an ISO date. */
  readonly day: string;
  readonly programme: string;
  readonly label: string;
  readonly value: number;

  /** Which attributes moved, or an empty string. */
  readonly changes: string;
}

/** Everything the chart and its table draw. */
export interface TrainingChart {
  /** The x-axis labels, in day order. */
  readonly labels: readonly string[];

  /** The same days as ISO dates. */
  readonly days: readonly string[];
  readonly series: readonly ChartSeries[];
  readonly rows: readonly ChartRow[];
}

// The Okabe-Ito colours that read on white, in the order the validator accepts. The seventh, yellow, is left
// out because it is invisible on a white surface, and black because it reads as chart ink rather than a series.
const COLORS = ['#0072B2', '#D55E00', '#009E73', '#CC79A7', '#E69F00', '#56B4E9'];
const DASHES: readonly (readonly number[])[] = [[], [6, 3], [2, 3], [8, 3, 2, 3]];
const POINT_STYLES = ['circle', 'triangle', 'rect', 'rectRot', 'cross', 'star'];

/**
 * The programme codes in the order each is given a look. A programme keeps its look whatever else is on the
 * chart, because the look follows the programme and not its rank on one player's history.
 */
const REGIME_ORDER = [
  'goalkeeper',
  'defender',
  'wingback',
  'midfielder',
  'winger',
  'forward',
  'mental',
  'physical',
  'recovery',
];

/** The look of one regime: colour, dash and point style, which together are unique across the programmes. */
export interface RegimeStyle {
  readonly color: string;
  readonly dash: readonly number[];
  readonly pointStyle: string;
}

/** The look of a programme. A code the client does not know gets one after the known ones, by a stable hash. */
export function regimeStyle(programme: string): RegimeStyle {
  const known = REGIME_ORDER.indexOf(programme);
  const index = known >= 0 ? known : REGIME_ORDER.length + stableHash(programme);

  return {
    color: COLORS[index % COLORS.length],
    dash: DASHES[index % DASHES.length],
    pointStyle: POINT_STYLES[index % POINT_STYLES.length],
  };
}

function stableHash(text: string): number {
  let hash = 0;

  for (const character of text) {
    hash = (hash * 31 + character.charCodeAt(0)) % 9973;
  }

  return hash;
}

/** The programmes in a run of days, in the order the player first trained each. */
export function regimesIn(days: readonly PlayerTrainingDay[]): readonly string[] {
  return [...new Set(days.map((day) => day.programme))];
}

/** Names the regimes by their summary lines, which is where the server sends the labels. */
export function regimeLabels(
  summary: readonly PlayerTrainingSummary[],
): ReadonlyMap<string, string> {
  return new Map(summary.map((line) => [line.programme, line.label]));
}

/** The days inside the chosen range, counted back from the latest recorded day. */
export function daysInRange(
  days: readonly PlayerTrainingDay[],
  range: ChartRange,
): readonly PlayerTrainingDay[] {
  if (range === 'all' || days.length === 0) {
    return days;
  }

  const latest = Math.max(...days.map((day) => dayNumber(day.day)));
  const earliest = latest - (Number(range) - 1);

  return days.filter((day) => dayNumber(day.day) >= earliest);
}

/** Words for one attribute change list: "+1 Finishing, −1 Pace". Empty when nothing moved. */
export function changesLabel(changes: readonly PlayerTrainingAttributeChange[]): string {
  return changes
    .map((change) => {
      const sign = change.delta > 0 ? '+' : '−';

      return `${sign}${Math.abs(change.delta)} ${attributeLabel(change.attribute)}`;
    })
    .join(', ');
}

/** A day as the x-axis shows it, in the viewer's locale; the raw value when it is not a date. */
export function dayLabel(isoDate: string, locale: string): string {
  const date = new Date(`${isoDate}T00:00:00Z`);

  return Number.isNaN(date.getTime())
    ? isoDate
    : new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(
        date,
      );
}

/**
 * Shapes the history for the chart.
 *
 * The range applies first, then the regime. With every regime shown there is one series per regime that
 * appears in the range, and a series is null on the days the player trained something else, so the line
 * breaks where the regime does. Cumulative restarts at zero for each regime: it is what that regime has
 * delivered, and the summary table beside the chart gives the totals.
 */
export function buildChart(
  days: readonly PlayerTrainingDay[],
  summary: readonly PlayerTrainingSummary[],
  filter: ChartFilter,
  locale: string,
): TrainingChart {
  const ranged = daysInRange(days, filter.range);
  const programmes = regimesIn(ranged).filter(
    (programme) => filter.regime === ALL_REGIMES || programme === filter.regime,
  );
  const shown = ranged.filter((day) => programmes.includes(day.programme));
  const isoDays = [...new Set(shown.map((day) => day.day))].sort();
  const names = regimeLabels(summary);

  const series = programmes.map((programme): ChartSeries => {
    const own = new Map(
      shown.filter((day) => day.programme === programme).map((day) => [day.day, day]),
    );
    let total = 0;

    const points = isoDays.map((iso) => {
      const day = own.get(iso);

      if (day === undefined) {
        return { value: null, note: null };
      }

      total = round(total + day.growth);

      return {
        value: filter.mode === 'daily' ? round(day.growth) : total,
        note: day.attributeChanges.length === 0 ? null : changesLabel(day.attributeChanges),
      };
    });

    return {
      programme,
      label: names.get(programme) ?? programme,
      ...regimeStyle(programme),
      data: points.map((point) => point.value),
      notes: points.map((point) => point.note),
    };
  });

  const rows = series
    .flatMap((line) =>
      isoDays.flatMap((day, index) => {
        const value = line.data[index];

        return value === null
          ? []
          : [
              {
                day,
                programme: line.programme,
                label: line.label,
                value,
                changes: line.notes[index] ?? '',
              },
            ];
      }),
    )
    .sort((a, b) => a.day.localeCompare(b.day));

  return { labels: isoDays.map((iso) => dayLabel(iso, locale)), days: isoDays, series, rows };
}

/** What a screen reader hears for the canvas: what is plotted, over how many days, and how many regimes. */
export function chartAriaLabel(chart: TrainingChart, mode: ChartMode): string {
  if (chart.days.length === 0) {
    return 'Training chart: no days recorded in this range.';
  }

  const what = mode === 'daily' ? 'Daily attribute growth' : 'Cumulative attribute growth';
  const regimes = chart.series.length === 1 ? '1 regime' : `${chart.series.length} regimes`;

  return `${what} in points, ${regimes}, ${chart.days.length} days from ${chart.labels[0]} to ${chart.labels[chart.labels.length - 1]}. The table below lists every point.`;
}

function dayNumber(isoDate: string): number {
  return Math.floor(Date.parse(`${isoDate}T00:00:00Z`) / 86_400_000);
}

function round(value: number): number {
  return Math.round(value * 1000) / 1000;
}
