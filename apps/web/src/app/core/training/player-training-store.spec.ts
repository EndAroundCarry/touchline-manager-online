import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { HISTORY_DAYS, PlayerTrainingStore } from './player-training-store';
import { TrainingApi } from './training-api';
import type { PlayerTraining } from './training.models';

/**
 * The player training history store.
 *
 * It keeps the last history read, asks for a fixed window of days, drops an answer that arrives for a player
 * the manager has already left, reports a refusal in the server's words, and forgets everything at sign-out.
 */

function history(playerId: string): PlayerTraining {
  return {
    playerId,
    regime: {
      programme: 'forward',
      label: 'Forward',
      description: 'For strikers.',
      isDefaultProgramme: true,
      intensity: 'normal',
      attributes: [{ name: 'finishing', family: 'technical', weight: 3 }],
    },
    days: [],
    summary: [],
    serverTime: '2026-10-04T00:00:00Z',
  };
}

describe('PlayerTrainingStore', () => {
  let api: { playerTraining: ReturnType<typeof vi.fn> };
  let store: PlayerTrainingStore;

  beforeEach(() => {
    api = { playerTraining: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: TrainingApi, useValue: api }] });

    store = TestBed.inject(PlayerTrainingStore);
  });

  it('reads a player’s history over the fixed window and keeps it', () => {
    api.playerTraining.mockReturnValue(of(history('p1')));

    store.load('p1');

    expect(api.playerTraining).toHaveBeenCalledWith('p1', HISTORY_DAYS);
    expect(store.history()?.playerId).toBe('p1');
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
  });

  it('is loading until the answer arrives', () => {
    const pending = new Subject<PlayerTraining>();

    api.playerTraining.mockReturnValue(pending);

    store.load('p1');

    expect(store.loading()).toBe(true);

    pending.next(history('p1'));

    expect(store.loading()).toBe(false);
  });

  it('drops an answer for a player the manager has since left', () => {
    const first = new Subject<PlayerTraining>();

    api.playerTraining.mockReturnValueOnce(first);
    api.playerTraining.mockReturnValueOnce(of(history('p2')));

    store.load('p1');
    store.load('p2');
    first.next(history('p1'));

    expect(store.history()?.playerId).toBe('p2');
  });

  it('drops a late failure for a player the manager has since left', () => {
    const first = new Subject<PlayerTraining>();

    api.playerTraining.mockReturnValueOnce(first);
    api.playerTraining.mockReturnValueOnce(of(history('p2')));

    store.load('p1');
    store.load('p2');
    first.error(new ApiError(403, 'CLUB_NOT_MANAGED', 'Not yours.', null, new Map()));

    expect(store.error()).toBeNull();
    expect(store.history()?.playerId).toBe('p2');
  });

  it('reports a refusal in the server’s words', () => {
    api.playerTraining.mockReturnValue(
      throwError(
        () =>
          new ApiError(
            403,
            'CLUB_NOT_MANAGED',
            'That player is not at your club.',
            null,
            new Map(),
          ),
      ),
    );

    store.load('p1');

    expect(store.error()).toBe('That player is not at your club.');
    expect(store.loading()).toBe(false);
  });

  it('says so, plainly, when the failure is not an API refusal', () => {
    api.playerTraining.mockReturnValue(throwError(() => new Error('offline')));

    store.load('p1');

    expect(store.error()).toBe("This player's training could not be loaded.");
  });

  it('forgets everything on clear, so one manager never sees the previous history', () => {
    api.playerTraining.mockReturnValue(of(history('p1')));

    store.load('p1');
    store.clear();

    expect(store.history()).toBeNull();
    expect(store.error()).toBeNull();
    expect(store.loading()).toBe(false);
  });
});
