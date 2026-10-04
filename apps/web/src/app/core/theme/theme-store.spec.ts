import { TestBed } from '@angular/core/testing';
import { THEME_STORAGE_KEY, ThemeStore } from './theme-store';

/**
 * The colour theme (ADR-0059).
 *
 * The behaviour that matters is that the choice reaches the document, survives a reload, and never throws when
 * storage is unavailable, because a manager in a private window must still be able to switch.
 */
describe('ThemeStore', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('app-dark', 'app-light');
    document.head.innerHTML =
      '<meta name="color-scheme" content="dark"><meta name="theme-color" content="#0b0f14">';
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('starts dark when nothing is stored', () => {
    const store = TestBed.inject(ThemeStore);

    expect(store.mode()).toBe('dark');
    expect(document.documentElement.classList.contains('app-dark')).toBe(true);
    expect(document.documentElement.classList.contains('app-light')).toBe(false);
  });

  it('switches the root class, the colour scheme and the browser chrome colour, and remembers the choice', () => {
    const store = TestBed.inject(ThemeStore);

    store.toggle();

    expect(store.mode()).toBe('light');
    expect(document.documentElement.classList.contains('app-light')).toBe(true);
    expect(document.documentElement.classList.contains('app-dark')).toBe(false);
    expect(document.querySelector('meta[name="color-scheme"]')?.getAttribute('content')).toBe(
      'light',
    );
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe(
      '#e6ebf2',
    );
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');

    store.toggle();

    expect(store.mode()).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('adopts the stored choice when the app starts', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'light');

    const store = TestBed.inject(ThemeStore);

    expect(store.mode()).toBe('light');
    expect(document.documentElement.classList.contains('app-light')).toBe(true);
  });

  it('still switches when storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    const store = TestBed.inject(ThemeStore);

    expect(() => store.toggle()).not.toThrow();
    expect(store.mode()).toBe('light');
  });
});
