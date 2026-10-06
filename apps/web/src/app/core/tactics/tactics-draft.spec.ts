import {
  BENCH_FIRST_SLOT,
  BENCH_SIZE,
  KEEPER_BENCH_SLOT,
  NEW_PLAN_NAME,
  assignedCount,
  benchCount,
  defaultInstructions,
  draftFromFormation,
  draftFromPlan,
  isComplete,
  isDirty,
  toRequest,
  withAssignment,
  withBenchAssignment,
  withFormation,
  withCounterAttack,
  withInstruction,
  withName,
  withRole,
} from './tactics-draft';
import { FormationPreset, TacticalPlan } from './tactics.models';

/**
 * The editable plan and its transforms.
 *
 * These are the rules the screen leans on that a component test would reach only through the DOM: that a
 * formation change keeps the picks, that a half-filled lineup is sent rather than silently dropped, and
 * that the dirty check compares what would be saved rather than the object graph.
 */

const FAMILY_BY_SLOT: Record<number, string> = {
  1: 'goalkeeper',
  2: 'defence',
  3: 'defence',
  4: 'defence',
  5: 'defence',
  6: 'midfield',
  7: 'midfield',
  8: 'midfield',
  9: 'attack',
  10: 'attack',
  11: 'attack',
};

const ROLE_BY_SLOT: Record<number, string> = {
  1: 'goalkeeper',
  2: 'full_back',
  3: 'centre_back',
  4: 'centre_back',
  5: 'full_back',
  6: 'central_midfielder',
  7: 'central_midfielder',
  8: 'central_midfielder',
  9: 'winger',
  10: 'striker',
  11: 'striker',
};

function formation(code: string, shift = 0): FormationPreset {
  return {
    code,
    slots: Array.from({ length: 11 }, (_, index) => {
      const slotNumber = index + 1;

      return {
        slotNumber,
        positionFamily: FAMILY_BY_SLOT[slotNumber],
        role: ROLE_BY_SLOT[slotNumber],
        normalizedX: slotNumber * 100 + shift,
        normalizedY: slotNumber * 100,
      };
    }),
  };
}

function savedPlan(overrides: Partial<TacticalPlan> = {}): TacticalPlan {
  const base = draftFromFormation(formation('4-4-2'));

  return {
    id: 'plan-1',
    name: 'Home shape',
    formationPreset: '4-4-2',
    isDefault: true,
    instructions: defaultInstructions(),
    version: 3,
    assignedCount: 0,
    isComplete: false,
    slots: base.slots.map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
      assignedPlayer: null,
      isOutOfPosition: false,
    })),
    bench: [],
    ...overrides,
  };
}

describe('tactics draft', () => {
  it('starts with the counter-attack off, and sends it once it is switched on', () => {
    const draft = draftFromFormation(formation('4-4-2'));

    expect(draft.instructions.counterAttack).toBe(false);
    expect(toRequest(draft).counterAttack).toBe(false);

    const counter = withCounterAttack(draft, true);

    expect(toRequest(counter).counterAttack).toBe(true);
    expect(withCounterAttack(counter, false).instructions.counterAttack).toBe(false);
  });

  it('sends the pass focus with the other instructions', () => {
    const draft = withInstruction(draftFromFormation(formation('4-4-2')), 'passFocus', 'wings');

    expect(toRequest(draft).passFocus).toBe('wings');
  });

  it('starts a new plan from neutral instructions with nobody picked', () => {
    const draft = draftFromFormation(formation('4-4-2'));

    expect(draft.planId).toBeNull();
    expect(draft.version).toBeNull();
    expect(draft.name).toBe(NEW_PLAN_NAME);
    expect(draft.formationPreset).toBe('4-4-2');
    expect(draft.instructions).toEqual(defaultInstructions());
    expect(draft.slots).toHaveLength(11);
    expect(draft.slots.every((slot) => slot.playerId === null)).toBe(true);
  });

  it('mirrors a saved plan, including its assignments and version', () => {
    const plan = savedPlan({
      slots: savedPlan().slots.map((slot) =>
        slot.slotNumber === 1
          ? {
              ...slot,
              assignedPlayer: {
                id: 'p1',
                fullName: 'A',
                shortName: 'A',
                primaryPosition: 'gk',
                isUnavailable: false,
              },
            }
          : slot,
      ),
    });

    const draft = draftFromPlan(plan);

    expect(draft.planId).toBe('plan-1');
    expect(draft.version).toBe(3);
    expect(draft.name).toBe('Home shape');
    expect(draft.slots.find((slot) => slot.slotNumber === 1)?.playerId).toBe('p1');
  });

  it('re-lays the slots from a new formation, keeping the picks by slot number (TAC-7)', () => {
    const draft = withAssignment(draftFromFormation(formation('4-4-2')), 5, 'p5');
    const switched = withFormation(draft, formation('4-3-3', 250));

    expect(switched.formationPreset).toBe('4-3-3');
    expect(switched.slots.find((slot) => slot.slotNumber === 5)?.normalizedX).toBe(750);
    expect(switched.slots.find((slot) => slot.slotNumber === 5)?.playerId).toBe('p5');
  });

  it('changes one slot at a time — assignment and role, never position', () => {
    const base = draftFromFormation(formation('4-4-2'));
    const assigned = withAssignment(base, 9, 'p9');
    const cleared = withAssignment(assigned, 9, null);
    const role = withRole(assigned, 9, 'striker');

    expect(assigned.slots.find((slot) => slot.slotNumber === 9)?.playerId).toBe('p9');
    expect(cleared.slots.find((slot) => slot.slotNumber === 9)?.playerId).toBeNull();
    expect(role.slots.find((slot) => slot.slotNumber === 9)?.role).toBe('striker');
    expect(role.slots.find((slot) => slot.slotNumber === 9)?.normalizedX).toBe(
      base.slots.find((slot) => slot.slotNumber === 9)?.normalizedX,
    );
  });

  it('changes one instruction without disturbing the others (INS-1..INS-8)', () => {
    const draft = withInstruction(draftFromFormation(formation('4-4-2')), 'pressing', 'high_press');

    expect(draft.instructions.pressing).toBe('high_press');
    expect(draft.instructions.mentality).toBe('balanced');
  });

  it('renames the plan', () => {
    expect(withName(draftFromFormation(formation('4-4-2')), 'Away').name).toBe('Away');
  });

  it('omits the lineup when nobody is picked, and sends every pick when all eleven are', () => {
    const base = draftFromFormation(formation('4-4-2'));

    expect(toRequest(base).lineup).toBeNull();

    const full = base.slots.reduce(
      (draft, slot) => withAssignment(draft, slot.slotNumber, `p${slot.slotNumber}`),
      base,
    );

    expect(assignedCount(full)).toBe(11);
    expect(isComplete(full)).toBe(true);
    expect(toRequest(full).lineup).toHaveLength(11);
  });

  it('sends a partial lineup rather than dropping it, so the server can refuse it (SQ-4)', () => {
    const partial = withAssignment(
      withAssignment(draftFromFormation(formation('4-4-2')), 1, 'p1'),
      2,
      'p2',
    );

    const request = toRequest(partial);

    expect(request.lineup).toHaveLength(2);
    expect(isComplete(partial)).toBe(false);
    expect(assignedCount(partial)).toBe(2);
  });

  it('orders slots and lineup by slot number regardless of edit order', () => {
    const draft = withAssignment(
      withAssignment(draftFromFormation(formation('4-4-2')), 11, 'p11'),
      1,
      'p1',
    );

    const request = toRequest(draft);

    expect(request.slots.map((slot) => slot.slotNumber)).toEqual([
      1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11,
    ]);
    expect(request.lineup?.map((entry) => entry.slotNumber)).toEqual([1, 11]);
  });

  it('reads an untouched draft as clean and any change as dirty (CONC-1)', () => {
    const plan = savedPlan();
    const clean = draftFromPlan(plan);

    expect(isDirty(clean, plan)).toBe(false);
    expect(isDirty(withName(clean, 'Renamed'), plan)).toBe(true);
    expect(isDirty(clean, null)).toBe(true);
  });

  describe('the bench', () => {
    it('starts as seven empty places numbered after the eleven starters, and sends nothing', () => {
      const draft = draftFromFormation(formation('4-4-2'));

      expect(draft.bench).toHaveLength(BENCH_SIZE);
      expect(draft.bench.map((place) => place.slotNumber)).toEqual([12, 13, 14, 15, 16, 17, 18]);
      expect(draft.bench.every((place) => place.playerId === null)).toBe(true);
      expect(benchCount(draft)).toBe(0);
      expect(toRequest(draft).bench).toBeNull();
    });

    it('keeps the goalkeeper place as the first one', () => {
      expect(KEEPER_BENCH_SLOT).toBe(BENCH_FIRST_SLOT);
    });

    it('sends every named substitute in slot order, including a half-filled bench', () => {
      const draft = withBenchAssignment(
        withBenchAssignment(draftFromFormation(formation('4-4-2')), 15, 'p15'),
        12,
        'p12',
      );

      expect(benchCount(draft)).toBe(2);
      expect(toRequest(draft).bench).toEqual([
        { slotNumber: 12, playerId: 'p12' },
        { slotNumber: 15, playerId: 'p15' },
      ]);
    });

    it('empties a place when no player is given', () => {
      const named = withBenchAssignment(draftFromFormation(formation('4-4-2')), 13, 'p13');
      const emptied = withBenchAssignment(named, 13, null);

      expect(benchCount(emptied)).toBe(0);
      expect(toRequest(emptied).bench).toBeNull();
    });

    it('moves a starter to the bench rather than naming them twice', () => {
      const started = withAssignment(draftFromFormation(formation('4-4-2')), 5, 'p5');
      const benched = withBenchAssignment(started, 14, 'p5');

      expect(benched.slots.find((slot) => slot.slotNumber === 5)?.playerId).toBeNull();
      expect(benched.bench.find((place) => place.slotNumber === 14)?.playerId).toBe('p5');
    });

    it('moves a substitute who is picked to start off the bench', () => {
      const benched = withBenchAssignment(draftFromFormation(formation('4-4-2')), 14, 'p5');
      const started = withAssignment(benched, 5, 'p5');

      expect(started.slots.find((slot) => slot.slotNumber === 5)?.playerId).toBe('p5');
      expect(benchCount(started)).toBe(0);
    });

    it('moves a substitute between bench places rather than naming them twice', () => {
      const benched = withBenchAssignment(draftFromFormation(formation('4-4-2')), 13, 'p9');
      const moved = withBenchAssignment(benched, 16, 'p9');

      expect(moved.bench.find((place) => place.slotNumber === 13)?.playerId).toBeNull();
      expect(moved.bench.find((place) => place.slotNumber === 16)?.playerId).toBe('p9');
    });

    it('keeps the bench when the formation changes', () => {
      const benched = withBenchAssignment(draftFromFormation(formation('4-4-2')), 12, 'gk2');

      expect(withFormation(benched, formation('4-4-2')).bench).toEqual(benched.bench);
    });

    it('reads the saved bench back into the draft and treats it as clean until it changes', () => {
      const plan = savedPlan({
        bench: [
          {
            slotNumber: 12,
            assignedPlayer: {
              id: 'gk2',
              fullName: 'Second Keeper',
              shortName: 'KEE',
              primaryPosition: 'gk',
              isUnavailable: false,
            },
          },
        ],
      });
      const draft = draftFromPlan(plan);

      expect(draft.bench[0]).toEqual({ slotNumber: 12, playerId: 'gk2' });
      expect(isDirty(draft, plan)).toBe(false);
      expect(isDirty(withBenchAssignment(draft, 12, null), plan)).toBe(true);
    });
  });
});
