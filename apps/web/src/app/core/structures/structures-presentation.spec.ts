import {
  bonusLabel,
  bonusPercent,
  buildDaysPhrase,
  customerPhrase,
  kindLabel,
  levelPips,
  readyOn,
  resultLabel,
  upgradeBlockedReason,
  weekLabel,
} from './structures-presentation';
import { Structure } from './structures.models';

/**
 * The structures' wording.
 *
 * The server owns every amount; these helpers only name results and moods, show a bonus with its sign, and say
 * in words why an upgrade cannot be ordered, in the order the server would refuse it.
 */

function building(overrides: Partial<Structure> = {}): Structure {
  return {
    kind: 'snack_bar',
    level: 1,
    maxLevel: 10,
    version: 1,
    spendMinor: 100,
    upkeepWeeklyMinor: 30_000,
    next: {
      level: 2,
      spendMinor: 125,
      upkeepWeeklyMinor: 45_000,
      costMinor: 2_500_000,
      buildDays: 1,
      estWeeklyProfitChangeMinor: 5_000,
    },
    building: null,
    bonuses: [],
    recentWeeks: [],
    recentMatches: [],
    seasonTakingsMinor: 0,
    seasonUpkeepMinor: 0,
    ...overrides,
  };
}

describe('Structures presentation', () => {
  it('names the buildings and who spends in them', () => {
    expect(kindLabel('snack_bar')).toBe('Snack bar');
    expect(kindLabel('merchandise')).toBe('Merchandise shop');
    expect(customerPhrase('snack_bar')).toBe('per spectator at a home match');
    expect(customerPhrase('merchandise')).toBe('per fan, each week');
  });

  it('names a result bonus line and a mood line in plain words', () => {
    expect(bonusLabel('underdog_win')).toBe('Win as the underdog');
    expect(bonusLabel('even_win')).toBe('Win in an even game');
    expect(bonusLabel('favourite_win')).toBe('Win as the favourite');
    expect(bonusLabel('underdog_draw')).toBe('Draw as the underdog');
    expect(bonusLabel('none')).toBe('Any other result');
    expect(bonusLabel('euphoric')).toBe('Euphoric');
    expect(bonusLabel('unheard_of')).toBe('unheard_of');
  });

  it('names a home result', () => {
    expect(resultLabel('win')).toBe('Won');
    expect(resultLabel('draw')).toBe('Drawn');
    expect(resultLabel('loss')).toBe('Lost');
  });

  it('shows a bonus with its sign, and none as 0%', () => {
    expect(bonusPercent(3_000)).toBe('+30%');
    expect(bonusPercent(250)).toBe('+2.5%');
    expect(bonusPercent(0)).toBe('0%');
  });

  it('draws one pip per level, filled up to the level reached', () => {
    expect(levelPips(3, 10)).toEqual([
      true,
      true,
      true,
      false,
      false,
      false,
      false,
      false,
      false,
      false,
    ]);
    expect(levelPips(10, 10).every(Boolean)).toBe(true);
  });

  it('names a week by its first day, without letting the time zone move the day', () => {
    expect(weekLabel('2026-10-19', 'en-GB')).toBe('Week of 19 Oct');
  });

  it('shows when work finishes in the given zone', () => {
    expect(readyOn('2026-10-23T14:00:00Z', 'en-GB', 'UTC')).toBe('Fri 23 Oct, 14:00');
  });

  it('counts build days', () => {
    expect(buildDaysPhrase(1)).toBe('1 day');
    expect(buildDaysPhrase(7)).toBe('7 days');
  });

  describe('why an upgrade cannot be ordered', () => {
    it('is not blocked when the club can pay and nothing is being built', () => {
      expect(upgradeBlockedReason(building(), 3_000_000, true)).toBeNull();
    });

    it('is not blocked when the club holds exactly the cost', () => {
      expect(upgradeBlockedReason(building(), 2_500_000, true)).toBeNull();
    });

    it('is blocked by work already under way, naming its level', () => {
      const reason = upgradeBlockedReason(
        building({ building: { targetLevel: 2, dueAt: '2026-10-23T14:00:00Z' } }),
        3_000_000,
        true,
      );

      expect(reason).toContain('level 2');
      expect(reason).toContain('under way');
    });

    it('is blocked at the top level', () => {
      expect(upgradeBlockedReason(building({ level: 10, next: null }), 99_000_000, true)).toBe(
        'This is the highest level there is.',
      );
    });

    it('is blocked in read-only mode', () => {
      expect(upgradeBlockedReason(building(), 3_000_000, false)).toContain('read-only');
    });

    it('is blocked when the club is short of cash', () => {
      expect(upgradeBlockedReason(building(), 2_499_999, true)).toContain('enough cash');
    });
  });
});
