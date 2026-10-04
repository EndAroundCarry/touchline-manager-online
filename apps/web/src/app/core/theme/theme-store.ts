import { Injectable, signal } from '@angular/core';

/** The two looks the client has. Dark is the default (ADR-0059). */
export type ThemeMode = 'dark' | 'light';

/** The key the choice is kept under on this device. */
export const THEME_STORAGE_KEY = 'touchline.theme';

/** The browser chrome colour for each theme, so the installed app's title bar matches the page behind it. */
const THEME_COLORS: Record<ThemeMode, string> = { dark: '#0b0f14', light: '#e6ebf2' };

/**
 * The colour theme.
 *
 * The choice is a preference of this device, not of the account, so it lives in `localStorage` and nowhere on the
 * server. It is applied as a class on <html>: `app-dark` (which PrimeNG's `darkModeSelector` also reads) or
 * `app-light`, and the tokens in `styles.css` do the rest. An inline script in `index.html` applies the stored choice
 * before the first paint, so a manager who chose light never sees a dark flash; this store then adopts the same value.
 *
 * Storage can be unavailable (a private window, blocked site data), so every access is guarded and the theme simply
 * does not persist.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly modeSignal = signal<ThemeMode>(readStoredMode());

  /** The theme in force. */
  readonly mode = this.modeSignal.asReadonly();

  constructor() {
    applyMode(this.modeSignal());
  }

  /** Switches between dark and light. */
  toggle(): void {
    this.set(this.modeSignal() === 'dark' ? 'light' : 'dark');
  }

  /** Chooses a theme and keeps the choice on this device. */
  set(mode: ThemeMode): void {
    this.modeSignal.set(mode);
    applyMode(mode);

    try {
      localStorage.setItem(THEME_STORAGE_KEY, mode);
    } catch {
      // Not persisted; the choice still holds for this visit.
    }
  }
}

/** Reads the stored choice, defaulting to dark when there is none or storage is unavailable. */
function readStoredMode(): ThemeMode {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : 'dark';
  } catch {
    return 'dark';
  }
}

/** Puts the theme on the document: the root class, the form-control colour scheme and the browser chrome colour. */
function applyMode(mode: ThemeMode): void {
  if (typeof document === 'undefined') {
    return;
  }

  const root = document.documentElement;

  root.classList.toggle('app-dark', mode === 'dark');
  root.classList.toggle('app-light', mode === 'light');

  document.querySelector('meta[name="color-scheme"]')?.setAttribute('content', mode);
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLORS[mode]);
}
