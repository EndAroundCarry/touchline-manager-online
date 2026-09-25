import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { TrainingApi } from './training-api';
import { TrainingStore } from './training-store';
import { Training, TrainingPlayer } from './training.models';

/**
 * The training store's guarantees.
 *
 * The concurrency contract is the point: a revise carries the plan's version in `If-Match` while a first
 * plan carries none, a `412` keeps the manager's choices and pulls the server's state, and a reapply then
 * goes out against the version that just arrived (CONC-1, ADR-0009, §11.2). A player's focus carries the
 * same contract on its own version, and the rest is that a fresh club starts from the defaults and that
 * signing out leaves nothing behind.
 */

function player(overrides: Partial<TrainingPlayer> = {}): TrainingPlayer {
  return {
    id: 'p1',
    fullName: 'Alaric Alderwick',
    shortName: 'ALD',
    primaryPosition: 'gk',
    positionFamily: 'goalkeeper',
    age: 24,
    state: { condition: 100, fatigue: 0, morale: 50, matchSharpness: 50 },
    focusFamily: null,
    focusVersion: null,
    ...overrides,
  };
}

function training(overrides: Partial<Training> = {}): Training {
  return {
    clubId: 'club-1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    countryCode: 'ENG',
    seasonNumber: 1,
    teamFocus: 'balanced',
    intensity: 'normal',
    effectiveDate: '2026-09-01',
    version: 0,
    isConfigured: false,
    teamFocusOptions: [
      'balanced',
      'recovery',
      'fitness',
      'attacking',
      'defending',
      'technical',
      'tactical',
    ],
    intensityOptions: ['light', 'normal', 'intense'],
    focusFamilyOptions: ['technical', 'mental', 'physical', 'goalkeeping'],
    players: [
      player(),
      player({
        id: 'p2',
        fullName: 'Bramwell Brambleby',
        shortName: 'BRA',
        primaryPosition: 'cm',
        positionFamily: 'midfield',
        focusFamily: 'technical',
        focusVersion: 7,
      }),
    ],
    serverTime: '2026-09-25T00:00:00Z',
    ...overrides,
  };
}

function createApiStub() {
  return { get: vi.fn(), save: vi.fn(), setFocus: vi.fn() };
}

describe('TrainingStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: TrainingStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: TrainingApi, useValue: api }] });

    store = TestBed.inject(TrainingStore);
  });

  it('reads the training state and starts editing the plan in force, clean', () => {
    api.get.mockReturnValue(
      of(training({ isConfigured: true, version: 3, teamFocus: 'recovery' })),
    );

    store.load();

    expect(store.draft()).toEqual({ teamFocus: 'recovery', intensity: 'normal' });
    expect(store.isDirty()).toBe(false);
    expect(store.loading()).toBe(false);
  });

  it('starts an unconfigured club from the implicit defaults, still clean', () => {
    api.get.mockReturnValue(of(training()));

    store.load();

    expect(store.training()?.isConfigured).toBe(false);
    expect(store.draft()).toEqual({ teamFocus: 'balanced', intensity: 'normal' });
    expect(store.isDirty()).toBe(false);
    expect(store.teamFocusOptions()[0]).toEqual({ value: 'balanced', label: 'Balanced' });
    expect(store.focusOptions()[0]).toEqual({ value: '', label: 'Team plan' });
  });

  it('marks an edit dirty', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));

    store.load();
    store.setTeamFocus('fitness');

    expect(store.draft()?.teamFocus).toBe('fitness');
    expect(store.isDirty()).toBe(true);
  });

  it('creates the first plan with no If-Match, because there is nothing to be conditional against', () => {
    api.get.mockReturnValue(of(training()));
    api.save.mockReturnValue(
      of(training({ isConfigured: true, version: 1, teamFocus: 'fitness' })),
    );

    store.load();
    store.setTeamFocus('fitness');
    store.save();

    expect(api.save).toHaveBeenCalledWith({ teamFocus: 'fitness', intensity: 'normal' }, undefined);
    expect(store.training()?.version).toBe(1);
    expect(store.isDirty()).toBe(false);
    expect(store.savedMessage()).toContain('created');
  });

  it('revises a set plan under the server version', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 3 })));
    api.save.mockReturnValue(
      of(training({ isConfigured: true, version: 4, intensity: 'intense' })),
    );

    store.load();
    store.setIntensity('intense');
    store.save();

    expect(api.save).toHaveBeenCalledWith({ teamFocus: 'balanced', intensity: 'intense' }, '"3"');
    expect(store.training()?.version).toBe(4);
    expect(store.savedMessage()).toContain('saved');
  });

  it('keeps the draft on 412 and reapplies it against the state that arrived (CONC-1)', () => {
    api.get.mockReturnValueOnce(of(training({ isConfigured: true, version: 3 })));

    // The reload the store performs on the conflict: another device won, so the server now holds version 5.
    api.get.mockReturnValueOnce(
      of(training({ isConfigured: true, version: 5, teamFocus: 'recovery' })),
    );

    api.save.mockReturnValueOnce(
      throwError(
        () => new ApiError(412, 'PRECONDITION_FAILED', 'The plan changed.', null, new Map()),
      ),
    );

    store.load();
    store.setTeamFocus('fitness');
    store.save();

    expect(store.hasConflict()).toBe(true);
    expect(store.draft()?.teamFocus).toBe('fitness');
    expect(api.get).toHaveBeenCalledTimes(2);

    // A reapply goes out against the version that just arrived, not the stale one that failed.
    api.save.mockReturnValueOnce(
      of(training({ isConfigured: true, version: 6, teamFocus: 'fitness' })),
    );

    store.reapply();

    expect(api.save).toHaveBeenLastCalledWith({ teamFocus: 'fitness', intensity: 'normal' }, '"5"');
    expect(store.hasConflict()).toBe(false);
  });

  it('sets a new player focus without an If-Match and adopts the answer', () => {
    api.get.mockReturnValue(of(training()));
    api.setFocus.mockReturnValue(
      of({
        playerId: 'p1',
        focusFamily: 'physical',
        version: 1,
        serverTime: '2026-09-25T00:00:00Z',
      }),
    );

    store.load();
    store.setPlayerFocus('p1', 'physical');

    expect(api.setFocus).toHaveBeenCalledWith('p1', 'physical', undefined);

    const updated = store.training()?.players.find((candidate) => candidate.id === 'p1');

    expect(updated?.focusFamily).toBe('physical');
    expect(updated?.focusVersion).toBe(1);
    expect(store.savedMessage()).toContain('updated');
  });

  it('changes a set focus under its own version, and clears it with the same contract', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));
    api.setFocus.mockReturnValueOnce(
      of({ playerId: 'p2', focusFamily: 'mental', version: 8, serverTime: '2026-09-25T00:00:00Z' }),
    );
    api.setFocus.mockReturnValueOnce(
      of({ playerId: 'p2', focusFamily: null, version: 0, serverTime: '2026-09-25T00:00:00Z' }),
    );

    store.load();
    store.setPlayerFocus('p2', 'mental');

    expect(api.setFocus).toHaveBeenLastCalledWith('p2', 'mental', '"7"');

    store.setPlayerFocus('p2', null);

    expect(api.setFocus).toHaveBeenLastCalledWith('p2', null, '"8"');

    const cleared = store.training()?.players.find((candidate) => candidate.id === 'p2');

    expect(cleared?.focusFamily).toBeNull();
    expect(cleared?.focusVersion).toBeNull();
    expect(store.savedMessage()).toContain('team');
  });

  it('does nothing when a focus is set to the value it already holds', () => {
    api.get.mockReturnValue(of(training()));

    store.load();
    store.setPlayerFocus('p2', 'technical');

    expect(api.setFocus).not.toHaveBeenCalled();
  });

  it('refreshes the roster on a stale focus and says so', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));
    api.setFocus.mockReturnValue(
      throwError(() => new ApiError(412, 'PRECONDITION_FAILED', 'That changed.', null, new Map())),
    );

    store.load();
    store.setPlayerFocus('p2', 'mental');

    expect(store.focusError()).not.toBeNull();
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(store.savingFocusPlayerId()).toBeNull();
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
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));

    store.load();
    expect(store.draft()).not.toBeNull();

    store.clear();

    expect(store.training()).toBeNull();
    expect(store.draft()).toBeNull();
    expect(store.isDirty()).toBe(false);
  });
});
