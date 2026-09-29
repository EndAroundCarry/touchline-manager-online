import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { OnboardingStore } from './onboarding-store';
import { preferredTimeZone, resetPresentation } from './presentation';
import { WorldApi } from './world-api';
import { ClubDashboard, CountryCapacity, CountrySummary, ManagerProfile } from './world.models';

/**
 * The onboarding store's guarantees.
 *
 * The one that matters competitively is the claim's idempotency key: a claim that is retried must present
 * the same key, or a timeout becomes a second tenure. These tests pin that, the caching of reference data,
 * and that signing out does not leave the previous manager's club behind.
 */

const country: CountrySummary = {
  id: 'country-1',
  code: 'ENG',
  displayName: 'England',
  locale: 'en-GB',
  sortOrder: 1,
};

function capacity(countryId: string, available: number): CountryCapacity {
  return {
    countryId,
    lowestActiveTier: 1,
    lowestActiveTierDivisionId: 'division-1',
    lowestActiveTierName: 'England Top Division',
    clubsInLowestTier: 18,
    humanOccupiedClubs: 18 - available,
    availableClubs: available,
    lowestTierIsFull: available === 0,
    targetTierForExpansion: 2,
    provisioning: null,
    serverTime: '2026-09-01T00:00:00Z',
  };
}

const dashboard = {
  club: { id: 'club-1', name: 'Ashvale United' },
} as unknown as ClubDashboard;

const managerProfile: ManagerProfile = {
  id: 'manager-1',
  reputation: 0,
  takeoverCooldownUntil: null,
  locale: 'en-GB',
  timeZone: 'Europe/Bucharest',
  version: 3,
};

function createWorldApiStub() {
  return {
    world: vi.fn(),
    countries: vi.fn(),
    capacity: vi.fn(),
    availableClubs: vi.fn(),
    createManagerProfile: vi.fn(),
    updateManagerProfile: vi.fn(),
    claimClub: vi.fn(),
    resign: vi.fn(),
    state: vi.fn(),
    dashboard: vi.fn(),
  };
}

describe('OnboardingStore', () => {
  let api: ReturnType<typeof createWorldApiStub>;
  let store: OnboardingStore;

  beforeEach(() => {
    api = createWorldApiStub();

    TestBed.configureTestingModule({ providers: [{ provide: WorldApi, useValue: api }] });

    store = TestBed.inject(OnboardingStore);
  });

  afterEach(() => resetPresentation());

  it('sends a fresh idempotency key for a first claim', async () => {
    api.claimClub.mockReturnValue(of(dashboard));

    await firstValueFrom(store.claimClub('club-1'));

    expect(api.claimClub).toHaveBeenCalledTimes(1);
    expect(api.claimClub.mock.calls[0][0]).toBe('club-1');
    expect(api.claimClub.mock.calls[0][1]).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('reuses the same key when a failed claim is retried', async () => {
    // The first attempt may have succeeded on the server and only the response been lost, so the retry has
    // to present the same key to get the first attempt's outcome rather than a second tenure.
    api.claimClub.mockReturnValueOnce(throwError(() => new Error('timeout')));
    api.claimClub.mockReturnValueOnce(of(dashboard));

    await expect(firstValueFrom(store.claimClub('club-1'))).rejects.toThrow();
    await firstValueFrom(store.claimClub('club-1'));

    expect(api.claimClub.mock.calls[0][1]).toBe(api.claimClub.mock.calls[1][1]);
  });

  it('uses a new key after a claim succeeded, so a later claim is a new attempt', async () => {
    api.claimClub.mockReturnValue(of(dashboard));

    await firstValueFrom(store.claimClub('club-1'));
    await firstValueFrom(store.claimClub('club-1'));

    expect(api.claimClub.mock.calls[0][1]).not.toBe(api.claimClub.mock.calls[1][1]);
  });

  it('keeps a separate key per club', async () => {
    api.claimClub.mockReturnValueOnce(throwError(() => new Error('timeout')));
    api.claimClub.mockReturnValueOnce(throwError(() => new Error('timeout')));

    await expect(firstValueFrom(store.claimClub('club-1'))).rejects.toThrow();
    await expect(firstValueFrom(store.claimClub('club-2'))).rejects.toThrow();

    expect(api.claimClub.mock.calls[0][1]).not.toBe(api.claimClub.mock.calls[1][1]);
  });

  it('reads the countries once and serves the rest from the store', async () => {
    api.countries.mockReturnValue(of([country]));

    await firstValueFrom(store.loadCountries());
    await firstValueFrom(store.loadCountries());

    expect(api.countries).toHaveBeenCalledTimes(1);
    expect(store.countries()).toEqual([country]);
  });

  it('measures every country when the picker is opened', async () => {
    api.countries.mockReturnValue(of([country, { ...country, id: 'country-2', code: 'ESP' }]));
    api.capacity.mockImplementation((countryId: string) =>
      of(capacity(countryId, countryId === 'country-1' ? 4 : 0)),
    );

    await firstValueFrom(store.loadCapacities());

    expect(api.capacity).toHaveBeenCalledTimes(2);
    expect(store.capacityFor('country-1')?.availableClubs).toBe(4);
    expect(store.capacityFor('country-2')?.availableClubs).toBe(0);
    expect(store.capacityFor('country-3')).toBeNull();
  });

  it('measures nothing when the world has not been seeded', async () => {
    api.countries.mockReturnValue(of([]));

    await firstValueFrom(store.loadCapacities());

    expect(api.capacity).not.toHaveBeenCalled();
    expect(store.capacities()).toEqual([]);
  });

  it('drops everything it holds when the session ends', async () => {
    api.countries.mockReturnValue(of([country]));
    api.state.mockReturnValue(
      of({ manager: null, tenure: null, serverTime: '2026-09-01T00:00:00Z' }),
    );

    await firstValueFrom(store.loadCountries());
    await firstValueFrom(store.loadState());

    store.clear();

    expect(store.countries()).toEqual([]);
    expect(store.state()).toBeNull();
    expect(store.availableClubs()).toBeNull();
  });

  it("adopts the manager's time zone for formatting when the profile loads (CAL-4)", async () => {
    api.state.mockReturnValue(
      of({ manager: managerProfile, tenure: null, serverTime: '2026-09-01T00:00:00Z' }),
    );

    await firstValueFrom(store.loadState());

    expect(preferredTimeZone()).toBe('Europe/Bucharest');
  });

  it('changes preferences under the version it last read', async () => {
    api.state.mockReturnValue(
      of({ manager: managerProfile, tenure: null, serverTime: '2026-09-01T00:00:00Z' }),
    );
    await firstValueFrom(store.loadState());
    api.updateManagerProfile.mockReturnValue(
      of({ ...managerProfile, timeZone: 'Asia/Tokyo', version: 4 }),
    );

    await firstValueFrom(store.updateManagerProfile('en-GB', 'Asia/Tokyo'));

    expect(api.updateManagerProfile).toHaveBeenCalledWith(
      { locale: 'en-GB', timeZone: 'Asia/Tokyo' },
      3,
    );
    expect(store.state()?.manager?.version).toBe(4);
    // The new zone applies immediately, so every deadline on screen re-renders without a re-read.
    expect(preferredTimeZone()).toBe('Asia/Tokyo');
  });

  it('refuses to change preferences before the profile has loaded', async () => {
    await expect(firstValueFrom(store.updateManagerProfile('en-GB', 'UTC'))).rejects.toThrow();

    expect(api.updateManagerProfile).not.toHaveBeenCalled();
  });

  it('forgets the formatting preference when the session ends', async () => {
    // Pick a zone that cannot be this machine's own, so the assertion distinguishes "configured" from
    // "fell back to the browser" whatever the test host is set to.
    const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
    const configuredZone = browserZone === 'Asia/Tokyo' ? 'America/New_York' : 'Asia/Tokyo';

    api.state.mockReturnValue(
      of({
        manager: { ...managerProfile, timeZone: configuredZone },
        tenure: null,
        serverTime: '2026-09-01T00:00:00Z',
      }),
    );
    await firstValueFrom(store.loadState());
    expect(preferredTimeZone()).toBe(configuredZone);

    store.clear();

    expect(preferredTimeZone()).toBe(browserZone);
  });
});
