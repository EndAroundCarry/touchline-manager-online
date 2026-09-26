import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { MatchApi } from './match-api';
import { Match, MatchPresentation } from './match.models';
import { MatchStore } from './match-store';

/**
 * The match store's guarantees.
 *
 * The viewer plays a response and never edits one, so the contract here is simpler than the competition
 * store's: both reads are made together, one failure is one message, and a read never leaves the previous
 * match's data on screen behind a failure.
 */

function match(): Match {
  return {
    matchId: 'm1',
    fixtureId: 'f1',
    divisionId: 'd1',
    divisionName: 'England Top Division',
    tierNumber: 1,
    countryId: 'co1',
    countryCode: 'ENG',
    countryName: 'England',
    seasonNumber: 1,
    seasonLabel: '2026/27',
    roundNumber: 4,
    kickoffAt: '2026-10-06T19:00:00Z',
    status: 'published',
    home: {
      clubId: 'c1',
      name: 'Ashvale United',
      shortName: 'ASH',
      goals: 2,
      statistics: {
        possessionBasisPoints: 5_500,
        goals: 2,
        shots: 9,
        shotsOnTarget: 5,
        shotsOffTarget: 3,
        shotsBlocked: 1,
        woodworkHits: 0,
        saves: 3,
        corners: 6,
        offsides: 1,
        fouls: 8,
        yellowCards: 1,
        redCards: 0,
        penaltiesAwarded: 0,
        penaltiesScored: 0,
        injuries: 0,
        substitutions: 2,
      },
    },
    away: {
      clubId: 'c2',
      name: 'Bramford Rovers',
      shortName: 'BRA',
      goals: 1,
      statistics: {
        possessionBasisPoints: 4_500,
        goals: 1,
        shots: 7,
        shotsOnTarget: 4,
        shotsOffTarget: 3,
        shotsBlocked: 0,
        woodworkHits: 1,
        saves: 3,
        corners: 2,
        offsides: 3,
        fouls: 10,
        yellowCards: 2,
        redCards: 0,
        penaltiesAwarded: 0,
        penaltiesScored: 0,
        injuries: 1,
        substitutions: 3,
      },
    },
    engineVersion: 'engine-v1',
    presentationVersion: 'highlights-v1',
    serverTime: '2026-10-06T21:00:00Z',
  };
}

function presentation(): MatchPresentation {
  return {
    matchId: 'm1',
    presentationVersion: 'highlights-v1',
    engineVersion: 'engine-v1',
    homeGoals: 2,
    awayGoals: 1,
    commentary: [],
    highlights: [],
    estimatedPayloadBytes: 1_024,
  };
}

function createApiStub() {
  return { get: vi.fn(), presentation: vi.fn() };
}

describe('MatchStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: MatchStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({
      providers: [MatchStore, { provide: MatchApi, useValue: api }],
    });

    store = TestBed.inject(MatchStore);
  });

  it('reads the summary and the replay together', () => {
    api.get.mockReturnValue(of(match()));
    api.presentation.mockReturnValue(of(presentation()));

    store.load('m1');

    expect(api.get).toHaveBeenCalledWith('m1');
    expect(api.presentation).toHaveBeenCalledWith('m1');
    expect(store.match()?.matchId).toBe('m1');
    expect(store.presentation()?.presentationVersion).toBe('highlights-v1');
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
  });

  it('surfaces a refusal as the server explained it', () => {
    const refusal = new ApiError(
      404,
      'MATCH_NOT_FOUND',
      'That match has not been played.',
      null,
      new Map(),
    );

    api.get.mockReturnValue(throwError(() => refusal));
    api.presentation.mockReturnValue(of(presentation()));

    store.load('m1');

    expect(store.error()).toBe('That match has not been played.');
    expect(store.match()).toBeNull();
    expect(store.loading()).toBe(false);
  });

  it('falls back to a plain message when the failure is not a refusal', () => {
    api.get.mockReturnValue(throwError(() => new Error('offline')));
    api.presentation.mockReturnValue(of(presentation()));

    store.load('m1');

    expect(store.error()).toBe('That match could not be loaded.');
  });

  it('drops the previous match before reading the next one, so a failure cannot leave it on screen', () => {
    api.get.mockReturnValue(of(match()));
    api.presentation.mockReturnValue(of(presentation()));
    store.load('m1');

    api.get.mockReturnValue(throwError(() => new Error('offline')));
    store.load('m2');

    expect(store.match()).toBeNull();
    expect(store.presentation()).toBeNull();
  });

  it('forgets everything when the session ends', () => {
    api.get.mockReturnValue(of(match()));
    api.presentation.mockReturnValue(of(presentation()));
    store.load('m1');

    store.clear();

    expect(store.match()).toBeNull();
    expect(store.presentation()).toBeNull();
    expect(store.error()).toBeNull();
    expect(store.loading()).toBe(false);
  });
});
