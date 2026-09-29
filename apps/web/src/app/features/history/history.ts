import { Component, inject } from '@angular/core';
import {
  movementLabel,
  seasonMovementLabel,
} from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import type { ClubSeasonHistoryEntry } from '../../core/competition/competition.models';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, PAGE_HEADING, STATUS_MESSAGE } from '../../shared/forms/control-styles';

/**
 * The club's season history (master plan §11.1, `PR-4`, `PR-6`).
 *
 * It reads the entries the rollover closed once and never rewrote, so the page shows what happened rather
 * than a projection: the final rank, the movement, and the closing cash and reputation of each finished
 * season, newest first. When the rollover has already placed the club for the next season, the season it is
 * going to is shown above the history, with the word for how it got there rather than a tier comparison the
 * screen would have to infer (§11.3).
 */
@Component({
  selector: 'app-history',
  templateUrl: './history.html',
})
export class SeasonHistory {
  private readonly store = inject(CompetitionStore);

  protected readonly history = this.store.clubHistory;
  protected readonly loading = this.store.historyLoading;
  protected readonly loadError = this.store.historyError;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    this.store.loadMyClubHistory();
  }

  /** Names a finished season's movement, or an empty string when the club stayed put (`PR-4`). */
  protected movement(entry: ClubSeasonHistoryEntry): string {
    return seasonMovementLabel(entry.promoted, entry.relegated);
  }

  /** Names how the club arrived in its next season (`PR-5`). */
  protected nextMovement(movement: string): string {
    return movementLabel(movement);
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
