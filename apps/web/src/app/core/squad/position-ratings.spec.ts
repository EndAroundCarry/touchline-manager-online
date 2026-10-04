import {
  ATTRIBUTE_MAX,
  attributeImportance,
  attributeWeight,
  familyAverage,
  positionAverages,
  ratedAtPrimary,
  skillGroups,
} from './position-ratings';
import { PlayerAttributes, SquadPlayer } from './squad.models';

/**
 * The position-weighted averages.
 *
 * The properties that matter are the ones a manager relies on: an average never leaves the 1–20 scale, the
 * skills a position cares about move its average more than the ones it does not, and a flat player scores
 * the same everywhere.
 */

const POSITIONS = ['gk', 'rb', 'cb', 'lb', 'dm', 'cm', 'am', 'rw', 'lw', 'st'];

function flat(value: number): PlayerAttributes {
  return {
    technical: {
      finishing: value,
      passing: value,
      crossing: value,
      dribbling: value,
      firstTouch: value,
      tackling: value,
      marking: value,
      heading: value,
      technique: value,
      setPieces: value,
    },
    mental: {
      decisions: value,
      vision: value,
      positioning: value,
      composure: value,
      anticipation: value,
      workRate: value,
      aggression: value,
      leadership: value,
    },
    physical: {
      pace: value,
      acceleration: value,
      stamina: value,
      strength: value,
      agility: value,
      jumpingReach: value,
    },
    goalkeeping: { handling: value, reflexes: value, oneOnOnes: value, aerialAbility: value },
  };
}

/** A flat 10 with the two tackling attributes raised: what a centre back is judged on. */
function ballWinner(): PlayerAttributes {
  const base = flat(10);

  return { ...base, technical: { ...base.technical, tackling: 18, marking: 18 } };
}

describe('position ratings', () => {
  describe('weights', () => {
    it('doubles what a centre back is judged on, and does not count a striker’s craft', () => {
      expect(attributeWeight('cb', 'tackling')).toBe(2);
      expect(attributeWeight('cb', 'strength')).toBe(1.5);
      expect(attributeWeight('cb', 'dribbling')).toBe(0.5);
      expect(attributeImportance('cb', 'passing')).toBe('normal');
    });

    it('weighs the same skill differently at different positions', () => {
      expect(attributeWeight('st', 'finishing')).toBe(2);
      expect(attributeWeight('cb', 'finishing')).toBe(0.5);
      expect(attributeWeight('rw', 'pace')).toBe(2);
      expect(attributeWeight('gk', 'handling')).toBe(2);
      expect(attributeWeight('st', 'handling')).toBe(0.5);
    });

    it('treats the two flanks alike', () => {
      for (const attribute of ['tackling', 'crossing', 'pace', 'dribbling', 'finishing']) {
        expect(attributeWeight('lb', attribute)).toBe(attributeWeight('rb', attribute));
        expect(attributeWeight('lw', attribute)).toBe(attributeWeight('rw', attribute));
      }
    });

    it('leaves every attribute neutral at a position the client does not know', () => {
      expect(attributeWeight('sweeper', 'tackling')).toBe(1);
    });
  });

  describe('averages', () => {
    it('is the weighted mean, not a plain one', () => {
      // Weights at cb for the technical family sum to 10: tackling 2, marking 2, heading 1.5, passing 1,
      // first touch 1, and five marginal skills at 0.5. (18*2 + 18*2 + 10*6) / 10 = 13.2; the plain mean is 11.6.
      expect(familyAverage(ballWinner(), 'technical', 'cb')).toBe(13.2);
    });

    it('rates the same player lower where the skills they have are not the ones that count', () => {
      const atCentreBack = familyAverage(ballWinner(), 'technical', 'cb');
      const atStriker = familyAverage(ballWinner(), 'technical', 'st');

      expect(atCentreBack).toBeGreaterThan(atStriker);
      expect(atStriker).toBe(10.8);
    });

    it('never leaves the 1-20 scale', () => {
      for (const position of POSITIONS) {
        const best = positionAverages(flat(ATTRIBUTE_MAX), position);
        const worst = positionAverages(flat(1), position);

        expect(Object.values(best).every((value) => value === ATTRIBUTE_MAX)).toBe(true);
        expect(Object.values(worst).every((value) => value === 1)).toBe(true);
      }
    });

    it('does not put an out-of-range attribute above 20 on screen', () => {
      expect(positionAverages(flat(25), 'cb').technical).toBe(ATTRIBUTE_MAX);
    });

    it('is a plain mean for a family the position has no view on', () => {
      const attributes: PlayerAttributes = {
        ...flat(10),
        goalkeeping: { handling: 4, reflexes: 8, oneOnOnes: 12, aerialAbility: 16 },
      };

      expect(familyAverage(attributes, 'goalkeeping', 'st')).toBe(10);
    });

    it('rounds to one decimal', () => {
      const base = flat(10);
      const attributes = { ...base, technical: { ...base.technical, tackling: 11 } };

      const value = familyAverage(attributes, 'technical', 'cb');

      expect(value).toBe(Math.round(value * 10) / 10);
    });
  });

  describe('ratedAtPrimary', () => {
    it('rates a squad player at the position they are listed at', () => {
      const player = {
        id: 'p',
        primaryPosition: 'cb',
        attributes: ballWinner(),
      } as unknown as SquadPlayer;

      expect(ratedAtPrimary(player).ratings.technical).toBe(13.2);
    });
  });

  describe('skillGroups', () => {
    it('leads with the family a goalkeeper is judged on, and with technical for everyone else', () => {
      expect(skillGroups(flat(10), 'gk')[0].key).toBe('goalkeeping');
      expect(skillGroups(flat(10), 'cb')[0].key).toBe('technical');
    });

    it('carries all twenty-eight attributes, each with its weight', () => {
      const groups = skillGroups(ballWinner(), 'cb');
      const rows = groups.flatMap((group) => group.rows);
      const tackling = rows.find((row) => row.label === 'Tackling');

      expect(rows).toHaveLength(28);
      expect(tackling).toMatchObject({ value: 18, weight: 2, importance: 'key' });
    });

    it('agrees with the table: each group carries the family average the table shows', () => {
      const group = skillGroups(ballWinner(), 'cb').find(
        (candidate) => candidate.key === 'technical',
      );

      expect(group?.average).toBe(positionAverages(ballWinner(), 'cb').technical);
    });
  });
});
