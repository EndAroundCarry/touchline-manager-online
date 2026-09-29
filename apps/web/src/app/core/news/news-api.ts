import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { NewsPage } from './news.models';

/** The news feed's read (`COM-1`). */
@Injectable({ providedIn: 'root' })
export class NewsApi {
  private readonly api = inject(ApiClient);

  /**
   * Reads a page of the feed, optionally scoped to a division or a country.
   *
   * The filters are query parameters rather than separate routes, because a division's feed and the
   * world's are the same read narrowed, exactly as the server models it.
   */
  list(
    cursor: string | null,
    divisionId: string | null,
    countryId: string | null,
  ): Observable<NewsPage> {
    const query: string[] = [];

    if (cursor !== null) {
      query.push(`cursor=${encodeURIComponent(cursor)}`);
    }

    if (divisionId !== null) {
      query.push(`divisionId=${encodeURIComponent(divisionId)}`);
    }

    if (countryId !== null) {
      query.push(`countryId=${encodeURIComponent(countryId)}`);
    }

    const suffix = query.length > 0 ? `?${query.join('&')}` : '';

    return this.api.get<NewsPage>(`/news${suffix}`);
  }
}
