import {
  ALL_REGIMES,
  DEFAULT_CHART_FILTER,
  buildChart,
  chartAriaLabel,
  changesLabel,
  dayLabel,
  daysInRange,
  regimeStyle,
  regimesIn,
} from './training-chart';
import type { PlayerTrainingDay, PlayerTrainingSummary } from './training.models';

/**
 * The chart's data shaping.
 *
 * What matters: the range counts back from the latest recorded day; a series breaks where its regime does;
 * cumulative is per regime and restarts at zero; a regime keeps its look whatever else is plotted; and the
 * rows the data table lists are exactly the points the chart draws.
 */

function day(
  iso: string,
  programme: string,
  growth: number,
  changes: { attribute: string; delta: number }[] = [],
): PlayerTrainingDay {
  return {
    day: iso,
    programme,
    intensity: 'normal',
    growth,
    pointsGained: changes.filter((change) => change.delta > 0).length,
    pointsLost: changes.filter((change) => change.delta < 0).length,
    attributeChanges: changes,
  };
}

// Five days: two on the winger programme, then three on recovery, with a gap in the dates (the 4th).
const days: readonly PlayerTrainingDay[] = [
  day('2026-10-01', 'winger', 0.4, [{ attribute: 'pace', delta: 1 }]),
  day('2026-10-02', 'winger', 0.3),
  day('2026-10-03', 'recovery', 0),
  day('2026-10-05', 'recovery', -0.1, [{ attribute: 'stamina', delta: -1 }]),
  day('2026-10-06', 'winger', 0.5),
];

const summary: readonly PlayerTrainingSummary[] = [
  { programme: 'winger', label: 'Winger', days: 3, pointsGained: 1, pointsLost: 0, net: 1 },
  { programme: 'recovery', label: 'Recovery', days: 2, pointsGained: 0, pointsLost: 1, net: -1 },
];

describe('training chart', () => {
  describe('regimesIn', () => {
    it('lists the programmes in the order the player first trained them', () => {
      expect(regimesIn(days)).toEqual(['winger', 'recovery']);
      expect(regimesIn([])).toEqual([]);
    });
  });

  describe('daysInRange', () => {
    it('keeps everything for "all" and for an empty history', () => {
      expect(daysInRange(days, 'all')).toHaveLength(5);
      expect(daysInRange([], '30')).toEqual([]);
    });

    it('counts calendar days back from the latest recorded day, inclusive', () => {
      // 6 Oct is the latest, so a 30-day window reaches back to 8 Sep and holds everything.
      expect(daysInRange(days, '30')).toHaveLength(5);

      // 15 Aug is 52 days before the latest day: inside 90, outside 30.
      const long = [day('2026-08-15', 'winger', 0.1), ...days];

      expect(daysInRange(long, '90')).toHaveLength(6);
      expect(daysInRange(long, '30').map((entry) => entry.day)).not.toContain('2026-08-15');
    });

    it('includes the first day of the window and excludes the one before', () => {
      const edge = [day('2026-09-06', 'winger', 0.1), day('2026-09-07', 'winger', 0.1), ...days];

      // Latest 6 Oct; 30 days inclusive starts on 7 Sep.
      expect(daysInRange(edge, '30').map((entry) => entry.day)).toEqual([
        '2026-09-07',
        ...days.map((entry) => entry.day),
      ]);
    });
  });

  describe('changesLabel', () => {
    it('words each change with its sign and the attribute name', () => {
      expect(
        changesLabel([
          { attribute: 'finishing', delta: 1 },
          { attribute: 'firstTouch', delta: -2 },
        ]),
      ).toBe('+1 Finishing, −2 First touch');
      expect(changesLabel([])).toBe('');
    });
  });

  describe('dayLabel', () => {
    it('shortens a date in the viewer locale, and keeps a value that is not a date', () => {
      expect(dayLabel('2026-10-06', 'en-GB')).toBe('6 Oct');
      expect(dayLabel('later', 'en-GB')).toBe('later');
    });
  });

  describe('regimeStyle', () => {
    const codes = [
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

    it('gives every programme a distinct colour, dash and point style together', () => {
      const looks = codes.map((code) => {
        const style = regimeStyle(code);

        return `${style.color}|${style.dash.join(',')}|${style.pointStyle}`;
      });

      expect(new Set(looks).size).toBe(codes.length);
    });

    it('keeps a programme’s look the same whatever else is plotted', () => {
      expect(regimeStyle('winger')).toEqual(regimeStyle('winger'));
    });

    it('gives a code it does not know a stable look rather than none', () => {
      expect(regimeStyle('sweeper')).toEqual(regimeStyle('sweeper'));
      expect(regimeStyle('sweeper').color).toMatch(/^#[0-9A-F]{6}$/);
    });
  });

  describe('buildChart', () => {
    it('draws one series per regime, null where the player trained something else', () => {
      const chart = buildChart(days, summary, DEFAULT_CHART_FILTER, 'en-GB');

      expect(chart.days).toEqual([
        '2026-10-01',
        '2026-10-02',
        '2026-10-03',
        '2026-10-05',
        '2026-10-06',
      ]);
      expect(chart.labels[0]).toBe('1 Oct');
      expect(chart.series.map((line) => line.label)).toEqual(['Winger', 'Recovery']);
      expect(chart.series[0].data).toEqual([0.4, 0.3, null, null, 0.5]);
      expect(chart.series[1].data).toEqual([null, null, 0, -0.1, null]);
    });

    it('notes the attribute changes on the days they happened', () => {
      const [winger, recovery] = buildChart(days, summary, DEFAULT_CHART_FILTER, 'en-GB').series;

      expect(winger.notes).toEqual(['+1 Pace', null, null, null, null]);
      expect(recovery.notes[3]).toBe('−1 Stamina');
    });

    it('accumulates per regime in cumulative mode, restarting at zero for each', () => {
      const chart = buildChart(
        days,
        summary,
        { ...DEFAULT_CHART_FILTER, mode: 'cumulative' },
        'en-GB',
      );

      // 0.4, 0.4+0.3, and the later winger day continues the winger total.
      expect(chart.series[0].data).toEqual([0.4, 0.7, null, null, 1.2]);
      expect(chart.series[1].data).toEqual([null, null, 0, -0.1, null]);
    });

    it('rounds a running total to three decimals, so floating point never shows', () => {
      const small = [day('2026-10-01', 'winger', 0.1), day('2026-10-02', 'winger', 0.2)];
      const chart = buildChart(
        small,
        summary,
        { ...DEFAULT_CHART_FILTER, mode: 'cumulative' },
        'en-GB',
      );

      expect(chart.series[0].data).toEqual([0.1, 0.3]);
    });

    it('plots a single regime on only its own days', () => {
      const chart = buildChart(
        days,
        summary,
        { ...DEFAULT_CHART_FILTER, regime: 'recovery' },
        'en-GB',
      );

      expect(chart.series).toHaveLength(1);
      expect(chart.days).toEqual(['2026-10-03', '2026-10-05']);
      expect(chart.series[0].data).toEqual([0, -0.1]);
    });

    it('applies the range before the regime, so a regime outside the range is empty', () => {
      const long = [day('2026-06-01', 'forward', 0.2), ...days];
      const chart = buildChart(
        long,
        [
          ...summary,
          {
            programme: 'forward',
            label: 'Forward',
            days: 1,
            pointsGained: 0,
            pointsLost: 0,
            net: 0,
          },
        ],
        { regime: 'forward', mode: 'daily', range: '30' },
        'en-GB',
      );

      expect(chart.series).toEqual([]);
      expect(chart.days).toEqual([]);
    });

    it('lists as table rows exactly the points the chart draws, in day order', () => {
      const chart = buildChart(days, summary, DEFAULT_CHART_FILTER, 'en-GB');
      const plotted = chart.series.reduce(
        (count, line) => count + line.data.filter((value) => value !== null).length,
        0,
      );

      expect(chart.rows).toHaveLength(plotted);
      expect(chart.rows.map((row) => row.day)).toEqual(
        [...chart.rows.map((row) => row.day)].sort(),
      );
      expect(chart.rows[0]).toEqual({
        day: '2026-10-01',
        programme: 'winger',
        label: 'Winger',
        value: 0.4,
        changes: '+1 Pace',
      });
    });

    it('falls back to the code when the summary has no label for a regime', () => {
      const chart = buildChart(days, [], DEFAULT_CHART_FILTER, 'en-GB');

      expect(chart.series.map((line) => line.label)).toEqual(['winger', 'recovery']);
    });

    it('is empty, not an error, before there is any history', () => {
      const chart = buildChart(
        [],
        [],
        { regime: ALL_REGIMES, mode: 'daily', range: 'all' },
        'en-GB',
      );

      expect(chart).toEqual({ labels: [], days: [], series: [], rows: [] });
    });
  });

  describe('chartAriaLabel', () => {
    it('says what is plotted, how many regimes and days, and where the table is', () => {
      const chart = buildChart(days, summary, DEFAULT_CHART_FILTER, 'en-GB');

      expect(chartAriaLabel(chart, 'daily')).toBe(
        'Daily attribute growth in points, 2 regimes, 5 days from 1 Oct to 6 Oct. The table below lists every point.',
      );
      expect(chartAriaLabel(chart, 'cumulative')).toContain('Cumulative attribute growth');
    });

    it('says so when there is nothing to plot', () => {
      const empty = buildChart([], [], DEFAULT_CHART_FILTER, 'en-GB');

      expect(chartAriaLabel(empty, 'daily')).toContain('no days recorded');
    });
  });
});
