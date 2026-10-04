import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { positionLabel, stateBand } from '../../core/squad/squad-presentation';
import {
  ATTRIBUTE_FAMILY_COLUMNS,
  WEIGHT_STYLES,
  effectiveDateLabel,
  intensityLabel,
  programmeChoices,
  programmeWeightGroups,
} from '../../core/training/training-presentation';
import { TrainingStore } from '../../core/training/training-store';
import { preferredLocale } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The training screen (master plan §11.1, F-20).
 *
 * Two decisions and the squad they apply to. The club's intensity is the plan, under the version that stops
 * two devices overwriting each other (`CONC-1`); what each player trains is a programme, chosen per player
 * and written on its own row. The squad is an attribute table, so the manager sees the effect of a choice
 * where it lands: the attributes the player's programme trains are tinted by weight, and the weight is also
 * a mark and a read-out, so colour is never the only signal (ADR-0039). Condition and fatigue carry their
 * band word beside the number (§11.3), and every programme control is a labelled select rather than a drag,
 * so the screen works from a keyboard and a screen reader.
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
  private readonly maintenance = inject(MaintenanceStore);

  protected readonly training = this.store.training;
  protected readonly draft = this.store.draft;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.loadError;
  protected readonly saving = this.store.saving;
  protected readonly saveError = this.store.saveError;
  protected readonly savedMessage = this.store.savedMessage;
  protected readonly hasConflict = this.store.hasConflict;
  protected readonly programmeError = this.store.programmeError;
  protected readonly savingProgrammePlayerId = this.store.savingProgrammePlayerId;
  protected readonly intensityOptions = this.store.intensityOptions;
  protected readonly isDirty = this.store.isDirty;

  /** The squad in the order the server returns it, as table rows: goalkeepers first, then by name. */
  protected readonly rows = this.store.rows;

  /** Whether a write is allowed; offline or read-only the plan stays editable but nothing is sent (§11.4). */
  protected readonly canMutate = this.maintenance.canMutate;

  /** The programmes a manager may choose, after the position default. */
  protected readonly programmeOptions = computed(() => programmeChoices(this.store.programmes()));

  /** The catalogue as the "what each programme trains" list reads it: skills grouped by weight. */
  protected readonly programmeList = computed(() =>
    this.store.programmes().map((programme) => ({
      code: programme.code,
      label: programme.label,
      description: programme.description,
      groups: programmeWeightGroups(programme),
    })),
  );

  /** The three weights, for the legend. */
  protected readonly weightStyles = WEIGHT_STYLES;

  /** The attribute families and their columns, for the table's group header. */
  protected readonly families = ATTRIBUTE_FAMILY_COLUMNS;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    this.store.load();
  }

  /** Changes the training intensity. */
  protected onIntensityChange(event: Event): void {
    this.store.setIntensity((event.target as HTMLSelectElement).value);
  }

  /** Sets, changes, or clears one player's programme; the empty value is the position default (`TRN-1`). */
  protected onProgrammeChange(playerId: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.store.setPlayerProgramme(playerId, value.length === 0 ? null : value);
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
