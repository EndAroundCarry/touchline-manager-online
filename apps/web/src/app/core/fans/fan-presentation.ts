import { LineChartSeries } from '../../shared/ui/line-chart/line-chart';
import { preferredLocale, preferredTimeZone } from '../world/presentation';
import { FanEvent, FanEventKind, FanExpectation } from './fan.models';

/** How an expectation reads at a glance: the colour family of the chip. The words always carry the meaning. */
export type ExpectationTone = 'good' | 'even' | 'bad';

/** One mood word with its band, for the key under the satisfaction chart. */
export interface MoodBand {
  readonly code: string;
  readonly phrase: string;

  /** The lowest satisfaction, 0–100, that earns the word. */
  readonly from: number;
}

/** The seven mood words, lowest first. Each band includes its lower edge (`FAN-5`). */
export const MOOD_BANDS: readonly MoodBand[] = [
  { code: 'furious', phrase: 'Furious', from: 0 },
  { code: 'angry', phrase: 'Angry', from: 10 },
  { code: 'unhappy', phrase: 'Unhappy', from: 25 },
  { code: 'calm', phrase: 'Calm', from: 40 },
  { code: 'content', phrase: 'Content', from: 60 },
  { code: 'delighted', phrase: 'Delighted', from: 75 },
  { code: 'euphoric', phrase: 'Euphoric', from: 90 },
];

/** The edges of the Calm band, drawn as dashed lines on the satisfaction chart. */
export const CALM_FROM = 40;
export const CALM_TO = 60;

/** The share of the soft cap the fans must reach before the cap is drawn; before that it only flattens the line. */
const CAP_VISIBLE_SHARE = 1 / 3;

// The same hues the training chart uses: mid-tones that hold 3:1 against the dark panel and the white one (ADR-0059).
const BLUE = '#2E8FD6';
const ORANGE = '#D4781F';
const GREEN = '#1FA37A';
const OLIVE = '#9C8700';
const VIOLET = '#8A6FE0';

/** The mood on the 0–100 scale the page shows. */
export function moodPoints(moodBp: number): number {
  return moodBp / 100;
}

/** The mood word for a mood in basis points. Mirrors the server's bands so a point on a chart can be named. */
export function moodWordFor(moodBp: number): MoodBand {
  const points = moodPoints(moodBp);
  let word = MOOD_BANDS[0];

  for (const band of MOOD_BANDS) {
    if (points >= band.from) {
      word = band;
    }
  }

  return word;
}

/** A signed whole number with a real minus sign, so a fall and a rise differ by more than colour. */
export function signedNumber(value: number): string {
  const grouped = new Intl.NumberFormat(preferredLocale()).format(Math.abs(value));

  if (value > 0) {
    return `+${grouped}`;
  }

  return value < 0 ? `−${grouped}` : grouped;
}

/** A whole number with the viewer's digit grouping. */
export function groupedNumber(value: number): string {
  return new Intl.NumberFormat(preferredLocale()).format(value);
}

/** A change in satisfaction in points, to one decimal, signed. */
export function moodChange(beforeBp: number, afterBp: number): string {
  const points = Math.round((afterBp - beforeBp) / 10) / 10;

  if (points === 0) {
    return '0.0';
  }

  const text = Math.abs(points).toFixed(1);

  return points > 0 ? `+${text}` : `−${text}`;
}

/** Expected points, in hundredths, as the fans' figure: 1.85. */
export function expectedPoints(centis: number): string {
  return (centis / 100).toFixed(2);
}

/**
 * The win, draw and loss chances as whole percentages that add up to 100.
 *
 * Rounding each one alone can give 99 or 101, which looks like a mistake on a card; the largest remainders take the
 * missing points instead.
 */
export function percentages(expectation: FanExpectation): readonly [number, number, number] {
  const raw = [expectation.winBp, expectation.drawBp, expectation.lossBp].map((bp) => bp / 100);
  const floors = raw.map(Math.floor);
  let missing = 100 - floors.reduce((sum, value) => sum + value, 0);

  const order = raw
    .map((value, index) => ({ index, remainder: value - Math.floor(value) }))
    .sort((a, b) => b.remainder - a.remainder || a.index - b.index);

  for (const { index } of order) {
    if (missing <= 0) {
      break;
    }

    floors[index] += 1;
    missing -= 1;
  }

  return [floors[0], floors[1], floors[2]];
}

/** The colour family of an expectation label. */
export function expectationTone(labelCode: string): ExpectationTone {
  switch (labelCode) {
    case 'should_hammer_them':
    case 'expect_a_win':
    case 'should_win':
      return 'good';
    case 'tough_game':
    case 'damage_limitation':
      return 'bad';
    default:
      return 'even';
  }
}

/** The classes of the chip for a tone, the same colours the fixtures screen gives a win, a draw and a defeat. */
export function expectationToneClass(tone: ExpectationTone): string {
  switch (tone) {
    case 'good':
      return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200';
    case 'bad':
      return 'border-red-500/40 bg-red-500/10 text-red-200';
    default:
      return 'border-amber-500/40 bg-amber-500/10 text-amber-200';
  }
}

/** The chances in words: "58% win, 24% draw, 18% loss". */
export function chancesLabel(expectation: FanExpectation): string {
  const [win, draw, loss] = percentages(expectation);

  return `${win}% win, ${draw}% draw, ${loss}% loss`;
}

/** The score the fans would bet on, the club's goals first: "2–1". */
export function predictedScore(expectation: FanExpectation): string {
  return `${expectation.predictedGoalsFor}–${expectation.predictedGoalsAgainst}`;
}

/** What put a point on the chart, in words. */
export function eventKindLabel(kind: FanEventKind): string {
  switch (kind) {
    case 'opening':
      return 'Opening count';
    case 'match':
      return 'Match';
    case 'weekly':
      return 'Weekly review';
    case 'season_reset':
      return 'Season ended';
    case 'promotion':
      return 'Promoted';
    case 'relegation':
      return 'Relegated';
    case 'takeover':
      return 'New manager';
  }
}

/** Where the club stood against the fans' target, in words. Positive means worse than the target. */
export function gapLabel(gap: number | null): string {
  if (gap === null) {
    return '';
  }

  if (gap === 0) {
    return 'on the fans’ target';
  }

  const places = Math.abs(gap);
  const noun = places === 1 ? 'place' : 'places';

  return gap > 0
    ? `${places} ${noun} behind the fans’ target`
    : `${places} ${noun} ahead of the fans’ target`;
}

/** One sentence on what happened at an event, for a tooltip and the data table. */
export function eventNote(event: FanEvent): string {
  switch (event.kind) {
    case 'match': {
      if (event.expectedPointsCentis === null || event.pointsWon === null) {
        return 'Match played';
      }

      return `Fans expected ${expectedPoints(event.expectedPointsCentis)} points, the club took ${event.pointsWon}`;
    }
    case 'weekly':
      return event.gap === null ? 'Weekly review' : `Weekly review: ${gapLabel(event.gap)}`;
    case 'season_reset':
      return event.gap === null
        ? 'Season ended: satisfaction moves halfway back to calm'
        : `Season ended, ${gapLabel(event.gap)}; satisfaction moves halfway back to calm`;
    case 'promotion':
      return 'Promoted: 10% more fans at once';
    case 'relegation':
      return 'Relegated: 10% fewer fans at once';
    case 'takeover':
      return 'New manager: the fans start again at the tier’s defaults';
    case 'opening':
      return 'The count the club opened with';
  }
}

/** The result of holding back what the manager has not yet seen. */
export interface HeldHistory {
  /** The events up to, but not including, the first match whose result is still unseen. */
  readonly events: readonly FanEvent[];

  /** Whether anything was cut. */
  readonly heldBack: boolean;
}

/**
 * Cuts the history before the first match whose result the manager has not yet seen.
 *
 * Every later point would give that result away: the fans and the mood that follow a match move by what happened in
 * it. The events are oldest first, so the history simply ends one step early, and every number on the page that comes
 * from it ends there too.
 *
 * @param events The history, oldest first.
 * @param hiddenFixtureIds The fixtures whose result is still unseen.
 */
export function holdBackHistory(
  events: readonly FanEvent[],
  hiddenFixtureIds: ReadonlySet<string>,
): HeldHistory {
  const cut = events.findIndex(
    (event) =>
      event.kind === 'match' && event.fixtureId !== null && hiddenFixtureIds.has(event.fixtureId),
  );

  return cut < 0 ? { events, heldBack: false } : { events: events.slice(0, cut), heldBack: true };
}

/** The events of the latest season in the list, which is what the satisfaction chart draws. */
export function latestSeasonEvents(events: readonly FanEvent[]): readonly FanEvent[] {
  if (events.length === 0) {
    return events;
  }

  const latest = Math.max(...events.map((event) => event.seasonNumber));

  return events.filter((event) => event.seasonNumber === latest);
}

/** A chart with the labels and series the shared line chart takes. */
export interface FanPlot {
  readonly labels: readonly string[];
  readonly series: readonly LineChartSeries[];
}

function dayLabel(instant: string): string {
  return new Intl.DateTimeFormat(preferredLocale(), {
    day: 'numeric',
    month: 'short',
    timeZone: preferredTimeZone(),
  }).format(new Date(instant));
}

/** The date of an event as the charts and the table print it. */
export function eventDate(instant: string): string {
  return dayLabel(instant);
}

/** The label of a point: the date, and the season when more than one is on the chart. */
function pointLabels(events: readonly FanEvent[]): readonly string[] {
  const seasons = new Set(events.map((event) => event.seasonNumber));

  return events.map((event) =>
    seasons.size > 1
      ? `S${event.seasonNumber} ${dayLabel(event.occurredAt)}`
      : dayLabel(event.occurredAt),
  );
}

/**
 * The satisfaction chart: the mood after each event of the season, with the edges of the Calm band as dashed lines.
 *
 * @param events The season's events, oldest first.
 */
export function satisfactionPlot(events: readonly FanEvent[]): FanPlot {
  return {
    labels: pointLabels(events),
    series: [
      {
        label: 'Satisfaction',
        data: events.map((event) => moodPoints(event.moodAfterBp)),
        color: BLUE,
        dash: [],
        pointStyle: 'circle',
        notes: events.map(
          (event) => `${moodWordFor(event.moodAfterBp).phrase} · ${eventNote(event)}`,
        ),
      },
      {
        label: `Calm from ${CALM_FROM}`,
        data: events.map(() => CALM_FROM),
        color: OLIVE,
        dash: [6, 3],
        pointStyle: 'triangle',
        pointRadius: 0,
      },
      {
        label: `Content from ${CALM_TO}`,
        data: events.map(() => CALM_TO),
        color: GREEN,
        dash: [2, 3],
        pointStyle: 'rect',
        pointRadius: 0,
      },
    ],
  };
}

/** Whether the soft cap is far enough from the fans to be worth drawing. */
export function showsCap(events: readonly FanEvent[], softCapFans: number): boolean {
  const most = events.reduce((top, event) => Math.max(top, event.fansAfter), 0);

  return most >= softCapFans * CAP_VISIBLE_SHARE;
}

/**
 * The fan count chart: the fans after each event, with the tier's floor and soft cap as dashed lines and a marker
 * where a new manager took over.
 *
 * The soft cap is drawn only once the fans are a third of the way to it. A tier-one club opens at a fifth of its cap,
 * and a line that far above would squash the fans into the bottom of the chart for seasons.
 *
 * @param events The history, oldest first.
 * @param floorFans The floor of the tier the club plays in now.
 * @param softCapFans The soft cap of that tier.
 */
export function fanCountPlot(
  events: readonly FanEvent[],
  floorFans: number,
  softCapFans: number,
): FanPlot {
  const series: LineChartSeries[] = [
    {
      label: 'Fans',
      data: events.map((event) => event.fansAfter),
      color: BLUE,
      dash: [],
      pointStyle: 'circle',
      pointRadius: 2,
      notes: events.map((event) => eventNote(event)),
    },
    {
      label: 'Floor',
      data: events.map(() => floorFans),
      color: ORANGE,
      dash: [6, 3],
      pointStyle: 'triangle',
      pointRadius: 0,
    },
  ];

  if (showsCap(events, softCapFans)) {
    series.push({
      label: 'Soft cap',
      data: events.map(() => softCapFans),
      color: GREEN,
      dash: [2, 3],
      pointStyle: 'rect',
      pointRadius: 0,
    });
  }

  if (events.some((event) => event.kind === 'takeover')) {
    series.push({
      label: 'New manager',
      data: events.map((event) => (event.kind === 'takeover' ? event.fansAfter : null)),
      color: VIOLET,
      dash: [],
      // A filled shape: the stroke-only styles (`star`, `cross`) are drawn in the border colour, which is the panel's.
      pointStyle: 'rectRot',
      pointRadius: 7,
    });
  }

  return { labels: pointLabels(events), series };
}

/** What a screen reader hears for the satisfaction chart. */
export function satisfactionSummary(events: readonly FanEvent[]): string {
  if (events.length === 0) {
    return 'Fan satisfaction this season. No points yet.';
  }

  const first = events[0];
  const last = events[events.length - 1];

  return (
    `Fan satisfaction this season, from ${moodPoints(first.moodAfterBp).toFixed(0)} (${moodWordFor(first.moodAfterBp).phrase}) ` +
    `to ${moodPoints(last.moodAfterBp).toFixed(0)} (${moodWordFor(last.moodAfterBp).phrase}) over ${events.length} points. ` +
    'The table below lists every point.'
  );
}

/** What a screen reader hears for the fan count chart. */
export function fanCountSummary(events: readonly FanEvent[]): string {
  if (events.length === 0) {
    return 'Fan base size. No points yet.';
  }

  const first = events[0];
  const last = events[events.length - 1];

  return (
    `Fan base size, from ${groupedNumber(first.fansAfter)} to ${groupedNumber(last.fansAfter)} fans over ${events.length} points. ` +
    'The table below lists every point.'
  );
}

/** A place in the table as the fans say it: 1st, 2nd, 3rd, 11th, 22nd. */
export function ordinalPlace(place: number): string {
  const lastTwo = place % 100;
  const suffix =
    lastTwo >= 11 && lastTwo <= 13
      ? 'th'
      : (({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[place % 10] ?? 'th');

  return `${place}${suffix}`;
}
