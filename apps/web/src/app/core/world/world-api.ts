import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  AvailableClubs,
  ChangeClubColoursPayload,
  ClaimClubPayload,
  ClubColours,
  ClubDashboard,
  CountryCapacity,
  CountrySummary,
  CreateManagerProfilePayload,
  ManagerProfile,
  OnboardingState,
  UpdateManagerProfilePayload,
  WorldSummary,
} from './world.models';

/**
 * The world and onboarding module's HTTP surface (master plan §10.2).
 *
 * Stateless reads and the two onboarding commands live here. The claim carries an idempotency key,
 * because a claim that ran twice would give one manager two clubs — or one club two managers — and a
 * retried request after a timeout is indistinguishable from a double click without one.
 */
@Injectable({ providedIn: 'root' })
export class WorldApi {
  private readonly api = inject(ApiClient);

  /** Reads the world the manager is onboarding into. Answers 404 before the world has been seeded. */
  world(): Observable<WorldSummary> {
    return this.api.get<WorldSummary>('/world');
  }

  /** Lists the countries a manager may join. */
  countries(): Observable<readonly CountrySummary[]> {
    return this.api.get<readonly CountrySummary[]>('/countries');
  }

  /** Measures a country's lowest active tier and its next-tier generation. */
  capacity(countryId: string): Observable<CountryCapacity> {
    return this.api.get<CountryCapacity>(`/countries/${countryId}/capacity`);
  }

  /** Lists the clubs of a country's lowest active tier, with availability. */
  availableClubs(countryId: string): Observable<AvailableClubs> {
    return this.api.get<AvailableClubs>(`/countries/${countryId}/available-clubs`);
  }

  /** Creates the account's manager profile. Idempotent: a repeat returns the existing profile. */
  createManagerProfile(payload: CreateManagerProfilePayload): Observable<ManagerProfile> {
    return this.api.post<ManagerProfile, CreateManagerProfilePayload>('/manager-profile', payload);
  }

  /**
   * Changes the manager's locale and time zone under an optimistic concurrency check.
   *
   * The version the client last read travels as the strong entity tag, so a change made on one device
   * cannot silently overwrite one made on another (`CONC-1`).
   */
  updateManagerProfile(
    payload: UpdateManagerProfilePayload,
    version: number,
  ): Observable<ManagerProfile> {
    return this.api.patch<ManagerProfile, UpdateManagerProfilePayload>(
      '/manager-profile',
      payload,
      {
        etag: `"${version}"`,
      },
    );
  }

  /** Takes over a club, under the given idempotency key. */
  claimClub(clubId: string, idempotencyKey: string): Observable<ClubDashboard> {
    const payload: ClaimClubPayload = { clubId };

    return this.api
      .postWithResponse<ClubDashboard, ClaimClubPayload>('/club-claims', payload, {
        idempotencyKey,
      })
      .pipe(
        map((response) => {
          if (response.body === null) {
            throw new Error('The claim response was empty.');
          }

          return response.body;
        }),
      );
  }

  /** Chooses the two colours the manager's club plays in. */
  changeClubColours(payload: ChangeClubColoursPayload): Observable<ClubColours> {
    return this.api.put<ClubColours, ChangeClubColoursPayload>('/club-tenure/colours', payload);
  }

  /** Resigns from the manager's club. */
  resign(): Observable<OnboardingState> {
    return this.api.post<OnboardingState, Record<string, never>>('/club-tenure/resign', {});
  }

  /** Reads the manager profile and current club in one request. */
  state(): Observable<OnboardingState> {
    return this.api.get<OnboardingState>('/club-tenure');
  }

  /** Reads a club's inherited dashboard. */
  dashboard(clubId: string): Observable<ClubDashboard> {
    return this.api.get<ClubDashboard>(`/clubs/${clubId}/dashboard`);
  }
}
