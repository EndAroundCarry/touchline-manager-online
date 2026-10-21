import { configurePresentation, resetPresentation } from '../world/presentation';
import {
  chancesLabel,
  eventNote,
  expectationTone,
  fanCountPlot,
  gapLabel,
  holdBackHistory,
  latestSeasonEvents,
  moodChange,
  moodWordFor,
  ordinalPlace,
  percentages,
  predictedScore,
  satisfactionPlot,
  showsCap,
  signedNumber,
} from './fan-presentation';
import { expectation, fanEvent } from './fan-test-data';

describe('fan presentation', () => {
  beforeEach(() => configurePresentation({ locale: 'en-GB', timeZone: 'UTC' }));
  afterEach(() => resetPresentation());

  describe('mood words', () => {
    it.each([
      [0, 'Furious'],
      [999, 'Furious'],
      [1_000, 'Angry'],
      [2_500, 'Unhappy'],
      [3_999, 'Unhappy'],
      [4_000, 'Calm'],
      [5_999, 'Calm'],
      [6_000, 'Content'],
      [7_500, 'Delighted'],
      [9_000, 'Euphoric'],
      [10_000, 'Euphoric'],
    ])('names %d basis points %s, each band including its lower edge', (bp, phrase) => {
      expect(moodWordFor(bp).phrase).toBe(phrase);
    });
  });

  describe('numbers', () => {
    it('signs a change with a real minus sign', () => {
      expect(signedNumber(450)).toBe('+450');
      expect(signedNumber(-1_250)).toBe('−1,250');
      expect(signedNumber(0)).toBe('0');
    });

    it('shows a change in satisfaction to one decimal', () => {
      expect(moodChange(5_000, 5_284)).toBe('+2.8');
      expect(moodChange(5_000, 4_220)).toBe('−7.8');
      expect(moodChange(5_000, 5_000)).toBe('0.0');
    });

    it('says a place the way the fans do', () => {
      expect([1, 2, 3, 4, 11, 12, 13, 21, 22, 23, 101, 111].map(ordinalPlace)).toEqual([
        '1st',
        '2nd',
        '3rd',
        '4th',
        '11th',
        '12th',
        '13th',
        '21st',
        '22nd',
        '23rd',
        '101st',
        '111th',
      ]);
    });
  });

  describe('expectation', () => {
    it('gives chances that always add up to 100', () => {
      const odd = expectation({ winBp: 3_333, drawBp: 3_333, lossBp: 3_334 });
      const [win, draw, loss] = percentages(odd);

      expect(win + draw + loss).toBe(100);
      expect(chancesLabel(expectation())).toBe('58% win, 24% draw, 18% loss');
    });

    it('prints the predicted score with the club first', () => {
      expect(predictedScore(expectation({ predictedGoalsFor: 3, predictedGoalsAgainst: 0 }))).toBe(
        '3–0',
      );
    });

    it('tints the three hopeful labels as a win, the middle two as a draw and the last two as a defeat', () => {
      expect(['should_hammer_them', 'expect_a_win', 'should_win'].map(expectationTone)).toEqual([
        'good',
        'good',
        'good',
      ]);
      expect(['could_go_either_way', 'a_point_would_do'].map(expectationTone)).toEqual([
        'even',
        'even',
      ]);
      expect(['tough_game', 'damage_limitation'].map(expectationTone)).toEqual(['bad', 'bad']);
    });
  });

  describe('words for the events', () => {
    it('says where the club stood against the fans’ target', () => {
      expect(gapLabel(2)).toBe('2 places behind the fans’ target');
      expect(gapLabel(1)).toBe('1 place behind the fans’ target');
      expect(gapLabel(-3)).toBe('3 places ahead of the fans’ target');
      expect(gapLabel(0)).toBe('on the fans’ target');
      expect(gapLabel(null)).toBe('');
    });

    it('says what the fans expected of a match and what the club took', () => {
      expect(eventNote(fanEvent({ expectedPointsCentis: 198, pointsWon: 3 }))).toBe(
        'Fans expected 1.98 points, the club took 3',
      );
    });
  });

  describe('holding back what the manager has not seen', () => {
    const events = [
      fanEvent({ id: 'open', kind: 'opening' }),
      fanEvent({ id: 'm1', fixtureId: 'f1' }),
      fanEvent({ id: 'w1', kind: 'weekly' }),
      fanEvent({ id: 'm2', fixtureId: 'f2' }),
      fanEvent({ id: 'm3', fixtureId: 'f3' }),
    ];

    it('shows everything when every result has been seen', () => {
      const held = holdBackHistory(events, new Set());

      expect(held.events).toHaveLength(5);
      expect(held.heldBack).toBe(false);
    });

    it('ends the history before the first match that is unseen, and drops what follows it', () => {
      const held = holdBackHistory(events, new Set(['f2']));

      expect(held.events.map((event) => event.id)).toEqual(['open', 'm1', 'w1']);
      expect(held.heldBack).toBe(true);
    });

    it('cuts at the earliest unseen match even when a later one is seen', () => {
      const held = holdBackHistory(events, new Set(['f1', 'f3']));

      expect(held.events.map((event) => event.id)).toEqual(['open']);
    });
  });

  describe('charts', () => {
    it('keeps only the latest season for the satisfaction chart', () => {
      const events = [
        fanEvent({ id: 'a', seasonNumber: 1 }),
        fanEvent({ id: 'b', seasonNumber: 2 }),
        fanEvent({ id: 'c', seasonNumber: 2 }),
      ];

      expect(latestSeasonEvents(events).map((event) => event.id)).toEqual(['b', 'c']);
      expect(latestSeasonEvents([])).toEqual([]);
    });

    it('draws satisfaction on a 0–100 scale between the two dashed edges of the calm band', () => {
      const plot = satisfactionPlot([
        fanEvent({ moodAfterBp: 5_000 }),
        fanEvent({ id: 'e2', moodAfterBp: 7_650 }),
      ]);

      expect(plot.series.map((line) => line.label)).toEqual([
        'Satisfaction',
        'Calm from 40',
        'Content from 60',
      ]);
      expect(plot.series[0].data).toEqual([50, 76.5]);
      expect(plot.series[1].data).toEqual([40, 40]);
      expect(plot.series[1].dash.length).toBeGreaterThan(0);
      expect(plot.series[0].notes?.[1]).toContain('Delighted');
    });

    it('draws the fan count with its floor, and the cap only once the fans are a third of the way', () => {
      const early = [fanEvent({ fansAfter: 10_000 })];
      const late = [fanEvent({ fansAfter: 70_000 })];

      expect(showsCap(early, 183_333)).toBe(false);
      expect(showsCap(late, 183_333)).toBe(true);
      expect(fanCountPlot(early, 4_000, 183_333).series.map((line) => line.label)).toEqual([
        'Fans',
        'Floor',
      ]);
      expect(fanCountPlot(late, 4_000, 183_333).series.map((line) => line.label)).toEqual([
        'Fans',
        'Floor',
        'Soft cap',
      ]);
    });

    it('marks a takeover on the fan count chart and nothing else', () => {
      const plot = fanCountPlot(
        [
          fanEvent({ id: 'a', fansAfter: 9_000 }),
          fanEvent({ id: 'b', kind: 'takeover', fansAfter: 10_000 }),
        ],
        4_000,
        183_333,
      );
      const marker = plot.series.find((line) => line.label === 'New manager');

      expect(marker?.data).toEqual([null, 10_000]);
    });

    it('names the season on the labels once more than one season is drawn', () => {
      const plot = fanCountPlot(
        [fanEvent({ seasonNumber: 1 }), fanEvent({ id: 'b', seasonNumber: 2 })],
        4_000,
        183_333,
      );

      expect(plot.labels[0]).toMatch(/^S1 /);
      expect(plot.labels[1]).toMatch(/^S2 /);
    });
  });
});
