import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { TrainingApi } from './training-api';
import { TrainingStore } from './training-store';
import { training } from './training-test-data';

/**
 * The training store's guarantees.
 *
 * The concurrency contract is the point: a revise carries the plan's version in `If-Match` while a first
 * plan carries none, a `412` keeps the manager's choices and pulls the server's state, and a reapply then
 * goes out against the version that just arrived (CONC-1, ADR-0009, §11.2). A player's programme override
 * carries the same contract on its own version, and the rest is that a fresh club starts from the defaults
 * and that signing out leaves nothing behind.
 */

function createApiStub() {
  return { get: vi.fn(), save: vi.fn(), setProgramme: vi.fn() };
}

function saved(overrides: Record<string, unknown>) {
  return {
    playerId: 'p1',
    programme: 'forward',
    isDefaultProgramme: true,
    defaultProgramme: 'forward',
    version: 0,
    serverTime: '2026-09-25T00:00:00Z',
    ...overrides,
  };
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
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 3, intensity: 'intense' })));

    store.load();

    expect(store.draft()).toEqual({ intensity: 'intense' });
    expect(store.isDirty()).toBe(false);
    expect(store.loading()).toBe(false);
  });

  it('starts an unconfigured club from the implicit defaults, still clean', () => {
    api.get.mockReturnValue(of(training()));

    store.load();

    expect(store.training()?.isConfigured).toBe(false);
    expect(store.draft()).toEqual({ intensity: 'normal' });
    expect(store.isDirty()).toBe(false);
    expect(store.intensityOptions()[0]).toEqual({ value: 'light', label: 'Light' });
  });

  it('serves the catalogue and one row per player, marked by that player’s own programme', () => {
    api.get.mockReturnValue(of(training()));

    store.load();

    expect(store.programmes().map((programme) => programme.code)).toEqual([
      'goalkeeper',
      'forward',
      'recovery',
    ]);
    expect(store.rows().map((row) => row.selected)).toEqual(['', 'recovery']);
    expect(store.rows()[0].cells.filter((cell) => cell.weight > 0)).toHaveLength(3);
    expect(store.rows()[1].cells.filter((cell) => cell.weight > 0)).toHaveLength(0);
  });

  it('has no rows or catalogue before the first read', () => {
    expect(store.rows()).toEqual([]);
    expect(store.programmes()).toEqual([]);
  });

  it('marks an edit dirty', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));

    store.load();
    store.setIntensity('intense');

    expect(store.draft()?.intensity).toBe('intense');
    expect(store.isDirty()).toBe(true);
  });

  it('creates the first plan with no If-Match, because there is nothing to be conditional against', () => {
    api.get.mockReturnValue(of(training()));
    api.save.mockReturnValue(of(training({ isConfigured: true, version: 1, intensity: 'light' })));

    store.load();
    store.setIntensity('light');
    store.save();

    expect(api.save).toHaveBeenCalledWith({ intensity: 'light' }, undefined);
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

    expect(api.save).toHaveBeenCalledWith({ intensity: 'intense' }, '"3"');
    expect(store.training()?.version).toBe(4);
    expect(store.savedMessage()).toContain('saved');
  });

  it('keeps the draft on 412 and reapplies it against the state that arrived (CONC-1)', () => {
    api.get.mockReturnValueOnce(of(training({ isConfigured: true, version: 3 })));

    // The reload the store performs on the conflict: another device won, so the server now holds version 5.
    api.get.mockReturnValueOnce(
      of(training({ isConfigured: true, version: 5, intensity: 'light' })),
    );

    api.save.mockReturnValueOnce(
      throwError(
        () => new ApiError(412, 'PRECONDITION_FAILED', 'The plan changed.', null, new Map()),
      ),
    );

    store.load();
    store.setIntensity('intense');
    store.save();

    expect(store.hasConflict()).toBe(true);
    expect(store.draft()?.intensity).toBe('intense');
    expect(api.get).toHaveBeenCalledTimes(2);

    // A reapply goes out against the version that just arrived, not the stale one that failed.
    api.save.mockReturnValueOnce(
      of(training({ isConfigured: true, version: 6, intensity: 'intense' })),
    );

    store.reapply();

    expect(api.save).toHaveBeenLastCalledWith({ intensity: 'intense' }, '"5"');
    expect(store.hasConflict()).toBe(false);
  });

  it('sets a first override without an If-Match and adopts the answer', () => {
    api.get.mockReturnValue(of(training()));
    api.setProgramme.mockReturnValue(
      of(saved({ programme: 'recovery', isDefaultProgramme: false, version: 1 })),
    );

    store.load();
    store.setPlayerProgramme('p1', 'recovery');

    expect(api.setProgramme).toHaveBeenCalledWith('p1', 'recovery', undefined);

    const updated = store.training()?.players.find((candidate) => candidate.id === 'p1');

    expect(updated?.programme).toBe('recovery');
    expect(updated?.isDefaultProgramme).toBe(false);
    expect(updated?.focusVersion).toBe(1);
    expect(store.rows()[0].selected).toBe('recovery');
    expect(store.savedMessage()).toContain('updated');
  });

  it('changes a set override under its own version, and clears it with the same contract', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));
    api.setProgramme.mockReturnValueOnce(
      of(
        saved({
          playerId: 'p2',
          programme: 'goalkeeper',
          isDefaultProgramme: false,
          defaultProgramme: 'goalkeeper',
          version: 8,
        }),
      ),
    );
    api.setProgramme.mockReturnValueOnce(
      of(
        saved({
          playerId: 'p2',
          programme: 'goalkeeper',
          isDefaultProgramme: true,
          defaultProgramme: 'goalkeeper',
          version: 0,
        }),
      ),
    );

    store.load();
    store.setPlayerProgramme('p2', 'goalkeeper');

    expect(api.setProgramme).toHaveBeenLastCalledWith('p2', 'goalkeeper', '"7"');

    store.setPlayerProgramme('p2', null);

    expect(api.setProgramme).toHaveBeenLastCalledWith('p2', null, '"8"');

    const cleared = store.training()?.players.find((candidate) => candidate.id === 'p2');

    expect(cleared?.isDefaultProgramme).toBe(true);
    expect(cleared?.focusVersion).toBeNull();
    expect(store.rows()[1].selected).toBe('');
    expect(store.savedMessage()).toContain('position default');
  });

  it('does nothing when a programme is set to the one the player already holds', () => {
    api.get.mockReturnValue(of(training()));

    store.load();
    store.setPlayerProgramme('p2', 'recovery');
    store.setPlayerProgramme('p1', null);

    expect(api.setProgramme).not.toHaveBeenCalled();
  });

  it('lets a manager pin a player’s default programme by name, which is an override', () => {
    api.get.mockReturnValue(of(training()));
    api.setProgramme.mockReturnValue(of(saved({ isDefaultProgramme: false, version: 1 })));

    store.load();
    store.setPlayerProgramme('p1', 'forward');

    expect(api.setProgramme).toHaveBeenCalledWith('p1', 'forward', undefined);
  });

  it('refreshes the roster on a stale override and says so', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));
    api.setProgramme.mockReturnValue(
      throwError(() => new ApiError(412, 'PRECONDITION_FAILED', 'That changed.', null, new Map())),
    );

    store.load();
    store.setPlayerProgramme('p2', 'forward');

    expect(store.programmeError()).not.toBeNull();
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(store.savingProgrammePlayerId()).toBeNull();
  });

  it('reports another refusal by its own words and leaves the roster alone', () => {
    api.get.mockReturnValue(of(training()));
    api.setProgramme.mockReturnValue(
      throwError(
        () => new ApiError(403, 'CLUB_NOT_MANAGED', 'That player is not yours.', null, new Map()),
      ),
    );

    store.load();
    store.setPlayerProgramme('p1', 'recovery');

    expect(store.programmeError()).toBe('That player is not yours.');
    expect(api.get).toHaveBeenCalledTimes(1);
    expect(store.training()?.players[0].programme).toBe('forward');
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

  it('forgets everything on clear, so one manager never sees the previous squad', () => {
    api.get.mockReturnValue(of(training({ isConfigured: true, version: 1 })));

    store.load();
    expect(store.draft()).not.toBeNull();
    expect(store.rows()).toHaveLength(2);

    store.clear();

    expect(store.training()).toBeNull();
    expect(store.draft()).toBeNull();
    expect(store.rows()).toEqual([]);
    expect(store.isDirty()).toBe(false);
  });
});
