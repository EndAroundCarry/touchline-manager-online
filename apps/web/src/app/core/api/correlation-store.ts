import { Injectable, signal } from '@angular/core';

/**
 * Holds the most recent correlation ID the server returned.
 *
 * Support asks the manager for one reference value; showing it in the shell footer means the answer
 * exists without opening developer tools. A signal rather than a subject because it is read state,
 * not an event stream.
 */
@Injectable({ providedIn: 'root' })
export class CorrelationStore {
  private readonly current = signal<string | null>(null);

  /** The most recent correlation ID, or null before the first response. */
  readonly correlationId = this.current.asReadonly();

  /** Records a correlation ID from a response. Ignores null so a missing header cannot clear it. */
  record(correlationId: string | null): void {
    if (correlationId !== null && correlationId.length > 0) {
      this.current.set(correlationId);
    }
  }
}
