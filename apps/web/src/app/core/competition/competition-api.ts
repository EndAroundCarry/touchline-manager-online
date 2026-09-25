import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  DivisionFixtures,
  DivisionTable,
  FixtureDetail,
  FixtureTeamSheet,
  MyFixtures,
  SaveTeamSheetRequest,
} from './competition.models';

/**
 * The competition module's HTTP surface (master plan §10.5, §11.1).
 *
 * The manager's own club is resolved by the server from their tenure, so none of these calls names a
 * manager or a club for the reads. The team sheet's `version` is the strong entity tag (`CONC-1`,
 * ADR-0009): a read returns it in the body and a save that replaces an existing side sends it back in
 * `If-Match`, so a selection prepared on one device is refused with `412` rather than overwriting a change
 * made on another. The first save carries no tag, because there is nothing to be conditional against.
 */
@Injectable({ providedIn: 'root' })
export class CompetitionApi {
  private readonly api = inject(ApiClient);

  /** Reads the fixtures of the club the caller holds, with the next one named. */
  mine(): Observable<MyFixtures> {
    return this.api.get<MyFixtures>('/fixtures/mine');
  }

  /** Reads one fixture in full. */
  fixture(fixtureId: string): Observable<FixtureDetail> {
    return this.api.get<FixtureDetail>(`/fixtures/${fixtureId}`);
  }

  /** Reads a division's whole fixture calendar. */
  divisionFixtures(divisionId: string): Observable<DivisionFixtures> {
    return this.api.get<DivisionFixtures>(`/divisions/${divisionId}/fixtures`);
  }

  /** Reads a division's league table for the season in progress. */
  divisionTable(divisionId: string): Observable<DivisionTable> {
    return this.api.get<DivisionTable>(`/divisions/${divisionId}/table`);
  }

  /** Reads the caller's club's side for a fixture, prepared or not. */
  teamSheet(fixtureId: string): Observable<FixtureTeamSheet> {
    return this.api.get<FixtureTeamSheet>(`/fixtures/${fixtureId}/team-sheet`);
  }

  /** Prepares or replaces the club's side for a fixture, conditional on its version when one exists. */
  saveTeamSheet(
    fixtureId: string,
    request: SaveTeamSheetRequest,
    etag: string | undefined,
  ): Observable<FixtureTeamSheet> {
    return this.api.put<FixtureTeamSheet, SaveTeamSheetRequest>(
      `/fixtures/${fixtureId}/team-sheet`,
      request,
      etag === undefined ? undefined : { etag },
    );
  }
}
