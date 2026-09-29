import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { NotificationPreferencesApi } from './notification-preferences-api';
import { NotificationPreferencesStore } from './notification-preferences-store';
import {
  NotificationPreferences,
  UpdateNotificationPreferencesRequest,
} from './notification-preferences.models';

function preferences(overrides: Partial<NotificationPreferences> = {}): NotificationPreferences {
  return {
    emailDeadlineReminders: true,
    emailInactivityWarnings: true,
    emailMarketMessages: true,
    emailNewsDigest: true,
    version: 1,
    serverTime: '2026-10-06T19:00:00Z',
    ...overrides,
  };
}

const draft: UpdateNotificationPreferencesRequest = {
  emailDeadlineReminders: false,
  emailInactivityWarnings: true,
  emailMarketMessages: true,
  emailNewsDigest: false,
};

function createApiStub() {
  return { read: vi.fn(), update: vi.fn() };
}

describe('NotificationPreferencesStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: NotificationPreferencesStore;

  beforeEach(() => {
    api = createApiStub();
    TestBed.configureTestingModule({
      providers: [{ provide: NotificationPreferencesApi, useValue: api }],
    });
    store = TestBed.inject(NotificationPreferencesStore);
  });

  it('reads the preferences', () => {
    api.read.mockReturnValue(of(preferences()));

    store.load();

    expect(store.preferences()?.version).toBe(1);
    expect(store.loading()).toBe(false);
  });

  it('saves under the version it read', () => {
    api.read.mockReturnValue(of(preferences({ version: 3 })));
    store.load();
    api.update.mockReturnValue(of(preferences({ version: 4, emailDeadlineReminders: false })));

    store.save(draft);

    expect(api.update).toHaveBeenCalledWith(draft, 3);
    expect(store.preferences()?.version).toBe(4);
    expect(store.saved()).toBe(true);
  });

  it('records a conflict and re-reads when the version is stale', () => {
    api.read.mockReturnValue(of(preferences({ version: 1 })));
    store.load();
    api.update.mockReturnValue(
      throwError(() => new ApiError(412, 'PRECONDITION_FAILED', 'Stale.', null, new Map())),
    );

    store.save(draft);

    expect(store.conflict()).toBe(true);
    expect(store.error()).toBeNull();
    // The conflict re-reads the stored state, so the next save starts from the right version.
    expect(api.read).toHaveBeenCalledTimes(2);
  });

  it('reports a save failure that is not a conflict', () => {
    api.read.mockReturnValue(of(preferences()));
    store.load();
    api.update.mockReturnValue(
      throwError(() => new ApiError(500, 'SERVER_ERROR', 'Boom.', null, new Map())),
    );

    store.save(draft);

    expect(store.error()).toBe('Boom.');
    expect(store.conflict()).toBe(false);
  });

  it('does not save before the preferences have been read', () => {
    store.save(draft);

    expect(api.update).not.toHaveBeenCalled();
  });
});
