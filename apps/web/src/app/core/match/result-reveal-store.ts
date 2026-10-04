import { Injectable, signal } from '@angular/core';

/** Where the revealed results are remembered on this device. */
export const RESULT_REVEAL_STORAGE_KEY = 'tm.revealed-results';

/** The most results remembered, so the list cannot grow without bound; the oldest are dropped first. */
const MAX_REMEMBERED = 500;

/** The key an inbox message that has no match of its own is remembered under. */
export function messageRevealKey(messageId: string): string {
  return `message:${messageId}`;
}

/** The key a news item that does not name its match is remembered under. */
export function newsRevealKey(newsId: string): string {
  return `news:${newsId}`;
}

/**
 * Which results the manager has chosen to see, or has watched.
 *
 * A result is a spoiler until the manager has seen the match unfold, skipped to its end, or asked for it, so
 * every screen that could give one away reads this before it does. A match is remembered under its own id, so
 * showing a result in the inbox shows it in the viewer too, and watching the match shows it in the inbox. A
 * message that names no match (a table move) is remembered under the message's id.
 *
 * The record is kept on the device, in local storage, and is never sent anywhere: it is a viewing preference,
 * not a game fact, so it needs neither the server nor a migration. Where storage is unavailable — a private
 * window, a blocked site — the results revealed this session stay revealed and nothing throws.
 */
@Injectable({ providedIn: 'root' })
export class ResultRevealStore {
  private readonly keys = signal<readonly string[]>(this.read());

  /** Whether a result has been revealed. Reads a signal, so a template or computed value stays current. */
  isRevealed(key: string | null): boolean {
    return key !== null && this.keys().includes(key);
  }

  /** Reveals a result, and remembers that it was. Revealing one twice changes nothing. */
  reveal(key: string | null): void {
    if (key === null || key.length === 0 || this.keys().includes(key)) {
      return;
    }

    const next = [...this.keys(), key].slice(-MAX_REMEMBERED);

    this.keys.set(next);
    this.write(next);
  }

  private read(): readonly string[] {
    try {
      const stored = globalThis.localStorage?.getItem(RESULT_REVEAL_STORAGE_KEY);
      const parsed: unknown = stored === null || stored === undefined ? [] : JSON.parse(stored);

      return Array.isArray(parsed)
        ? parsed.filter((entry): entry is string => typeof entry === 'string')
        : [];
    } catch {
      return [];
    }
  }

  private write(keys: readonly string[]): void {
    try {
      globalThis.localStorage?.setItem(RESULT_REVEAL_STORAGE_KEY, JSON.stringify(keys));
    } catch {
      // Storage is unavailable: the choice holds for this session and no longer.
    }
  }
}
