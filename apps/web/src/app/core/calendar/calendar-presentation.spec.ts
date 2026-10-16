import {
  countLabel,
  dayKey,
  firstDayOfWeek,
  formatDay,
  formatMonth,
  formatTime,
  gridRange,
  groupByDay,
  kindStyle,
  kindsOf,
  monthGrid,
  monthOf,
  shiftMonth,
  weekdayNames,
} from './calendar-presentation';
import { CalendarEvent } from './calendar.models';

function event(id: string, kind: string, at: string): CalendarEvent {
  return {
    id,
    kind,
    at,
    state: 'upcoming',
    title: id,
    detail: null,
    link: null,
    amountMinor: null,
    resultMatchId: null,
  };
}

describe('calendar presentation', () => {
  describe('kindStyle', () => {
    it('dresses the kinds it knows', () => {
      expect(kindStyle('match').label).toBe('Match');
      expect(kindStyle('finance').icon).toBe('pi pi-wallet');
      expect(kindStyle('injury').label).toBe('Injury');
    });

    it('draws a kind it has never heard of, named after its code', () => {
      const style = kindStyle('construction_finished');

      expect(style.label).toBe('Construction finished');
      expect(style.icon).toBe('pi pi-circle');
      expect(style.tone.length).toBeGreaterThan(0);
    });

    it('never fails on an empty code', () => {
      expect(kindStyle('').label).toBe('Event');
    });
  });

  describe('dayKey', () => {
    it('files an instant under the day it falls on in the zone given', () => {
      const late = '2026-10-17T23:30:00Z';

      expect(dayKey(late, 'UTC')).toBe('2026-10-17');
      expect(dayKey(late, 'Europe/London')).toBe('2026-10-18');
      expect(dayKey(late, 'America/Los_Angeles')).toBe('2026-10-17');
      expect(dayKey(late, 'Pacific/Auckland')).toBe('2026-10-18');
    });
  });

  describe('monthGrid', () => {
    it('starts the week on the day asked for and ends on a whole week', () => {
      const monday = monthGrid({ year: 2026, month: 10 }, 1);
      const sunday = monthGrid({ year: 2026, month: 10 }, 7);

      // 1 October 2026 is a Thursday.
      expect(monday).toHaveLength(35);
      expect(monday[0].key).toBe('2026-09-28');
      expect(monday[0].inMonth).toBe(false);
      expect(monday[3].key).toBe('2026-10-01');
      expect(monday[3].inMonth).toBe(true);
      expect(monday[34].key).toBe('2026-11-01');

      expect(sunday[0].key).toBe('2026-09-27');
      expect(sunday).toHaveLength(35);
    });

    it('takes only the rows a month needs', () => {
      // February 2027 starts on a Monday and has 28 days: four rows.
      expect(monthGrid({ year: 2027, month: 2 }, 1)).toHaveLength(28);
      // August 2026 starts on a Saturday and has 31 days: six rows.
      expect(monthGrid({ year: 2026, month: 8 }, 1)).toHaveLength(42);
    });

    it('marks exactly the days of the month as in the month', () => {
      const days = monthGrid({ year: 2026, month: 10 }, 1).filter((day) => day.inMonth);

      expect(days).toHaveLength(31);
      expect(days[0].dayOfMonth).toBe(1);
      expect(days[30].dayOfMonth).toBe(31);
    });
  });

  describe('gridRange', () => {
    it('pads the grid by the widest a zone can move a day, and stays within what the server serves', () => {
      const days = monthGrid({ year: 2026, month: 8 }, 1);
      const range = gridRange(days);
      const first = Date.UTC(2026, 6, 27);
      const spanDays = (Date.parse(range.to) - Date.parse(range.from)) / 86_400_000;

      expect(days[0].key).toBe('2026-07-27');
      expect(Date.parse(range.from)).toBe(first - 14 * 3_600_000);
      expect(spanDays).toBeLessThanOrEqual(70);
    });

    it('covers the grid whichever zone its days are read in', () => {
      const days = monthGrid({ year: 2026, month: 10 }, 1);
      const range = gridRange(days);

      // The earliest instant the first day can start (UTC+14) and the latest the last day can end (UTC-12).
      const earliestStart = Date.parse(`${days[0].key}T00:00:00Z`) - 14 * 3_600_000;
      const latestEnd = Date.parse(`${days[days.length - 1].key}T23:59:59Z`) + 12 * 3_600_000;

      expect(Date.parse(range.from)).toBeLessThanOrEqual(earliestStart);
      expect(Date.parse(range.to)).toBeGreaterThanOrEqual(latestEnd);
    });
  });

  describe('groupByDay', () => {
    it('files each event under its day in time order', () => {
      const days = groupByDay(
        [
          event('c', 'match', '2026-10-17T19:00:00Z'),
          event('a', 'finance', '2026-10-17T09:00:00Z'),
          event('b', 'injury', '2026-10-18T09:00:00Z'),
        ],
        'UTC',
      );

      expect([...days.keys()]).toEqual(['2026-10-17', '2026-10-18']);
      expect(days.get('2026-10-17')?.map((item) => item.id)).toEqual(['a', 'c']);
    });

    it('uses the zone given to decide the day', () => {
      const days = groupByDay([event('late', 'match', '2026-10-17T23:30:00Z')], 'Europe/London');

      expect(days.has('2026-10-18')).toBe(true);
      expect(days.has('2026-10-17')).toBe(false);
    });
  });

  it('lists the kinds present once each, in order of appearance', () => {
    expect(
      kindsOf([
        event('1', 'match', '2026-10-17T09:00:00Z'),
        event('2', 'injury', '2026-10-17T10:00:00Z'),
        event('3', 'match', '2026-10-17T11:00:00Z'),
      ]),
    ).toEqual(['match', 'injury']);
  });

  it('moves months across the year end', () => {
    expect(shiftMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth({ year: 2026, month: 10 }, 0)).toEqual({ year: 2026, month: 10 });
    expect(shiftMonth({ year: 2026, month: 10 }, -22)).toEqual({ year: 2024, month: 12 });
  });

  it('reads the month an instant falls in, in the zone given', () => {
    expect(monthOf('2026-10-31T23:30:00Z', 'UTC')).toEqual({ year: 2026, month: 10 });
    expect(monthOf('2026-10-31T23:30:00Z', 'Asia/Tokyo')).toEqual({ year: 2026, month: 11 });
  });

  it('knows which weekday a locale starts its week on', () => {
    expect(firstDayOfWeek('en-US')).toBe(7);
    expect(firstDayOfWeek('en-GB')).toBe(1);
    expect(firstDayOfWeek('not a locale')).toBe(1);
  });

  it('names the weekdays from the first day of the week', () => {
    expect(weekdayNames(1, 'en-GB')).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']);
    expect(weekdayNames(7, 'en-GB')[0]).toBe('Sun');
    expect(weekdayNames(1, 'en-GB', 'narrow')).toHaveLength(7);
  });

  it('formats months, days and times', () => {
    expect(formatMonth({ year: 2026, month: 10 }, 'en-GB')).toBe('October 2026');
    // Some runtimes put a comma after the weekday and some do not.
    expect(formatDay('2026-10-17', 'en-GB').replace(',', '')).toBe('Saturday 17 October 2026');
    expect(formatTime('2026-10-17T19:00:00Z', 'en-GB', 'UTC')).toBe('19:00');
    expect(formatTime('2026-10-17T19:00:00Z', 'en-GB', 'Asia/Tokyo')).toBe('04:00');
  });

  it('counts events for a screen reader', () => {
    expect(countLabel(1)).toBe('1 event');
    expect(countLabel(3)).toBe('3 events');
  });
});
