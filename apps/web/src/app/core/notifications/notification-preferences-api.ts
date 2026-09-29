import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import {
  NotificationPreferences,
  UpdateNotificationPreferencesRequest,
} from './notification-preferences.models';

/**
 * The notification-preferences read and change (`COM-4`).
 *
 * The change is conditional on the version the manager last read (`CONC-1`): the entity tag is built
 * from the body's `version`, which the server returns on both the read and the write, so no header has
 * to be threaded through the transport.
 */
@Injectable({ providedIn: 'root' })
export class NotificationPreferencesApi {
  private readonly api = inject(ApiClient);

  /** Reads the preferences. */
  read(): Observable<NotificationPreferences> {
    return this.api.get<NotificationPreferences>('/settings/notifications');
  }

  /** Changes the preferences under the version the caller last read. */
  update(
    payload: UpdateNotificationPreferencesRequest,
    version: number,
  ): Observable<NotificationPreferences> {
    return this.api.put<NotificationPreferences, UpdateNotificationPreferencesRequest>(
      '/settings/notifications',
      payload,
      { etag: entityTag(version) },
    );
  }
}

/** Formats a version as the strong entity tag the server expects in `If-Match` (`CONC-1`). */
export function entityTag(version: number): string {
  return `"${version}"`;
}
