import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import {
  MAX_INSTRUCTION_NAME_LENGTH,
  MAX_MATCH_INSTRUCTIONS,
  blankInstruction,
} from './match-instructions';
import { previewShape } from './match-instructions-fit';
import { TacticsApi } from './tactics-api';
import {
  PlanDraft,
  defaultInstructions,
  draftFromFormation,
  draftFromPlan,
  isDirty,
  toRequest,
  withAssignment,
  withAssistantFatigue,
  withDuplicatedInstruction,
  withInstructionOrder,
  withMatchInstruction,
  withoutMatchInstruction,
} from './tactics-draft';
import { TacticsStore } from './tactics-store';
import type {
  FormationPreset,
  MatchInstruction,
  SelectablePlayer,
  TacticalPlan,
  Tactics,
} from './tactics.models';

/**
 * The plan's match instructions in the draft, and what a save sends.
 *
 * The rule that matters most: a save sends the instructions only when the manager has touched them. A save that sends none keeps the
 * stored ones, so changing the lineup does not make the server judge instructions the manager never opened. The dirty check, on the
 * other hand, compares the instructions as they stand, so putting an edit back is not a change.
 */

const SLOTS: readonly [string, string][] = [
  ['goalkeeper', 'goalkeeper'],
  ['defence', 'full_back'],
  ['defence', 'centre_back'],
  ['defence', 'centre_back'],
  ['defence', 'full_back'],
  ['midfield', 'wide_midfielder'],
  ['midfield', 'central_midfielder'],
  ['midfield', 'central_midfielder'],
  ['midfield', 'wide_midfielder'],
  ['attack', 'striker'],
  ['attack', 'striker'],
];

function formation(code: string): FormationPreset {
  return {
    code,
    slots: SLOTS.map(([positionFamily, role], index) => ({
      slotNumber: index + 1,
      positionFamily,
      role,
      normalizedX: 500 + index * 700,
      normalizedY: 1_000 + (index % 5) * 2_000,
    })),
  };
}

function rule(name: string): MatchInstruction {
  return { ...blankInstruction(name), fromMinute: 60 };
}

function savedPlan(matchInstructions: readonly MatchInstruction[] = []): TacticalPlan {
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
    matchInstructions,
    assistantFatigueSubstitutions: true,
    matchInstructionWarnings: [],
  };
}

describe('match instructions in the draft', () => {
  const empty = draftFromPlan(savedPlan());

  it('starts a plan with none, and the assistant making the fatigue substitutions', () => {
    expect(empty.matchInstructions).toEqual([]);
    expect(empty.assistantFatigueSubstitutions).toBe(true);
    expect(draftFromFormation(formation('4-4-2')).matchInstructions).toEqual([]);
  });

  it('adds an instruction at the end, replaces one in place, and removes one', () => {
    const added = withMatchInstruction(withMatchInstruction(empty, 0, rule('A')), 1, rule('B'));

    expect(added.matchInstructions.map((r) => r.name)).toEqual(['A', 'B']);

    const renamed = withMatchInstruction(added, 0, { ...rule('A'), name: 'A2' });

    expect(renamed.matchInstructions.map((r) => r.name)).toEqual(['A2', 'B']);
    expect(withoutMatchInstruction(renamed, 0).matchInstructions.map((r) => r.name)).toEqual(['B']);
  });

  it('holds at most ten instructions', () => {
    let draft = empty;

    for (let count = 0; count < MAX_MATCH_INSTRUCTIONS + 3; count++) {
      draft = withMatchInstruction(draft, draft.matchInstructions.length, rule(`R${count}`));
    }

    expect(draft.matchInstructions).toHaveLength(MAX_MATCH_INSTRUCTIONS);
    expect(withDuplicatedInstruction(draft, 0)).toBe(draft);
  });

  it('moves an instruction one place, and stops at the ends', () => {
    const draft = ['A', 'B', 'C'].reduce(
      (current, name, index) => withMatchInstruction(current, index, rule(name)),
      empty,
    );
    const names = (d: PlanDraft): string[] => d.matchInstructions.map((r) => r.name);

    expect(names(withInstructionOrder(draft, 2, -1))).toEqual(['A', 'C', 'B']);
    expect(names(withInstructionOrder(draft, 0, 1))).toEqual(['B', 'A', 'C']);
    expect(withInstructionOrder(draft, 0, -1)).toBe(draft);
    expect(withInstructionOrder(draft, 2, 1)).toBe(draft);
  });

  it('duplicates an instruction after itself, named as a copy that still fits the name limit', () => {
    const long = 'x'.repeat(MAX_INSTRUCTION_NAME_LENGTH);
    const draft = withMatchInstruction(withMatchInstruction(empty, 0, rule('A')), 1, rule(long));

    expect(withDuplicatedInstruction(draft, 0).matchInstructions.map((r) => r.name)).toEqual([
      'A',
      'A (copy)',
      long,
    ]);

    const copy = withDuplicatedInstruction(draft, 1).matchInstructions[2].name;

    expect(copy.length).toBe(MAX_INSTRUCTION_NAME_LENGTH);
    expect(copy.endsWith(' (copy)')).toBe(true);
  });

  it('sends the instructions only once they have been touched', () => {
    expect(toRequest(empty).matchInstructions).toBeNull();
    expect(toRequest(empty).assistantFatigueSubstitutions).toBe(true);

    const withRule = withMatchInstruction(empty, 0, rule('A'));

    expect(toRequest(withRule).matchInstructions).toEqual([rule('A')]);
    expect(toRequest(withoutMatchInstruction(withRule, 0)).matchInstructions).toEqual([]);
  });

  it('sends the assistant choice either way', () => {
    expect(toRequest(withAssistantFatigue(empty, false)).assistantFatigueSubstitutions).toBe(false);
  });

  it('is not dirty until an instruction or the assistant choice differs from what is saved', () => {
    const saved = savedPlan([rule('A')]);
    const draft = draftFromPlan(saved);

    expect(isDirty(draft, saved)).toBe(false);

    const edited = withMatchInstruction(draft, 0, { ...rule('A'), fromMinute: 70 });

    expect(isDirty(edited, saved)).toBe(true);
    expect(isDirty(withMatchInstruction(edited, 0, rule('A')), saved)).toBe(false);
    expect(isDirty(withAssistantFatigue(draft, false), saved)).toBe(true);
    expect(isDirty(withoutMatchInstruction(draft, 0), saved)).toBe(true);
  });

  it('keeps the instructions when the formation or the lineup changes', () => {
    const withRule = withMatchInstruction(empty, 0, rule('A'));
    const picked = withAssignment(withRule, 2, 'p1');

    expect(picked.matchInstructions).toEqual([rule('A')]);
  });
});

describe('TacticsStore match instructions', () => {
  const api = { get: vi.fn(), create: vi.fn(), update: vi.fn(), makeDefault: vi.fn() };
  const players: SelectablePlayer[] = [];

  function tactics(plan: TacticalPlan): Tactics {
    return {
      clubId: 'club-1',
      clubName: 'Ashvale United',
      clubShortName: 'ASH',
      countryCode: 'ENG',
      seasonNumber: 1,
      plans: [plan],
      selectablePlayers: players,
      formations: [formation('4-4-2'), formation('4-3-3')],
      serverTime: '2026-09-01T00:00:00Z',
    };
  }

  let store: TacticsStore;

  beforeEach(() => {
    vi.resetAllMocks();
    TestBed.configureTestingModule({ providers: [{ provide: TacticsApi, useValue: api }] });
    store = TestBed.inject(TacticsStore);
  });

  it('adds, orders, duplicates and removes instructions, and reports a change to save', () => {
    api.get.mockReturnValue(of(tactics(savedPlan())));
    store.load();

    expect(store.isDirty()).toBe(false);

    store.setMatchInstruction(0, rule('A'));
    store.setMatchInstruction(1, rule('B'));
    store.moveMatchInstruction(1, -1);
    store.duplicateMatchInstruction(0);

    expect(store.draft()?.matchInstructions.map((r) => r.name)).toEqual(['B', 'B (copy)', 'A']);
    expect(store.isDirty()).toBe(true);

    store.removeMatchInstruction(1);

    expect(store.draft()?.matchInstructions.map((r) => r.name)).toEqual(['B', 'A']);
  });

  it('sends the edited instructions with a save, and keeps the stored ones when only the lineup changed', () => {
    const saved = savedPlan([rule('Stored')]);

    api.get.mockReturnValue(of(tactics(saved)));
    api.update.mockReturnValue(of(saved));
    store.load();

    store.assignPlayer(2, 'p1');
    store.save();

    expect(api.update.mock.calls[0][1].matchInstructions).toBeNull();

    store.setMatchInstruction(1, rule('New'));
    store.save();

    expect(
      api.update.mock.calls[1][1].matchInstructions.map((r: MatchInstruction) => r.name),
    ).toEqual(['Stored', 'New']);
  });

  it('draws the plan back from the server after a save, with no instruction left marked as edited', () => {
    const saved = savedPlan([rule('Stored')]);

    api.get.mockReturnValue(of(tactics(savedPlan())));
    api.update.mockReturnValue(of(saved));
    store.load();
    store.setMatchInstruction(0, rule('Stored'));
    store.save();

    expect(store.draft()?.matchInstructionsEdited).toBe(false);
    expect(store.isDirty()).toBe(false);
  });
});

describe('previewShape', () => {
  const players: SelectablePlayer[] = SLOTS.map(([family], index) => ({
    id: `p${index + 1}`,
    fullName: `Player ${index + 1}`,
    shortName: `P${index + 1}`,
    primaryPosition:
      family === 'goalkeeper'
        ? 'gk'
        : family === 'defence'
          ? 'cb'
          : family === 'midfield'
            ? 'cm'
            : 'st',
    positionFamily: family,
    isUnavailable: false,
  }));

  function fullDraft(): PlanDraft {
    return players.reduce(
      (draft, player, index) => withAssignment(draft, index + 1, player.id),
      draftFromFormation(formation('4-4-2')),
    );
  }

  const noSecondary = new Map<string, readonly string[]>();

  it('has nothing to show for a rule that keeps the shape', () => {
    expect(
      previewShape(fullDraft(), blankInstruction(), [formation('4-3-3')], players, noSecondary),
    ).toBeNull();
  });

  it('keeps everyone in his own slot when the new shape is the same', () => {
    const preview = previewShape(
      fullDraft(),
      { ...blankInstruction(), formation: '4-4-2' },
      [formation('4-4-2')],
      players,
      noSecondary,
    );

    expect(preview).toHaveLength(11);
    expect(
      preview?.every((placement) => placement.slotNumber === Number(placement.playerId.slice(1))),
    ).toBe(true);
    expect(preview?.some((placement) => placement.isChosen)).toBe(false);
  });

  it('puts a player where the manager chose and marks it chosen', () => {
    const preview = previewShape(
      fullDraft(),
      {
        ...blankInstruction(),
        formation: '4-4-2',
        overrides: [{ playerId: 'p10', slotNumber: 3 }],
      },
      [formation('4-4-2')],
      players,
      noSecondary,
    );

    expect(preview?.find((placement) => placement.playerId === 'p10')).toEqual({
      playerId: 'p10',
      slotNumber: 3,
      isChosen: true,
    });
    expect(preview).toHaveLength(11);
  });

  it('fits only the players picked, leaving a slot empty when the eleven are not complete', () => {
    const draft = withAssignment(draftFromFormation(formation('4-4-2')), 1, 'p1');
    const preview = previewShape(
      draft,
      { ...blankInstruction(), formation: '4-4-2' },
      [formation('4-4-2')],
      players,
      noSecondary,
    );

    expect(preview).toEqual([{ playerId: 'p1', slotNumber: 1, isChosen: false }]);
  });
});
