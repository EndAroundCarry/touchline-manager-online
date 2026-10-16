import { formatHour, minuteOfDay, placeEvents, shiftDay } from './calendar-presentation';
import { CalendarEvent } from './calendar.models';

function at(id: string, time: string): CalendarEvent {
  return {
    id,
    kind: 'match',
    at: `2026-10-17T${time}:00Z`,
    state: 'upcoming',
    title: id,
    detail: null,
    link: null,
    amountMinor: null,
    resultMatchId: null,
  };
}

function place(
  events: readonly CalendarEvent[],
): Record<string, { column: number; columns: number }> {
  return Object.fromEntries(
    placeEvents(events, 'UTC').map((placed) => [
      placed.event.id,
      { column: placed.column, columns: placed.columns },
    ]),
  );
}

describe('the hourly view layout', () => {
  it('reads the minute of the day in the zone given', () => {
    expect(minuteOfDay('2026-10-17T00:00:00Z', 'UTC')).toBe(0);
    expect(minuteOfDay('2026-10-17T15:30:00Z', 'UTC')).toBe(15 * 60 + 30);
    expect(minuteOfDay('2026-10-17T23:59:00Z', 'UTC')).toBe(1439);
    expect(minuteOfDay('2026-10-17T23:30:00Z', 'Asia/Tokyo')).toBe(8 * 60 + 30);
  });

  it('gives an event that overlaps nothing the whole width', () => {
    expect(place([at('a', '09:00'), at('b', '11:00')])).toEqual({
      a: { column: 0, columns: 1 },
      b: { column: 0, columns: 1 },
    });
  });

  it('treats an event that starts when the last one ends as no overlap', () => {
    // Each block is an hour long, so 10:00 starts exactly when 09:00 ends.
    expect(place([at('a', '09:00'), at('b', '10:00')])).toEqual({
      a: { column: 0, columns: 1 },
      b: { column: 0, columns: 1 },
    });
  });

  it('splits the width between events that share an hour', () => {
    expect(place([at('a', '14:00'), at('b', '14:30')])).toEqual({
      a: { column: 0, columns: 2 },
      b: { column: 1, columns: 2 },
    });
    expect(place([at('a', '14:00'), at('b', '14:00'), at('c', '14:10')])).toEqual({
      a: { column: 0, columns: 3 },
      b: { column: 1, columns: 3 },
      c: { column: 2, columns: 3 },
    });
  });

  it('reuses a column that has become free within a cluster', () => {
    // a and b overlap; c overlaps b but not a, so it slots under a in column 0; the cluster stays two wide.
    expect(place([at('a', '09:00'), at('b', '09:30'), at('c', '10:00')])).toEqual({
      a: { column: 0, columns: 2 },
      b: { column: 1, columns: 2 },
      c: { column: 0, columns: 2 },
    });
  });

  it('keeps separate clusters independent', () => {
    expect(place([at('a', '09:00'), at('b', '09:15'), at('c', '15:00')])).toEqual({
      a: { column: 0, columns: 2 },
      b: { column: 1, columns: 2 },
      c: { column: 0, columns: 1 },
    });
  });

  it('never runs a block past the end of the day', () => {
    const [last] = placeEvents([at('late', '23:30')], 'UTC');

    expect(last.startMinute).toBe(23 * 60 + 30);
    expect(last.endMinute).toBe(1440);
  });

  it('moves a day key across month and year ends', () => {
    expect(shiftDay('2026-10-31', 1)).toBe('2026-11-01');
    expect(shiftDay('2026-12-31', 1)).toBe('2027-01-01');
    expect(shiftDay('2026-03-01', -1)).toBe('2026-02-28');
    expect(shiftDay('2026-10-17', 0)).toBe('2026-10-17');
  });

  it('labels an hour row by locale', () => {
    expect(formatHour(9, 'en-GB')).toMatch(/^0?9:00$/);
    expect(formatHour(15, 'en-GB')).toBe('15:00');
    expect(formatHour(15, 'en-US')).toMatch(/3:00\s?PM/);
  });
});
