import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { CalendarWindow } from './calendar.models';

/**
 * The calendar's HTTP surface.
 *
 * Nothing here names a club: the server derives it from the authenticated tenure. With no bounds the server
 * answers with the game clock's month, which is how the screen learns what day it is.
 */
@Injectable({ providedIn: 'root' })
export class CalendarApi {
  private readonly api = inject(ApiClient);

  /** Reads the events in `[from, to)`, or the game clock's month when neither is given. */
  window(from: string | null, to: string | null): Observable<CalendarWindow> {
    const query =
      from === null || to === null
        ? ''
        : `?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;

    return this.api.get<CalendarWindow>(`/calendar${query}`);
  }
}
