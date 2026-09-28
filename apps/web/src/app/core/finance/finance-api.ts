import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { FinanceLedgerPage, FinanceSummary } from './finance.models';

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
}
