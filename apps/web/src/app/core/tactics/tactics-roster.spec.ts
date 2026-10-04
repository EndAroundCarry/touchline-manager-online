import { PlayerAttributes, SquadPlayer } from '../squad/squad.models';
import { positionForRole } from './tactics-presentation';
import { buildRoster } from './tactics-roster';
import { SelectablePlayer } from './tactics.models';

/**
 * The tactics player table's rows: who is listed, in what order, what they are rated as, and which shirt
 * they would wear.
 */

function attributes(value: number): PlayerAttributes {
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

function selectable(
  id: string,
  fullName: string,
  primaryPosition: string,
  positionFamily: string,
  isUnavailable = false,
): SelectablePlayer {
  return { id, fullName, shortName: fullName, primaryPosition, positionFamily, isUnavailable };
}

function detail(id: string, age: number, condition: number, fatigue: number): SquadPlayer {
  return {
    id,
    age,
    state: { condition, fatigue, morale: 50, matchSharpness: 50 },
    attributes: attributes(12),
  } as unknown as SquadPlayer;
}

const PLAYERS = [
  selectable('st1', 'Zed Striker', 'st', 'attack'),
  selectable('cb1', 'Ben Back', 'cb', 'defence'),
  selectable('gk1', 'Gus Keeper', 'gk', 'goalkeeper', true),
  selectable('cb2', 'Al Back', 'cb', 'defence'),
];

const SQUAD = [detail('st1', 24, 90, 10), detail('cb1', 30, 70, 40), detail('gk1', 28, 100, 0)];

describe('buildRoster', () => {
  it('lists goalkeepers first, then each family by name', () => {
    const roster = buildRoster(PLAYERS, SQUAD, [], null);

    expect(roster.map((row) => row.id)).toEqual(['gk1', 'cb2', 'cb1', 'st1']);
  });

  it('joins the squad read: age, condition, fatigue and attributes', () => {
    const row = buildRoster(PLAYERS, SQUAD, [], null).find((candidate) => candidate.id === 'cb1');

    expect(row).toMatchObject({ age: 30, condition: 70, fatigue: 40 });
    expect(row?.attributes).not.toBeNull();
  });

  it('keeps a player the squad read has no record of, with the details missing', () => {
    const row = buildRoster(PLAYERS, SQUAD, [], null).find((candidate) => candidate.id === 'cb2');

    expect(row).toMatchObject({ age: null, condition: null, fatigue: null, ratings: null });
  });

  it('shows the slot a player fills as the shirt they would wear', () => {
    const roster = buildRoster(
      PLAYERS,
      SQUAD,
      [
        { slotNumber: 5, playerId: 'cb1' },
        { slotNumber: 6, playerId: null },
      ],
      null,
    );

    expect(roster.find((row) => row.id === 'cb1')?.slotNumber).toBe(5);
    expect(roster.find((row) => row.id === 'st1')?.slotNumber).toBeNull();
  });

  it('rates each player at their own position by default', () => {
    const roster = buildRoster(PLAYERS, SQUAD, [], null);

    expect(roster.every((row) => row.ratedAs === row.position)).toBe(true);
  });

  it('rates everyone for a chosen position, so they can be compared for it', () => {
    const roster = buildRoster(PLAYERS, SQUAD, [], 'cb');

    expect(roster.every((row) => row.ratedAs === 'cb')).toBe(true);
  });

  it('keeps every rating on the 1-20 scale', () => {
    const roster = buildRoster(PLAYERS, SQUAD, [], null);

    for (const row of roster) {
      for (const value of Object.values(row.ratings ?? {})) {
        expect(value).toBeGreaterThanOrEqual(1);
        expect(value).toBeLessThanOrEqual(20);
      }
    }
  });
});

describe('positionForRole', () => {
  it('rates each role as the position it plays', () => {
    expect(positionForRole('goalkeeper')).toBe('gk');
    expect(positionForRole('centre_back')).toBe('cb');
    expect(positionForRole('wing_back')).toBe('rb');
    expect(positionForRole('striker')).toBe('st');
  });

  it('has no position for a role it does not know', () => {
    expect(positionForRole('libero')).toBeNull();
  });
});
