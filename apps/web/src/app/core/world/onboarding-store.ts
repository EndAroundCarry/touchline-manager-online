import { Injectable, inject, signal } from '@angular/core';
import { Observable, forkJoin, map, of, switchMap, tap } from 'rxjs';
import { WorldApi } from './world-api';
import {
  AvailableClubs,
  ClubDashboard,
  CountryCapacity,
  CountrySummary,
  ManagerProfile,
  OnboardingState,
  WorldSummary,
} from './world.models';

/**
 * The onboarding state: where this account stands, and what it can choose next.
 *
 * A feature-scoped store rather than a global one, following the auth module's `SessionStore`. It holds
 * reference data and the account's own onboarding position — nothing here is competitive state, so nothing
 * here needs the invalidation care a squad or a bid does.
 */
@Injectable({ providedIn: 'root' })
export class OnboardingStore {
  private readonly api = inject(WorldApi);

  private readonly stateSignal = signal<OnboardingState | null>(null);
  private readonly worldSignal = signal<WorldSummary | null>(null);
  private readonly countriesSignal = signal<readonly CountrySummary[]>([]);
  private readonly capacitiesSignal = signal<readonly CountryCapacity[]>([]);
  private readonly availableClubsSignal = signal<AvailableClubs | null>(null);

  /**
   * The idempotency key per club being claimed.
   *
   * Kept so a double click, or a retry after a dropped connection, presents the same key and the server
   * answers with the first attempt's outcome. A fresh key per request would turn the second click into a
   * second claim, which is exactly what the key exists to prevent.
   */
  private readonly claimKeys = new Map<string, string>();

  /** Where the account stands: a profile, a club, or neither. */
  readonly state = this.stateSignal.asReadonly();

  /** The world being onboarded into. */
  readonly world = this.worldSignal.asReadonly();

  /** Every country, in presentation order. */
  readonly countries = this.countriesSignal.asReadonly();

  /** Each country's capacity, for the country picker. */
  readonly capacities = this.capacitiesSignal.asReadonly();

  /** The clubs of the country last opened. */
  readonly availableClubs = this.availableClubsSignal.asReadonly();

  /** Reads the account's onboarding position. */
  loadState(): Observable<OnboardingState> {
    return this.api.state().pipe(tap((state) => this.stateSignal.set(state)));
  }

  /** Reads the world. A 404 here means the seeder has not been run. */
  loadWorld(): Observable<WorldSummary> {
    return this.api.world().pipe(tap((world) => this.worldSignal.set(world)));
  }

  /** Reads the countries, caching them. */
  loadCountries(): Observable<readonly CountrySummary[]> {
    const loaded = this.countriesSignal();

    return loaded.length > 0
      ? of(loaded)
      : this.api.countries().pipe(tap((countries) => this.countriesSignal.set(countries)));
  }

  /** Reads the countries, then each country's capacity, so the picker can show which ones have room. */
  loadCapacities(): Observable<readonly CountryCapacity[]> {
    return this.loadCountries().pipe(
      switchMap((countries) =>
        countries.length === 0
          ? of<readonly CountryCapacity[]>([])
          : forkJoin(countries.map((country) => this.api.capacity(country.id))),
      ),
      tap((capacities) => this.capacitiesSignal.set(capacities)),
    );
  }

  /** Reads the clubs of a country's lowest active tier. */
  loadAvailableClubs(countryId: string): Observable<AvailableClubs> {
    return this.api
      .availableClubs(countryId)
      .pipe(tap((clubs) => this.availableClubsSignal.set(clubs)));
  }

  /** Creates the manager profile, then re-reads the position it changed. */
  createManagerProfile(locale: string, timeZone: string): Observable<ManagerProfile> {
    return this.api
      .createManagerProfile({ locale, timeZone })
      .pipe(switchMap((profile) => this.loadState().pipe(map(() => profile))));
  }

  /**
   * Claims a club under a stable idempotency key.
   *
   * The key survives a failed attempt on purpose. A claim that timed out may have succeeded on the server,
   * and the only way to find out is to present the same key again: the retry then returns the first
   * attempt's outcome instead of creating a second tenure. A key that produced a tenure is dropped, so a
   * later, deliberate claim of the same club — after a resignation, say — is a new attempt rather than a
   * replay of the old one.
   */
  claimClub(clubId: string): Observable<ClubDashboard> {
    const key = this.claimKeys.get(clubId) ?? crypto.randomUUID();

    this.claimKeys.set(clubId, key);

    return this.api.claimClub(clubId, key).pipe(tap({ next: () => this.claimKeys.delete(clubId) }));
  }

  /** Resigns from the manager's club. */
  resign(): Observable<OnboardingState> {
    return this.api.resign().pipe(tap((state) => this.stateSignal.set(state)));
  }

  /** Reads a club's dashboard. */
  dashboard(clubId: string): Observable<ClubDashboard> {
    return this.api.dashboard(clubId);
  }

  /** Forgets the cached reference data. Called when the session ends. */
  clear(): void {
    this.stateSignal.set(null);
    this.worldSignal.set(null);
    this.countriesSignal.set([]);
    this.capacitiesSignal.set([]);
    this.availableClubsSignal.set(null);
    this.claimKeys.clear();
  }

  /** Looks one country's capacity up by identity, for a picker that renders one row per country. */
  capacityFor(countryId: string): CountryCapacity | null {
    return this.capacitiesSignal().find((capacity) => capacity.countryId === countryId) ?? null;
  }
}
