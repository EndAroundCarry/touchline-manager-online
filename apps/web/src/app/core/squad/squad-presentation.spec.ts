import {
  NO_SQUAD_FILTER,
  attributeBand,
  attributeGroups,
  availabilityLabel,
  familyLabel,
  filterSquad,
  footLabel,
  positionFamilyOf,
  positionLabel,
  squadStatusLabel,
  stateBand,
  stateRows,
} from './squad-presentation';
import { PlayerAttributes, PlayerState, SquadPlayer } from './squad.models';

/**
 * The squad presentation helpers.
 *
 * The bands are the F-17 gate in unit form: master plan §11.3 requires an attribute's colour to also
 * carry a number, an icon, or text, and the band word is that text — so these tests pin that every
 * number lands in a band that has a word, on both the 1–20 attribute scale and the 0–100 state scale.
 */

function squadPlayer(
  id: string,
  fullName: string,
  primaryPosition: string,
  overrides: Partial<SquadPlayer> = {},
): SquadPlayer {
  return {
    id,
    fullName,
    shortName: fullName.slice(0, 3).toUpperCase(),
    nationalityCode: 'ENG',
    age: 25,
    preferredFoot: 'right',
    primaryPosition,
    secondaryPositions: [],
    state: { condition: 100, fatigue: 0, morale: 50, matchSharpness: 50 },
    contract: null,
    availability: [],
    ...overrides,
  };
}

const attributes: PlayerAttributes = {
  technical: {
    finishing: 10,
    passing: 11,
    crossing: 9,
    dribbling: 12,
    firstTouch: 10,
    tackling: 8,
    marking: 7,
    heading: 6,
    technique: 13,
    setPieces: 5,
  },
  mental: {
    decisions: 12,
    vision: 11,
    positioning: 10,
    composure: 9,
    anticipation: 14,
    workRate: 15,
    aggression: 8,
    leadership: 7,
  },
  physical: { pace: 16, acceleration: 15, stamina: 14, strength: 13, agility: 12, jumpingReach: 11 },
  goalkeeping: { handling: 1, reflexes: 2, oneOnOnes: 3, aerialAbility: 4 },
};

const state: PlayerState = { condition: 90, fatigue: 10, morale: 60, matchSharpness: 55 };

describe('squad presentation', () => {
  describe('attributeBand', () => {
    it('places every value on the 1-20 scale into a band with a word', () => {
      const bands = Array.from({ length: 20 }, (_, index) => attributeBand(index + 1));

      expect(bands.every((band) => band.label.length > 0)).toBe(true);
      expect(bands.every((band) => band.className.length > 0)).toBe(true);
      expect(attributeBand(1).band).toBe('low');
      expect(attributeBand(6).band).toBe('low');
      expect(attributeBand(7).band).toBe('average');
      expect(attributeBand(13).band).toBe('average');
      expect(attributeBand(14).band).toBe('strong');
      expect(attributeBand(20).band).toBe('strong');
    });
  });

  describe('stateBand', () => {
    it('reads high condition as strong and high fatigue as weak', () => {
      expect(stateBand(90, true).band).toBe('strong');
      expect(stateBand(90, false).band).toBe('low');
      expect(stateBand(10, false).band).toBe('strong');
      expect(stateBand(60, true).band).toBe('average');
      expect(stateBand(50, true).band).toBe('average');
      expect(stateBand(49, true).band).toBe('low');
    });
  });

  describe('position helpers', () => {
    it('maps every position code to a family', () => {
      expect(positionFamilyOf('gk')).toBe('goalkeeper');
      expect(positionFamilyOf('cb')).toBe('defence');
      expect(positionFamilyOf('cm')).toBe('midfield');
      expect(positionFamilyOf('st')).toBe('attack');
      expect(positionFamilyOf('keeper')).toBeNull();
    });

    it('names a position, and falls back to the code rather than showing nothing', () => {
      expect(positionLabel('cb')).toBe('Centre back');
      expect(positionLabel('xyz')).toBe('xyz');
      expect(familyLabel('defence')).toBe('Defenders');
      expect(squadStatusLabel('key_player')).toBe('Key player');
      expect(squadStatusLabel('unknown')).toBe('unknown');
      expect(footLabel('both')).toBe('Either');
    });

    it('describes an absence in fixtures, with the right plural (TRN-12)', () => {
      expect(availabilityLabel('injury', 1)).toBe('Injured · 1 fixture');
      expect(availabilityLabel('injury', 3)).toBe('Injured · 3 fixtures');
      expect(availabilityLabel('suspension', 1)).toBe('Suspended · 1 fixture');
    });
  });

  describe('filterSquad', () => {
    const players = [
      squadPlayer('1', 'Alaric Alderwick', 'gk'),
      squadPlayer('2', 'Bramwell Brambleby', 'cb'),
      squadPlayer('3', 'Corin Cawthorne', 'st', {
        availability: [
          {
            id: 'a1',
            type: 'injury',
            severity: 'minor',
            remainingFixtures: 2,
            startedAt: '2026-09-01T00:00:00Z',
          },
        ],
      }),
    ];

    it('returns everything when nothing is applied', () => {
      expect(filterSquad(players, NO_SQUAD_FILTER)).toHaveLength(3);
    });

    it('matches the name case-insensitively and ignores surrounding space', () => {
      expect(filterSquad(players, { ...NO_SQUAD_FILTER, name: '  bramble  ' })).toHaveLength(1);
      expect(filterSquad(players, { ...NO_SQUAD_FILTER, name: 'ZZZ' })).toHaveLength(0);
    });

    it('filters by position family', () => {
      const defenders = filterSquad(players, { ...NO_SQUAD_FILTER, positionFamily: 'defence' });

      expect(defenders).toHaveLength(1);
      expect(defenders[0].fullName).toBe('Bramwell Brambleby');
    });

    it('filters to available players only', () => {
      const available = filterSquad(players, { ...NO_SQUAD_FILTER, availableOnly: true });

      expect(available).toHaveLength(2);
      expect(available.map((player) => player.id)).not.toContain('3');
    });

    it('combines the filters', () => {
      const filtered = filterSquad(players, {
        name: 'c',
        positionFamily: 'attack',
        availableOnly: false,
      });

      expect(filtered).toHaveLength(1);
      expect(filtered[0].id).toBe('3');
    });
  });

  describe('attributeGroups', () => {
    it('groups all twenty-eight attributes into four families, goalkeeping first', () => {
      const groups = attributeGroups(attributes);

      expect(groups.map((group) => group.key)).toEqual([
        'goalkeeping',
        'technical',
        'mental',
        'physical',
      ]);
      expect(groups.reduce((total, group) => total + group.rows.length, 0)).toBe(28);
      expect(groups.every((group) => group.rows.every((row) => row.label.length > 0))).toBe(true);
      expect(groups[1].rows[0]).toEqual({ label: 'Finishing', value: 10 });
      expect(groups[0].rows[0]).toEqual({ label: 'Handling', value: 1 });
    });
  });

  describe('stateRows', () => {
    it('shows the four measures with their bands and direction', () => {
      const rows = stateRows(state);

      expect(rows.map((row) => row.label)).toEqual([
        'Condition',
        'Fatigue',
        'Morale',
        'Match sharpness',
      ]);
      expect(rows[0].band.band).toBe('strong');
      expect(rows[1].higherIsBetter).toBe(false);

      // Low fatigue is good, so it bands as strong while a high value reads as weak.
      expect(rows[1].band.band).toBe('strong');
      expect(stateBand(95, false).band).toBe('low');
    });
  });
});
