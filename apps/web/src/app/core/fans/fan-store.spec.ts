import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { FanApi } from './fan-api';
import { FanStore } from './fan-store';
import { fanBase } from './fan-test-data';

describe('FanStore', () => {
  let api: { get: ReturnType<typeof vi.fn> };
  let store: FanStore;

  beforeEach(() => {
    api = { get: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: FanApi, useValue: api }] });
    store = TestBed.inject(FanStore);
  });

  it('reads the fans with the shorter history unless asked for the whole club', () => {
    api.get.mockReturnValue(of(fanBase()));

    store.load();

    expect(api.get).toHaveBeenCalledWith('tenure');
    expect(store.fans()?.clubName).toBe('Harbour Town');
    expect(store.nextFixture()?.expectation.labelPhrase).toBe('Should win');
    expect(store.loading()).toBe(false);
  });

  it('keeps the page on screen while a different history is read', () => {
    api.get.mockReturnValue(of(fanBase()));
    store.load();

    const pending = new Subject<ReturnType<typeof fanBase>>();
    api.get.mockReturnValue(pending);
    store.load('club', false);

    expect(api.get).toHaveBeenLastCalledWith('club');
    expect(store.loading()).toBe(false);
    expect(store.fans()).not.toBeNull();
  });

  it('drops a slow answer for a history the manager has already left', () => {
    const slow = new Subject<ReturnType<typeof fanBase>>();
    api.get.mockReturnValueOnce(slow);
    store.load('club');

    api.get.mockReturnValueOnce(of(fanBase({ history: 'tenure', fans: 10_450 })));
    store.load('tenure');

    slow.next(fanBase({ history: 'club', fans: 1 }));

    expect(store.fans()?.history).toBe('tenure');
    expect(store.fans()?.fans).toBe(10_450);
  });

  it('reports a refused read rather than showing an empty club', () => {
    api.get.mockReturnValue(
      throwError(
        () => new ApiError(403, 'NO_CLUB', 'You do not manage a club yet.', null, new Map()),
      ),
    );

    store.load();

    expect(store.error()).toBe('You do not manage a club yet.');
    expect(store.fans()).toBeNull();
    expect(store.loading()).toBe(false);
  });

  it('forgets everything when the session ends', () => {
    api.get.mockReturnValue(of(fanBase()));
    store.load();

    store.clear();

    expect(store.fans()).toBeNull();
    expect(store.nextFixture()).toBeNull();
    expect(store.error()).toBeNull();
  });
});
