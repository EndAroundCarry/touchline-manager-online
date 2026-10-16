import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { CalendarApi } from './calendar-api';
import { CalendarStore } from './calendar-store';
import { CalendarWindow } from './calendar.models';

function window(from: string, to: string, serverTime = '2026-10-17T12:00:00Z'): CalendarWindow {
  return { from, to, events: [], serverTime };
}

describe('CalendarStore', () => {
  let api: { window: ReturnType<typeof vi.fn> };
  let store: CalendarStore;

  beforeEach(() => {
    api = { window: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: CalendarApi, useValue: api }] });
    store = TestBed.inject(CalendarStore);
  });

  it('reads the default window when given no bounds', () => {
    api.window.mockReturnValue(of(window('2026-09-23T00:00:00Z', '2026-11-09T00:00:00Z')));

    store.load();

    expect(api.window).toHaveBeenCalledWith(null, null);
    expect(store.window()?.serverTime).toBe('2026-10-17T12:00:00Z');
    expect(store.loading()).toBe(false);
  });

  it('does not read again for a range the held window covers', () => {
    api.window.mockReturnValue(of(window('2026-09-23T00:00:00Z', '2026-11-09T00:00:00Z')));
    store.load();
    api.window.mockClear();

    store.ensure('2026-09-28T00:00:00Z', '2026-11-02T00:00:00Z');

    expect(api.window).not.toHaveBeenCalled();
  });

  it('reads a range the held window does not cover', () => {
    api.window.mockReturnValue(of(window('2026-09-23T00:00:00Z', '2026-11-09T00:00:00Z')));
    store.load();
    api.window.mockReturnValue(of(window('2026-10-26T00:00:00Z', '2026-12-08T00:00:00Z')));

    store.ensure('2026-10-26T00:00:00Z', '2026-12-08T00:00:00Z');

    expect(api.window).toHaveBeenLastCalledWith('2026-10-26T00:00:00Z', '2026-12-08T00:00:00Z');
    expect(store.window()?.from).toBe('2026-10-26T00:00:00Z');
  });

  it('drops a slow answer for a window the manager has already left', () => {
    const slow = new Subject<CalendarWindow>();

    api.window.mockReturnValueOnce(slow);
    store.load('2026-08-01T00:00:00Z', '2026-09-01T00:00:00Z');

    api.window.mockReturnValueOnce(of(window('2026-10-01T00:00:00Z', '2026-11-01T00:00:00Z')));
    store.load('2026-10-01T00:00:00Z', '2026-11-01T00:00:00Z');

    slow.next(window('2026-08-01T00:00:00Z', '2026-09-01T00:00:00Z'));

    expect(store.window()?.from).toBe('2026-10-01T00:00:00Z');
  });

  it('reports why a read failed, and keeps the window it had', () => {
    api.window.mockReturnValueOnce(of(window('2026-09-23T00:00:00Z', '2026-11-09T00:00:00Z')));
    store.load();
    api.window.mockReturnValueOnce(
      throwError(
        () => new ApiError(403, 'NO_CLUB', 'You do not manage a club yet.', null, new Map()),
      ),
    );

    store.load('2026-12-01T00:00:00Z', '2027-01-01T00:00:00Z');

    expect(store.error()).toBe('You do not manage a club yet.');
    expect(store.loading()).toBe(false);
    expect(store.window()).not.toBeNull();
  });

  it('forgets everything when the session ends', () => {
    api.window.mockReturnValue(of(window('2026-09-23T00:00:00Z', '2026-11-09T00:00:00Z')));
    store.load();

    store.clear();

    expect(store.window()).toBeNull();
    expect(store.error()).toBeNull();
  });
});
