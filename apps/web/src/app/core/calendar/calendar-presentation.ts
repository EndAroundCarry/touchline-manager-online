import { preferredLocale, preferredTimeZone } from '../world/presentation';
import { CalendarEvent } from './calendar.models';

/**
 * How a kind of event looks.
 *
 * The kind is never the only signal: the icon and the label carry it, and the tone is decoration on top.
 */
export interface CalendarKindStyle {
  /** The name of the kind, for the legend and a screen reader. */
  readonly label: string;

  /** The PrimeIcons class of the marker. */
  readonly icon: string;

  /** Tailwind classes that tint the marker. Status shades only, so the light theme retunes them. */
  readonly tone: string;
}

/** The marker an unknown kind gets, so a kind the server adds before this build learns it still shows. */
const GENERIC_TONE = 'border-line-strong bg-raised text-ink-2';

/**
 * The kinds this build dresses up. Adding a row here only improves how a kind looks: a kind with no row is
 * still drawn, with its own code as the label and a neutral marker, so a new event source never waits on this
 * table.
 */
export const CALENDAR_KINDS: Readonly<Record<string, CalendarKindStyle>> = {
  match: {
    label: 'Match',
    icon: 'pi pi-flag',
    tone: 'border-sky-500/40 bg-sky-500/15 text-sky-300',
  },
  finance: {
    label: 'Finances',
    icon: 'pi pi-wallet',
    tone: 'border-emerald-500/40 bg-emerald-500/15 text-emerald-300',
  },
  injury: {
    label: 'Injury',
    icon: 'pi pi-exclamation-triangle',
    tone: 'border-red-500/40 bg-red-500/15 text-red-300',
  },
  recovery: {
    label: 'Recovery',
    icon: 'pi pi-heart',
    tone: 'border-amber-500/40 bg-amber-500/15 text-amber-300',
  },
};

/** Looks a kind up, falling back to a neutral marker named after its code. */
export function kindStyle(kind: string): CalendarKindStyle {
  return (
    CALENDAR_KINDS[kind] ?? {
      label: humanise(kind),
      icon: 'pi pi-circle',
      tone: GENERIC_TONE,
    }
  );
}

/** Turns a kind code such as `construction_finished` into `Construction finished`. */
function humanise(code: string): string {
  const words = code.replace(/[_\-.]+/g, ' ').trim();

  return words.length === 0 ? 'Event' : words.charAt(0).toUpperCase() + words.slice(1);
}

/** A month, by its year and its 1-based number. */
export interface MonthRef {
  readonly year: number;
  readonly month: number;
}

/** One cell of the month grid. */
export interface CalendarDay {
  /** The day's key, `YYYY-MM-DD`, which is also how events are filed. */
  readonly key: string;

  /** The day of the month, 1–31. */
  readonly dayOfMonth: number;

  /** Whether the day belongs to the month being shown rather than to a neighbour that fills the week. */
  readonly inMonth: boolean;
}

function pad(value: number, width = 2): string {
  return String(value).padStart(width, '0');
}

function keyOf(year: number, month: number, day: number): string {
  return `${pad(year, 4)}-${pad(month)}-${pad(day)}`;
}

function keyOfUtc(date: Date): string {
  return keyOf(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
}

/**
 * The day an instant falls on in a time zone, as `YYYY-MM-DD`.
 *
 * The manager's own zone decides the day, not the server's and not the device's, so a late kickoff does not
 * move to the next day for someone whose profile says otherwise (`CAL-4`).
 */
export function dayKey(instant: string | Date, timeZone = preferredTimeZone()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone,
  }).formatToParts(typeof instant === 'string' ? new Date(instant) : instant);

  const part = (type: string): string =>
    parts.find((candidate) => candidate.type === type)?.value ?? '';

  return `${part('year')}-${part('month')}-${part('day')}`;
}

/** The month a day key belongs to. */
export function monthOfKey(key: string): MonthRef {
  const [year, month] = key.split('-').map(Number);

  return { year, month };
}

/** The month an instant falls in, in a time zone. */
export function monthOf(instant: string, timeZone = preferredTimeZone()): MonthRef {
  return monthOfKey(dayKey(instant, timeZone));
}

/** The month `delta` months from another, across year ends. */
export function shiftMonth(month: MonthRef, delta: number): MonthRef {
  const index = month.year * 12 + (month.month - 1) + delta;

  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

/**
 * The weekday a week starts on in a locale, as ISO 1 (Monday) to 7 (Sunday). Falls back to Monday where the
 * runtime cannot say.
 */
export function firstDayOfWeek(locale = preferredLocale()): number {
  try {
    const intlLocale = new Intl.Locale(locale) as Intl.Locale & {
      getWeekInfo?: () => { firstDay: number };
      weekInfo?: { firstDay: number };
    };
    const info = intlLocale.getWeekInfo?.() ?? intlLocale.weekInfo;

    return info?.firstDay ?? 1;
  } catch {
    return 1;
  }
}

/**
 * The cells of a month, in whole weeks.
 *
 * Starts on the week's first day on or before the 1st and runs to the end of the week holding the last day, so
 * a month takes the rows it needs, four to six, and every row is a full week.
 *
 * @param month The month to draw.
 * @param weekStartsOn The ISO weekday a week starts on, 1 (Monday) to 7 (Sunday).
 */
export function monthGrid(month: MonthRef, weekStartsOn = 1): readonly CalendarDay[] {
  const first = new Date(Date.UTC(month.year, month.month - 1, 1));
  const daysInMonth = new Date(Date.UTC(month.year, month.month, 0)).getUTCDate();

  // getUTCDay is 0 (Sunday) to 6; the ISO weekday is 1 (Monday) to 7.
  const isoWeekday = first.getUTCDay() === 0 ? 7 : first.getUTCDay();
  const lead = (isoWeekday - weekStartsOn + 7) % 7;
  const rows = Math.ceil((lead + daysInMonth) / 7);
  const days: CalendarDay[] = [];

  for (let index = 0; index < rows * 7; index += 1) {
    const date = new Date(Date.UTC(month.year, month.month - 1, 1 - lead + index));

    days.push({
      key: keyOfUtc(date),
      dayOfMonth: date.getUTCDate(),
      inMonth: date.getUTCMonth() === month.month - 1,
    });
  }

  return days;
}

/**
 * The instants to ask the server for so the grid is covered whatever zone the days are drawn in.
 *
 * A day starts at a different instant in each zone, anywhere from 12 hours before to 14 hours after the same
 * wall-clock midnight read as UTC, so the window is padded by exactly that. Events the padding brings in but
 * the grid does not show are simply not filed under any cell.
 */
export function gridRange(days: readonly CalendarDay[]): { from: string; to: string } {
  const [startYear, startMonth, startDay] = days[0].key.split('-').map(Number);
  const [endYear, endMonth, endDay] = days[days.length - 1].key.split('-').map(Number);
  const hour = 3_600_000;

  const from = Date.UTC(startYear, startMonth - 1, startDay) - 14 * hour;
  const to = Date.UTC(endYear, endMonth - 1, endDay + 1) + 12 * hour;

  return { from: new Date(from).toISOString(), to: new Date(to).toISOString() };
}

/** Files events under the day they fall on in a time zone, each day's events in time order. */
export function groupByDay(
  events: readonly CalendarEvent[],
  timeZone = preferredTimeZone(),
): ReadonlyMap<string, readonly CalendarEvent[]> {
  const days = new Map<string, CalendarEvent[]>();

  for (const event of [...events].sort((a, b) => Date.parse(a.at) - Date.parse(b.at))) {
    const key = dayKey(event.at, timeZone);
    const day = days.get(key);

    if (day === undefined) {
      days.set(key, [event]);
    } else {
      day.push(event);
    }
  }

  return days;
}

/** The kinds present in a set of events, in the order they first appear. */
export function kindsOf(events: readonly CalendarEvent[]): readonly string[] {
  return [...new Set(events.map((event) => event.kind))];
}

function dayKeyAsDate(key: string): Date {
  const [year, month, day] = key.split('-').map(Number);

  // Noon, so no zone's offset can carry the formatted date to a neighbouring day.
  return new Date(Date.UTC(year, month - 1, day, 12));
}

/** `October 2026`. */
export function formatMonth(month: MonthRef, locale = preferredLocale()): string {
  return new Intl.DateTimeFormat(locale, {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(month.year, month.month - 1, 15)));
}

/** `Saturday 17 October 2026`, for the heading of a selected day. */
export function formatDay(key: string, locale = preferredLocale()): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(dayKeyAsDate(key));
}

/** The short weekday names for the grid's header, starting on the week's first day. */
export function weekdayNames(
  weekStartsOn = 1,
  locale = preferredLocale(),
  style: 'short' | 'narrow' = 'short',
): readonly string[] {
  // 2024-01-01 was a Monday, so day `n` of that January is ISO weekday `n`.
  return Array.from({ length: 7 }, (_, index) => {
    const isoWeekday = ((weekStartsOn - 1 + index) % 7) + 1;

    return new Intl.DateTimeFormat(locale, { weekday: style, timeZone: 'UTC' }).format(
      new Date(Date.UTC(2024, 0, isoWeekday, 12)),
    );
  });
}

/** The time of day of an instant in the manager's zone, `19:00`. */
export function formatTime(
  instant: string,
  locale = preferredLocale(),
  timeZone = preferredTimeZone(),
): string {
  return new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', timeZone }).format(
    new Date(instant),
  );
}

/** A day's events as a sentence for a screen reader, `2 events`. */
export function countLabel(count: number): string {
  return count === 1 ? '1 event' : `${count} events`;
}

/** The day `delta` days from a day key, across month and year ends. */
export function shiftDay(key: string, delta: number): string {
  const [year, month, day] = key.split('-').map(Number);

  return keyOfUtc(new Date(Date.UTC(year, month - 1, day + delta)));
}

/** Minutes in a day, which is where the hourly view ends. */
export const MINUTES_PER_DAY = 1440;

/**
 * How long an event is drawn for in the hourly view.
 *
 * Events are instants, not spans, so each is drawn as one hour from its start. That is also what decides
 * which events share the width: two that start within the same hour overlap.
 */
export const EVENT_BLOCK_MINUTES = 60;

/** The minute of the day an instant falls on in a time zone, 0 to 1439. */
export function minuteOfDay(instant: string | Date, timeZone = preferredTimeZone()): number {
  const parts = new Intl.DateTimeFormat('en-GB', {
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone,
  }).formatToParts(typeof instant === 'string' ? new Date(instant) : instant);

  const part = (type: string): number =>
    Number(parts.find((candidate) => candidate.type === type)?.value ?? 0);

  return (part('hour') % 24) * 60 + part('minute');
}

/** An event placed on the hourly view. */
export interface PlacedEvent {
  readonly event: CalendarEvent;

  /** Where the block starts, in minutes from midnight. */
  readonly startMinute: number;

  /** Where the block ends, in minutes from midnight, never past the end of the day. */
  readonly endMinute: number;

  /** Which of the cluster's columns the block sits in, from 0. */
  readonly column: number;

  /** How many columns its cluster has, so the block is `1 / columns` of the width. */
  readonly columns: number;
}

/**
 * Lays one day's events out on the hourly view so that overlapping ones share the width.
 *
 * Events are grouped into clusters of events that overlap one another, directly or through a third; each
 * cluster is split into as many equal columns as its busiest moment needs, and each event takes the first
 * column that is free when it starts. Events that overlap nothing take the whole width.
 */
export function placeEvents(
  events: readonly CalendarEvent[],
  timeZone = preferredTimeZone(),
): readonly PlacedEvent[] {
  const timed = events
    .map((event) => {
      const startMinute = minuteOfDay(event.at, timeZone);

      return {
        event,
        startMinute,
        endMinute: Math.min(startMinute + EVENT_BLOCK_MINUTES, MINUTES_PER_DAY),
      };
    })
    .sort(
      (a, b) =>
        a.startMinute - b.startMinute ||
        b.endMinute - a.endMinute ||
        a.event.id.localeCompare(b.event.id),
    );

  const placed: PlacedEvent[] = [];
  let cluster: { event: CalendarEvent; startMinute: number; endMinute: number; column: number }[] =
    [];
  let columnEnds: number[] = [];
  let clusterEnd = -1;

  const closeCluster = (): void => {
    for (const item of cluster) {
      placed.push({ ...item, columns: columnEnds.length });
    }

    cluster = [];
    columnEnds = [];
  };

  for (const item of timed) {
    if (item.startMinute >= clusterEnd) {
      closeCluster();
    }

    let column = columnEnds.findIndex((end) => end <= item.startMinute);

    if (column === -1) {
      column = columnEnds.length;
      columnEnds.push(item.endMinute);
    } else {
      columnEnds[column] = item.endMinute;
    }

    cluster.push({ ...item, column });
    clusterEnd = cluster.length === 1 ? item.endMinute : Math.max(clusterEnd, item.endMinute);
  }

  closeCluster();

  return placed;
}

/** The label of an hour row, `09:00` or `9:00 AM` by locale. */
export function formatHour(hour: number, locale = preferredLocale()): string {
  return new Intl.DateTimeFormat(locale, {
    hour: 'numeric',
    minute: '2-digit',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(2024, 0, 1, hour)));
}
