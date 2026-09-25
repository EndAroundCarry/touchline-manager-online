import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  fixtureStatusLabel,
  lockCountdown,
  roundLabel,
  teamSheetIssueMessage,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { TeamSheetIssue, TeamSheetSlot } from '../../core/competition/competition.models';
import { positionLabel } from '../../core/squad/squad-presentation';
import { familyLabel, roleLabel } from '../../core/tactics/tactics-presentation';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

/**
 * The prepare-match screen (master plan §11.1, F-19).
 *
 * The opponent, the deadline, and the side. The eleven starting slots take their shape from the club's
 * default plan; the bench is seven more places. Every slot is a labelled select rather than a drag, so the
 * screen is reachable from a keyboard and a screen reader (`§11.3`), and a refused save lists the
 * validator's issues against the slots they concern.
 *
 * When the fixture has locked the controls are disabled and the screen says so — the deadline is the
 * deadline whether or not the server has rewritten the fixture's status yet (`CAL-3`). When the club has no
 * default plan there is nothing to prepare from, so the screen points at the tactics screen instead.
 */
@Component({
  selector: 'app-prepare',
  imports: [RouterLink],
  templateUrl: './prepare.html',
})
export class Prepare implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CompetitionStore);
  private readonly timer: ReturnType<typeof setInterval>;

  /** The clock the countdown is measured against, advanced on an interval. */
  private readonly nowSignal = signal(new Date());

  protected readonly teamSheet = this.store.teamSheet;
  protected readonly selection = this.store.selection;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.loadError;
  protected readonly saving = this.store.saving;
  protected readonly saveError = this.store.saveError;
  protected readonly savedMessage = this.store.savedMessage;
  protected readonly hasConflict = this.store.hasConflict;
  protected readonly validation = this.store.validation;
  protected readonly isDirty = this.store.isDirty;
  protected readonly starterSlots = this.store.starterSlots;
  protected readonly substituteSlots = this.store.substituteSlots;
  protected readonly selectedStarterCount = this.store.selectedStarterCount;

  /** Every player who may be selected. */
  protected readonly selectable = computed(() => this.teamSheet()?.selectablePlayers ?? []);

  /** Whether the fixture's side can no longer be changed. */
  protected readonly isLocked = computed(() => this.teamSheet()?.isLocked ?? true);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  constructor() {
    const fixtureId = this.route.snapshot.paramMap.get('fixtureId');

    if (fixtureId !== null && fixtureId.length > 0) {
      this.store.loadTeamSheet(fixtureId);
    }

    this.timer = setInterval(() => this.nowSignal.set(new Date()), 30_000);
  }

  /** Stops the countdown's interval so no timer outlives the screen. */
  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  /** The player currently picked for a slot, or an empty string when it is empty. */
  protected selected(slotNumber: number): string {
    return this.selection().get(slotNumber) ?? '';
  }

  /** Records a slot's pick. An empty value empties the slot. */
  protected onSelect(slotNumber: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    if (value.length === 0) {
      this.store.clearSlot(slotNumber);

      return;
    }

    this.store.assign(slotNumber, value);
  }

  /** Saves the side, creating the club's sheet for this fixture when it has none. */
  protected save(): void {
    this.store.save();
  }

  /** Re-applies the selection after a conflict (`§11.2`). */
  protected reapply(): void {
    this.store.reapply();
  }

  /** Adopts the server's side after a conflict. */
  protected discardConflict(): void {
    this.store.discardConflict();
  }

  /** Formats a kickoff or deadline in the viewer's local time (`CAL-4`). */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** How long until the sheet locks. */
  protected countdown(value: string): string {
    return lockCountdown(value, this.nowSignal());
  }

  /** Names the club's side. */
  protected venue(value: string): string {
    return venueLabel(value);
  }

  /** Names a fixture's lifecycle state. */
  protected status(code: string): string {
    return fixtureStatusLabel(code);
  }

  /** A round's label. */
  protected round(roundNumber: number): string {
    return roundLabel(roundNumber);
  }

  /** Names a slot's position family. */
  protected family(code: string | null): string {
    return code === null ? '' : familyLabel(code);
  }

  /** Names a slot's role. */
  protected role(code: string | null): string {
    return code === null ? '' : roleLabel(code);
  }

  /** Names a player's position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** Describes a bench slot by its place on the bench rather than by a pitch position. */
  protected benchLabel(slot: TeamSheetSlot): string {
    return `Substitute ${slot.slotNumber - this.starterSlots().length}`;
  }

  /** The validator issue concerning a slot, if any. */
  protected issueFor(slotNumber: number): TeamSheetIssue | null {
    return this.validation()?.issues.find((issue) => issue.slotNumber === slotNumber) ?? null;
  }

  /** Words for one validator issue. */
  protected issueMessage(issue: TeamSheetIssue): string {
    return teamSheetIssueMessage(issue);
  }
}
