import { Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiError } from '../api/api-error';
import { CalendarApi } from './calendar-api';
import { CalendarWindow } from './calendar.models';

/**
 * The calendar's view state.
 *
 * Holds one window of events at a time, the one the screen is drawing. Moving to a month the window does not
 * cover reads that month; moving within it costs nothing. A read that is still in flight when the next one
 * starts is dropped, so a slow answer for a month the manager has already left can never overwrite the month
 * they are looking at.
 */
@Injectable({ providedIn: 'root' })
export class CalendarStore {
  private readonly api = inject(CalendarApi);

  private readonly windowSignal = signal<CalendarWindow | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  private inFlight: Subscription | null = null;

  /** The window last read, or null before the first read. */
  readonly window = this.windowSignal.asReadonly();

  /** Whether a read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed, or null. */
  readonly error = this.errorSignal.asReadonly();

  /**
   * Reads a window. With no bounds the server answers with the game clock's month, which is how the screen
   * learns what day it is before it knows which month to draw.
   */
  load(from: string | null = null, to: string | null = null): void {
    this.inFlight?.unsubscribe();
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    this.inFlight = this.api.window(from, to).subscribe({
      next: (window) => {
        this.windowSignal.set(window);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your calendar could not be loaded.',
        );
      },
    });
  }

  /** Reads `[from, to)` unless the window already held covers it. */
  ensure(from: string, to: string): void {
    const held = this.windowSignal();

    if (
      held !== null &&
      Date.parse(held.from) <= Date.parse(from) &&
      Date.parse(held.to) >= Date.parse(to)
    ) {
      return;
    }

    this.load(from, to);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.windowSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }
}
