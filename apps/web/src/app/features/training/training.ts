import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { positionLabel, stateBand } from '../../core/squad/squad-presentation';
import {
  effectiveDateLabel,
  intensityLabel,
  teamFocusLabel,
} from '../../core/training/training-presentation';
import { TrainingStore } from '../../core/training/training-store';
import { preferredLocale } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The training screen (master plan §11.1, F-20).
 *
 * Two decisions and the squad they apply to. The club's team focus and intensity are one plan under the
 * version that stops two devices overwriting each other (`CONC-1`); each player's individual focus is a
 * single value written on its own row. Condition and fatigue carry their band word beside the number, so
 * colour is decoration rather than the message (§11.3), and every focus control is a labelled select rather
 * than a drag, so the screen works from a keyboard and a screen reader.
 *
 * A refused save is handled the way §11.2 requires: a `412` keeps the manager's choices, reloads the
 * server's state, and offers an explicit reapply instead of overwriting the change that won.
 */
@Component({
  selector: 'app-training',
  imports: [RouterLink],
  templateUrl: './training.html',
})
export class Training {
  private readonly store = inject(TrainingStore);

  protected readonly training = this.store.training;
  protected readonly draft = this.store.draft;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.loadError;
  protected readonly saving = this.store.saving;
  protected readonly saveError = this.store.saveError;
  protected readonly savedMessage = this.store.savedMessage;
  protected readonly hasConflict = this.store.hasConflict;
  protected readonly focusError = this.store.focusError;
  protected readonly savingFocusPlayerId = this.store.savingFocusPlayerId;
  protected readonly teamFocusOptions = this.store.teamFocusOptions;
  protected readonly intensityOptions = this.store.intensityOptions;
  protected readonly focusOptions = this.store.focusOptions;
  protected readonly isDirty = this.store.isDirty;

  /** The squad in the order the server returns it: goalkeepers first, then by name. */
  protected readonly players = computed(() => this.training()?.players ?? []);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    this.store.load();
  }

  /** Changes the club-wide focus as the manager picks (`TRN-1`). */
  protected onTeamFocusChange(event: Event): void {
    this.store.setTeamFocus((event.target as HTMLSelectElement).value);
  }

  /** Changes the training intensity. */
  protected onIntensityChange(event: Event): void {
    this.store.setIntensity((event.target as HTMLSelectElement).value);
  }

  /** Sets, changes, or clears one player's individual focus (`TRN-2`). */
  protected onFocusChange(playerId: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.store.setPlayerFocus(playerId, value.length === 0 ? null : value);
  }

  /** Saves, or creates the plan when the club has never set one. */
  protected save(): void {
    this.store.save();
  }

  /** Re-applies the draft after a conflict (`§11.2`). */
  protected reapply(): void {
    this.store.reapply();
  }

  /** Adopts the server's state after a conflict. */
  protected discardConflict(): void {
    this.store.discardConflict();
  }

  /** Names a club-wide focus. */
  protected focusLabel(code: string): string {
    return teamFocusLabel(code);
  }

  /** Names an intensity. */
  protected intensityName(code: string): string {
    return intensityLabel(code);
  }

  /** Renders the effective date in the viewer's locale. */
  protected effective(isoDate: string): string {
    return effectiveDateLabel(isoDate, preferredLocale());
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** The tint for a state value where higher is better. */
  protected conditionClass(value: number): string {
    return stateBand(value, true).className;
  }

  /** The band word for a state value where higher is better. */
  protected conditionWord(value: number): string {
    return stateBand(value, true).label;
  }

  /** The tint for a state value where lower is better, which is fatigue. */
  protected fatigueClass(value: number): string {
    return stateBand(value, false).className;
  }

  /** The band word for a state value where lower is better. */
  protected fatigueWord(value: number): string {
    return stateBand(value, false).label;
  }
}
