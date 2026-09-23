import { Injectable, signal } from '@angular/core';

/**
 * Tracks whether the browser believes it has a network connection.
 *
 * The product's offline rule is deliberately narrow: reading cached data is allowed, mutations are
 * not (ADR-0007). Surfacing this as a signal lets the shell show a stale/offline banner and lets
 * command forms disable themselves with a stated reason instead of failing silently.
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityStore {
  private readonly online = signal(globalThis.navigator?.onLine ?? true);

  /** Whether the browser reports a connection. */
  readonly isOnline = this.online.asReadonly();

  constructor() {
    globalThis.addEventListener?.('online', () => this.online.set(true));
    globalThis.addEventListener?.('offline', () => this.online.set(false));
  }
}
