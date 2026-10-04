import { TestBed } from '@angular/core/testing';
import {
  messageRevealKey,
  RESULT_REVEAL_STORAGE_KEY,
  ResultRevealStore,
} from './result-reveal-store';

/**
 * Which results the manager has seen.
 *
 * What matters is that nothing is revealed until it is asked for, that a reveal survives a reload, and that a
 * device whose storage is unavailable still works for the session.
 */
describe('ResultRevealStore', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('hides every result until one is revealed', () => {
    const store = TestBed.inject(ResultRevealStore);

    expect(store.isRevealed('match-1')).toBe(false);
    expect(store.isRevealed(null)).toBe(false);
  });

  it('reveals one match and no other, and remembers it across a reload', () => {
    const store = TestBed.inject(ResultRevealStore);

    store.reveal('match-1');

    expect(store.isRevealed('match-1')).toBe(true);
    expect(store.isRevealed('match-2')).toBe(false);

    TestBed.resetTestingModule();

    expect(TestBed.inject(ResultRevealStore).isRevealed('match-1')).toBe(true);
  });

  it('keeps a message that names no match apart from every match', () => {
    const store = TestBed.inject(ResultRevealStore);

    store.reveal(messageRevealKey('abc'));

    expect(store.isRevealed(messageRevealKey('abc'))).toBe(true);
    expect(store.isRevealed('abc')).toBe(false);
  });

  it('reveals a result once however often it is asked', () => {
    const store = TestBed.inject(ResultRevealStore);

    store.reveal('match-1');
    store.reveal('match-1');

    expect(JSON.parse(localStorage.getItem(RESULT_REVEAL_STORAGE_KEY) ?? '[]')).toEqual([
      'match-1',
    ]);
  });

  it('forgets the oldest results first once it holds too many', () => {
    const store = TestBed.inject(ResultRevealStore);

    for (let index = 0; index < 505; index++) {
      store.reveal(`match-${index}`);
    }

    expect(store.isRevealed('match-0')).toBe(false);
    expect(store.isRevealed('match-504')).toBe(true);
  });

  it('ignores a stored value it cannot read', () => {
    localStorage.setItem(RESULT_REVEAL_STORAGE_KEY, '{not json');

    expect(TestBed.inject(ResultRevealStore).isRevealed('match-1')).toBe(false);

    TestBed.resetTestingModule();
    localStorage.setItem(RESULT_REVEAL_STORAGE_KEY, JSON.stringify({ match: 1 }));

    expect(TestBed.inject(ResultRevealStore).isRevealed('match-1')).toBe(false);
  });

  it('still reveals for the session when storage refuses to write', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    const store = TestBed.inject(ResultRevealStore);

    expect(() => store.reveal('match-1')).not.toThrow();
    expect(store.isRevealed('match-1')).toBe(true);
  });
});
