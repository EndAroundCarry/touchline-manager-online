import cases from '../../../../../../tests/shared/shape-fit-cases.json';
import { ShapeFitPlayer, ShapeFitSlot, fitShape } from './shape-fit';

/**
 * The shape fit, pinned by the table of cases the engine and the domain are held to as well
 * (`tests/shared/shape-fit-cases.json`). The table spells positions, families and roles the way the C# enums do; the web app
 * uses the server's snake-case codes, so each is translated once, here.
 */

interface CasePlayer {
  readonly id: string;
  readonly position: string;
  readonly secondary: readonly string[];
  readonly slot: {
    readonly family: string;
    readonly role: string;
    readonly x: number;
    readonly y: number;
  };
}

interface CaseSlot {
  readonly slot: number;
  readonly family: string;
  readonly role: string;
  readonly x: number;
  readonly y: number;
}

interface FitCase {
  readonly name: string;
  readonly players: readonly CasePlayer[];
  readonly newSlots: readonly CaseSlot[];
  readonly overrides: Record<string, number>;
  readonly expected: Record<string, number>;
}

const POSITION_CODES: Record<string, string> = {
  Goalkeeper: 'gk',
  RightBack: 'rb',
  CentreBack: 'cb',
  LeftBack: 'lb',
  DefensiveMidfielder: 'dm',
  CentralMidfielder: 'cm',
  AttackingMidfielder: 'am',
  RightWinger: 'rw',
  LeftWinger: 'lw',
  Striker: 'st',
  RightMidfielder: 'rm',
  LeftMidfielder: 'lm',
};

/** `FullBack` becomes `full_back`. */
function snake(name: string): string {
  return name.replace(/(?<=[a-z])(?=[A-Z])/g, '_').toLowerCase();
}

function playerOf(source: CasePlayer): ShapeFitPlayer {
  return {
    id: source.id,
    position: POSITION_CODES[source.position],
    secondary: source.secondary.map((position) => POSITION_CODES[position]),
    standing: {
      family: snake(source.slot.family),
      role: snake(source.slot.role),
      x: source.slot.x,
      y: source.slot.y,
    },
  };
}

function slotOf(source: CaseSlot): ShapeFitSlot {
  return {
    slot: source.slot,
    family: snake(source.family),
    role: snake(source.role),
    x: source.x,
    y: source.y,
  };
}

const table = cases as unknown as { readonly cases: readonly FitCase[] };

describe('fitShape', () => {
  for (const fitCase of table.cases) {
    it(`agrees with the shared table: ${fitCase.name}`, () => {
      const result = fitShape(
        fitCase.players.map(playerOf),
        fitCase.newSlots.map(slotOf),
        new Map(Object.entries(fitCase.overrides)),
      );

      expect(Object.fromEntries(result)).toEqual(fitCase.expected);
    });
  }

  it('gives the same answer whatever order the players are listed in', () => {
    const fitCase = table.cases[1];
    const players = fitCase.players.map(playerOf);
    const slots = fitCase.newSlots.map(slotOf);

    expect(Object.fromEntries(fitShape([...players].reverse(), slots, null))).toEqual(
      Object.fromEntries(fitShape(players, slots, null)),
    );
  });

  it('refuses a shape whose slot numbers repeat', () => {
    const slot: ShapeFitSlot = {
      slot: 1,
      family: 'goalkeeper',
      role: 'goalkeeper',
      x: 500,
      y: 5000,
    };

    expect(() => fitShape([], [slot, slot], null)).toThrow();
  });
});
