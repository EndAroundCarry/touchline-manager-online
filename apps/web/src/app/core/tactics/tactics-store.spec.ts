import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { TacticsApi } from './tactics-api';
import { defaultInstructions } from './tactics-draft';
import { TacticsStore } from './tactics-store';
import {
  FormationPreset,
  SelectablePlayer,
  TacticalPlan,
  TacticalPlanValidation,
  Tactics,
} from './tactics.models';

/**
 * The tactics store's guarantees.
 *
 * The concurrency contract is the point: a save carries the version the client last read, a `412` keeps
 * the manager's edits and pulls the server's state, and a reapply then goes out against the version that
 * just arrived (CONC-1, ADR-0009, §11.2). The rest is that a fresh club starts a new plan and that signing
 * out leaves nothing behind.
 */

function formation(code: string): FormationPreset {
  return {
    code,
    slots: Array.from({ length: 11 }, (_, index) => {
      const slotNumber = index + 1;

      return {
        slotNumber,
        positionFamily: index === 0 ? 'goalkeeper' : 'defence',
        role: index === 0 ? 'goalkeeper' : 'centre_back',
        normalizedX: slotNumber * 100,
        normalizedY: slotNumber * 100,
      };
    }),
  };
}

function plan(overrides: Partial<TacticalPlan> = {}): TacticalPlan {
  return {
    id: 'plan-1',
    name: 'Home shape',
    formationPreset: '4-4-2',
    isDefault: true,
    instructions: defaultInstructions(),
    version: 1,
    assignedCount: 0,
    isComplete: false,
    slots: formation('4-4-2').slots.map((slot) => ({
      ...slot,
      assignedPlayer: null,
      isOutOfPosition: false,
    })),
    ...overrides,
  };
}

const players: SelectablePlayer[] = [
  {
    id: 'p1',
    fullName: 'Alaric Alderwick',
    shortName: 'ALD',
    primaryPosition: 'gk',
    positionFamily: 'goalkeeper',
    isUnavailable: false,
  },
];

function tactics(plans: TacticalPlan[]): Tactics {
  return {
    clubId: 'club-1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    countryCode: 'ENG',
    seasonNumber: 1,
    plans,
    selectablePlayers: players,
    formations: [formation('4-4-2'), formation('4-3-3')],
    serverTime: '2026-09-01T00:00:00Z',
  };
}

function createApiStub() {
  return {
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    makeDefault: vi.fn(),
  };
}

describe('TacticsStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: TacticsStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: TacticsApi, useValue: api }] });

    store = TestBed.inject(TacticsStore);
  });

  it('reads the plans and starts editing the default one, clean', () => {
    api.get.mockReturnValue(of(tactics([plan({ isDefault: true })])));

    store.load();

    expect(store.selectedPlan()?.id).toBe('plan-1');
    expect(store.draft()?.planId).toBe('plan-1');
    expect(store.draft()?.slots).toHaveLength(11);
    expect(store.isDirty()).toBe(false);
    expect(store.loading()).toBe(false);
  });

  it('starts a new plan when the club has none', () => {
    api.get.mockReturnValue(of(tactics([])));

    store.load();

    expect(store.selectedPlan()).toBeNull();
    expect(store.draft()?.planId).toBeNull();
    expect(store.draft()?.formationPreset).toBe('4-4-2');
    expect(store.isDirty()).toBe(true);
  });

  it('marks an edit dirty', () => {
    api.get.mockReturnValue(of(tactics([plan()])));

    store.load();
    store.setName('Away shape');

    expect(store.draft()?.name).toBe('Away shape');
    expect(store.isDirty()).toBe(true);
  });

  it('saves a revision under the server version and adopts the answer', () => {
    api.get.mockReturnValue(of(tactics([plan({ version: 1 })])));
    api.update.mockReturnValue(of(plan({ name: 'Away shape', version: 2 })));

    store.load();
    store.setName('Away shape');
    store.save();

    expect(api.update).toHaveBeenCalledWith(
      'plan-1',
      expect.objectContaining({ name: 'Away shape' }),
      '"1"',
    );
    expect(store.selectedPlan()?.version).toBe(2);
    expect(store.draft()?.version).toBe(2);
    expect(store.savedMessage()).toContain('saved');
    expect(store.isDirty()).toBe(false);
  });

  it('creates the first plan the club saves', () => {
    api.get.mockReturnValue(of(tactics([])));
    api.create.mockReturnValue(of(plan({ id: 'plan-new', isDefault: true })));

    store.load();
    store.save();

    expect(api.create).toHaveBeenCalledTimes(1);
    expect(store.plans()).toHaveLength(1);
    expect(store.selectedPlan()?.id).toBe('plan-new');
    expect(store.savedMessage()).toContain('created');
  });

  it('keeps the validation preview a refused save came back with', () => {
    const preview: TacticalPlanValidation = {
      isValid: false,
      assignedCount: 2,
      isComplete: false,
      issues: [{ code: 'SELECTION_INCOMPLETE', slotNumber: null, playerId: null }],
    };

    api.get.mockReturnValue(of(tactics([plan()])));
    api.update.mockReturnValue(
      throwError(
        () =>
          new ApiError(
            400,
            'PLAN_VALIDATION_FAILED',
            'That plan is not valid.',
            null,
            new Map(),
            new Map([['validation', preview]]),
          ),
      ),
    );

    store.load();
    store.setName('Partial');
    store.save();

    expect(store.validation()).toEqual(preview);
    expect(store.saveError()).toBeNull();
  });

  it('keeps the draft on 412 and reapplies it against the state that arrived (CONC-1)', () => {
    api.get.mockReturnValueOnce(of(tactics([plan({ version: 1 })])));

    // The reload the store performs on the conflict: another device won, so the server now holds version 5.
    api.get.mockReturnValueOnce(of(tactics([plan({ name: 'Other device', version: 5 })])));

    api.update.mockReturnValueOnce(
      throwError(
        () => new ApiError(412, 'PRECONDITION_FAILED', 'The plan changed.', null, new Map()),
      ),
    );

    store.load();
    store.setName('Away shape');
    store.save();

    expect(store.hasConflict()).toBe(true);
    expect(store.draft()?.name).toBe('Away shape');
    expect(api.get).toHaveBeenCalledTimes(2);

    // A reapply goes out against the version that just arrived, not the stale one that failed.
    api.update.mockReturnValueOnce(of(plan({ name: 'Away shape', version: 6 })));

    store.reapply();

    expect(api.update).toHaveBeenLastCalledWith(
      'plan-1',
      expect.objectContaining({ name: 'Away shape' }),
      '"5"',
    );
    expect(store.hasConflict()).toBe(false);
  });

  it('promotes a plan to the default and demotes the previous one (INS-11)', () => {
    api.get.mockReturnValue(
      of(
        tactics([
          plan({ id: 'plan-1', isDefault: true }),
          plan({ id: 'plan-2', name: 'Away', isDefault: false }),
        ]),
      ),
    );
    api.makeDefault.mockReturnValue(
      of(plan({ id: 'plan-2', name: 'Away', isDefault: true, version: 2 })),
    );

    store.load();
    store.makeDefault('plan-2');

    expect(api.makeDefault).toHaveBeenCalledWith('plan-2', '"1"');
    expect(store.plans().find((candidate) => candidate.id === 'plan-2')?.isDefault).toBe(true);
    expect(store.plans().find((candidate) => candidate.id === 'plan-1')?.isDefault).toBe(false);
    expect(store.savedMessage()).toContain('default');
  });

  it('reports a refusal to read as a load error', () => {
    api.get.mockReturnValue(
      throwError(
        () => new ApiError(403, 'NO_CLUB', 'You do not manage a club yet.', null, new Map()),
      ),
    );

    store.load();

    expect(store.loadError()).toBe('You do not manage a club yet.');
    expect(store.draft()).toBeNull();
  });

  it('forgets everything on clear, so one manager never sees the previous plan', () => {
    api.get.mockReturnValue(of(tactics([plan()])));

    store.load();
    expect(store.draft()).not.toBeNull();

    store.clear();

    expect(store.tactics()).toBeNull();
    expect(store.draft()).toBeNull();
    expect(store.plans()).toEqual([]);
  });
});
