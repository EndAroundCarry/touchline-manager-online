import { Component, OnInit, computed, effect, inject } from '@angular/core';
import { GameClockStore } from '../../core/devtools/game-clock-store';

/**
 * The non-production season-stepping toolbar (ADR-0049).
 *
 * A slim bar under the header with the current game date and two buttons: step one game day, or jump to the
 * next round's kickoff. It is the visible half of the stepped clock, and it renders only when the API reports
 * one — a real-time, compressed, or production host has no such route, so a manager never sees it.
 *
 * After a step lands and the day's jobs have run, it reloads the page, so whatever screen the manager is
 * looking at shows the new day rather than the previous one.
 */
@Component({
  selector: 'app-game-clock-bar',
  templateUrl: './game-clock-bar.html',
})
export class GameClockBar implements OnInit {
  private readonly store = inject(GameClockStore);

  /** Whether the API reports a stepped clock. */
  protected readonly available = this.store.available;

  /** The current game instant. */
  protected readonly gameNow = this.store.gameNow;

  /** The next round's kickoff, or null. */
  protected readonly nextMatchdayAt = this.store.nextMatchdayAt;

  /** Whether a step is in flight. */
  protected readonly busy = this.store.busy;

  /** The last step's failure, or null. */
  protected readonly error = this.store.error;

  /** The current game instant, rendered in UTC. */
  protected readonly gameNowLabel = computed(() => this.format(this.gameNow()));

  /** The next round's kickoff, rendered in UTC. */
  protected readonly nextMatchdayLabel = computed(() => this.format(this.nextMatchdayAt()));

  constructor() {
    // A completed step is the moment to show the new day, once the day itself has finished processing.
    effect(() => {
      if (this.store.completedTick() > 0) {
        this.reload();
      }
    });
  }

  /** Probes once, so the toolbar knows whether it is enabled and what to show. */
  ngOnInit(): void {
    this.store.probe();
  }

  /** Steps one game day. */
  protected nextDay(): void {
    this.store.advance('day');
  }

  /** Steps to the next round's kickoff. */
  protected nextMatchday(): void {
    this.store.advance('matchday');
  }

  private format(value: string | null): string {
    if (value === null) {
      return '—';
    }

    const formatted = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'UTC',
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(new Date(value));

    return `${formatted} UTC`;
  }

  private reload(): void {
    globalThis.location?.reload();
  }
}
