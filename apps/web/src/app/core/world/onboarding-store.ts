import { Injectable, inject, signal } from '@angular/core';
import { Observable, defer, forkJoin, map, of, switchMap, tap, throwError } from 'rxjs';
import { ConnectivityStore } from '../connectivity/connectivity-store';
import { configurePresentation, resetPresentation } from './presentation';
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
  private readonly connectivity = inject(ConnectivityStore);

  private readonly stateSignal = signal<OnboardingState | null>(null);
  private readonly worldSignal = signal<WorldSummary | null>(null);
  private readonly countriesSignal = signal<readonly CountrySummary[]>([]);
  private readonly capacitiesSignal = signal<readonly CountryCapacity[]>([]);
  private readonly availableClubsSignal = signal<AvailableClubs | null>(null);
  private readonly provisioningSettledSignal = signal(0);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollCountryId: string | null = null;

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

  /**
   * Increments each time a polled country's next tier finishes generating, so a screen waiting on it can
   * reload the clubs that just became claimable (`PYR-10`).
   */
  readonly provisioningSettled = this.provisioningSettledSignal.asReadonly();

  /**
   * Begins polling a country's capacity until its next tier is generated.
   *
   * The cadence is the server's own hint (`PYR-10`), re-read on every tick, and the poll is skipped while
   * the tab is hidden or the browser is offline — the same gating the shell's sync poll uses. It is one
   * target at a time, because a manager waits on one country.
   */
  startProvisioningPoll(countryId: string): void {
    this.stopProvisioningPoll();
    this.pollCountryId = countryId;
    this.scheduleProvisioningPoll();
  }

  /** Stops polling. */
  stopProvisioningPoll(): void {
    this.pollCountryId = null;

    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private scheduleProvisioningPoll(): void {
    if (this.pollCountryId === null || this.pollTimer !== null) {
      return;
    }

    const seconds = Math.max(
      5,
      this.capacityFor(this.pollCountryId)?.provisioning?.pollAfterSeconds ?? 30,
    );

    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.pollProvisioningOnce();
    }, seconds * 1000);
  }

  private pollProvisioningOnce(): void {
    const countryId = this.pollCountryId;

    if (countryId === null) {
      return;
    }

    if (!this.connectivity.isOnline() || !this.isVisible()) {
      this.scheduleProvisioningPoll();

      return;
    }

    this.api.capacity(countryId).subscribe({
      next: (capacity) => {
        this.capacitiesSignal.update((list) => {
          const index = list.findIndex((entry) => entry.countryId === capacity.countryId);

          if (index < 0) {
            return [...list, capacity];
          }

          const next = [...list];
          next[index] = capacity;

          return next;
        });

        const pending = capacity.provisioning;

        if (pending === null || pending.status === 'completed') {
          this.stopProvisioningPoll();
          this.provisioningSettledSignal.update((count) => count + 1);

          return;
        }

        this.scheduleProvisioningPoll();
      },
      error: () => {
        // A failed tick is not worth a message; the next one retries.
        this.scheduleProvisioningPoll();
      },
    });
  }

  private isVisible(): boolean {
    return globalThis.document?.visibilityState !== 'hidden';
  }

  /** Reads the account's onboarding position, and adopts the manager's formatting preferences from it. */
  loadState(): Observable<OnboardingState> {
    return this.api.state().pipe(
      tap((state) => {
        this.stateSignal.set(state);
        this.adoptPreferences(state.manager);
      }),
    );
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
   * Changes the manager's locale and time zone.
   *
   * The version check happens inside `defer` so that calling this before the profile has loaded produces
   * a failed observable the caller can handle, rather than throwing past their error handling. On success
   * the new values are adopted immediately, so every deadline on screen re-renders in the chosen zone
   * without waiting for the next read (`CAL-4`).
   */
  updateManagerProfile(locale: string, timeZone: string): Observable<ManagerProfile> {
    return defer(() => {
      const version = this.stateSignal()?.manager?.version;

      if (version === undefined) {
        return throwError(
          () =>
            new Error('The manager profile has not been loaded, so there is no version to send.'),
        );
      }

      return this.api.updateManagerProfile({ locale, timeZone }, version).pipe(
        tap((profile) => {
          this.stateSignal.update((state) =>
            state === null ? state : { ...state, manager: profile },
          );
          this.adoptPreferences(profile);
        }),
      );
    });
  }

  /** Applies a manager profile's locale and time zone to every date and amount the screens render. */
  private adoptPreferences(manager: ManagerProfile | null): void {
    configurePresentation({
      locale: manager?.locale ?? null,
      timeZone: manager?.timeZone ?? null,
    });
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
    this.stopProvisioningPoll();
    resetPresentation();
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
