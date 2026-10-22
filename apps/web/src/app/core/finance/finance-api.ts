import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { entityTag } from '../notifications/notification-preferences-api';
import { FinanceLedgerPage, FinanceSummary } from './finance.models';
import { SignSponsorPayload, SponsorPage } from './sponsor.models';

/**
 * The finance module's HTTP surface (master plan §10.7).
 *
 * Nothing here names a manager or a club: the server derives the club from the authenticated tenure. The
 * ledger walk is a keyset cursor, so an entry written between two pages cannot shift the window the way an
 * offset would.
 */
@Injectable({ providedIn: 'root' })
export class FinanceApi {
  private readonly api = inject(ApiClient);

  /** Reads the club's money, its weekly commitments, and its season totals by category. */
  getSummary(): Observable<FinanceSummary> {
    return this.api.get<FinanceSummary>('/finances/summary');
  }

  /** Reads one page of the club's ledger, continuing from a cursor. */
  getLedger(cursor: string | null): Observable<FinanceLedgerPage> {
    const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`;

    return this.api.get<FinanceLedgerPage>(`/finances/ledger${query}`);
  }

  /** Reads the club's sponsor offers, the deal it signed, the realistic objective and the deadline. */
  getSponsor(): Observable<SponsorPage> {
    return this.api.get<SponsorPage>('/finances/sponsor');
  }

  /** Signs the season's sponsor deal against the version the manager last read (0 before a deal), so a retry cannot sign twice. */
  signSponsor(payload: SignSponsorPayload, version: number): Observable<SponsorPage> {
    return this.api.post<SponsorPage, SignSponsorPayload>('/finances/sponsor', payload, {
      etag: entityTag(version),
    });
  }
}
