import {
  NEW_PLAN_NAME,
  assignedCount,
  defaultInstructions,
  draftFromFormation,
  draftFromPlan,
  isComplete,
  isDirty,
  toRequest,
  withAssignment,
  withFormation,
  withInstruction,
  withMovedSlot,
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
    ...overrides,
  };
}

describe('tactics draft', () => {
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

  it('changes one slot at a time — assignment, role, and position', () => {
    const base = draftFromFormation(formation('4-4-2'));
    const assigned = withAssignment(base, 9, 'p9');
    const cleared = withAssignment(assigned, 9, null);
    const role = withRole(assigned, 9, 'striker');
    const moved = withMovedSlot(assigned, 9, 9_500, 500);

    expect(assigned.slots.find((slot) => slot.slotNumber === 9)?.playerId).toBe('p9');
    expect(cleared.slots.find((slot) => slot.slotNumber === 9)?.playerId).toBeNull();
    expect(role.slots.find((slot) => slot.slotNumber === 9)?.role).toBe('striker');
    expect(moved.slots.find((slot) => slot.slotNumber === 9)?.normalizedX).toBe(9_500);
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
});
