import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { CompetitionApi } from './competition-api';
import {
  FixtureTeamSheet,
  MyFixtures,
  SaveTeamSheetRequest,
  TeamSheetSlot,
  TeamSheetValidation,
} from './competition.models';

/**
 * The competition module's view state (master plan §11.1).
 *
 * A feature-scoped store following `TrainingStore`. It holds the manager's fixture list and, for the
 * prepare-match screen, the read response plus an editable *selection* of slot-to-player, so the screen
 * never edits a response object and "is there anything to save" is one comparison.
 *
 * The concurrency contract: a save that replaces an existing side sends the sheet's `version` in `If-Match`
 * (a first save sends none, because there is nothing to be conditional against). A `412` keeps the
 * manager's selection, reloads the server's state, and offers an explicit reapply (§11.2). A refused
 * selection arrives as `TEAM_SHEET_VALIDATION_FAILED` with the issues the screen points at.
 */
@Injectable({ providedIn: 'root' })
export class CompetitionStore {
  private readonly api = inject(CompetitionApi);

  private readonly fixturesSignal = signal<MyFixtures | null>(null);
  private readonly fixturesLoadingSignal = signal(false);
  private readonly fixturesErrorSignal = signal<string | null>(null);

  private readonly teamSheetSignal = signal<FixtureTeamSheet | null>(null);
  private readonly selectionSignal = signal<ReadonlyMap<number, string>>(new Map());
  private readonly loadingSignal = signal(false);
  private readonly loadErrorSignal = signal<string | null>(null);
  private readonly savingSignal = signal(false);
  private readonly saveErrorSignal = signal<string | null>(null);
  private readonly savedMessageSignal = signal<string | null>(null);
  private readonly conflictSignal = signal(false);
  private readonly validationSignal = signal<TeamSheetValidation | null>(null);

  /** The manager's club's fixture list last read. */
  readonly fixtures = this.fixturesSignal.asReadonly();

  /** Whether the fixture list is being read. */
  readonly fixturesLoading = this.fixturesLoadingSignal.asReadonly();

  /** Why the fixture list could not be read. */
  readonly fixturesError = this.fixturesErrorSignal.asReadonly();

  /** The prepared side last read. */
  readonly teamSheet = this.teamSheetSignal.asReadonly();

  /** The edited selection, keyed by slot number. */
  readonly selection = this.selectionSignal.asReadonly();

  /** Whether the prepare screen is reading the server. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the prepare screen's read failed. */
  readonly loadError = this.loadErrorSignal.asReadonly();

  /** Whether the selection is being saved. */
  readonly saving = this.savingSignal.asReadonly();

  /** Why the last save failed. */
  readonly saveError = this.saveErrorSignal.asReadonly();

  /** The last success, so the screen can confirm it. */
  readonly savedMessage = this.savedMessageSignal.asReadonly();

  /** Whether the sheet changed underneath the client, so an explicit reapply is needed (`CONC-1`). */
  readonly hasConflict = this.conflictSignal.asReadonly();

  /** Why the last selection was refused, drawn on the slots it concerns (`SQ-4`). */
  readonly validation = this.validationSignal.asReadonly();

  /** The club's eleven starting slots, in slot order. */
  readonly starterSlots = computed(() => this.slots('starter'));

  /** The up-to-seven substitute slots, in slot order. */
  readonly substituteSlots = computed(() => this.slots('substitute'));

  /** How many starting slots currently name a player, so the screen can warn before a save. */
  readonly selectedStarterCount = computed(() => {
    const selection = this.selectionSignal();
    const slots = this.teamSheetSignal()?.slots ?? [];

    return slots.filter((slot) => slot.designation === 'starter' && selection.has(slot.slotNumber))
      .length;
  });

  /** Whether the selection differs from the stored one, so there is something to save. */
  readonly isDirty = computed(() => {
    const teamSheet = this.teamSheetSignal();

    if (teamSheet === null) {
      return false;
    }

    const stored = new Map(
      teamSheet.slots
        .filter((slot) => slot.player !== null)
        .map((slot) => [slot.slotNumber, slot.player!.id]),
    );
    const selection = this.selectionSignal();

    if (stored.size !== selection.size) {
      return true;
    }

    for (const [slotNumber, playerId] of selection) {
      if (stored.get(slotNumber) !== playerId) {
        return true;
      }
    }

    return false;
  });

  /** Reads the manager's club's fixture list. */
  loadFixtures(): void {
    this.fixturesLoadingSignal.set(true);
    this.fixturesErrorSignal.set(null);

    this.api.mine().subscribe({
      next: (fixtures) => {
        this.fixturesSignal.set(fixtures);
        this.fixturesLoadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.fixturesLoadingSignal.set(false);
        this.fixturesErrorSignal.set(
          error instanceof ApiError ? error.detail : 'Your fixtures could not be loaded.',
        );
      },
    });
  }

  /** Reads the club's prepared side for a fixture and starts editing it. */
  loadTeamSheet(fixtureId: string): void {
    this.loadingSignal.set(true);
    this.loadErrorSignal.set(null);

    this.api.teamSheet(fixtureId).subscribe({
      next: (teamSheet) => {
        this.teamSheetSignal.set(teamSheet);
        this.selectionSignal.set(selectionOf(teamSheet));
        this.loadingSignal.set(false);
        this.clearMessages();
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.loadErrorSignal.set(
          error instanceof ApiError ? error.detail : 'Your team sheet could not be loaded.',
        );
      },
    });
  }

  /**
   * Puts a player in a slot, moving them out of the slot they were in.
   *
   * A player occupies one place, so assigning one who is already picked is a move rather than a duplicate
   * the server would refuse (`SQ-4`).
   */
  assign(slotNumber: number, playerId: string): void {
    const selection = new Map(this.selectionSignal());

    for (const [number, assigned] of selection) {
      if (assigned === playerId && number !== slotNumber) {
        selection.delete(number);
      }
    }

    selection.set(slotNumber, playerId);

    this.edit(selection);
  }

  /** Empties a slot. */
  clearSlot(slotNumber: number): void {
    const selection = new Map(this.selectionSignal());

    selection.delete(slotNumber);

    this.edit(selection);
  }

  /** Saves the selection, creating the club's sheet for the fixture when it has none. */
  save(): void {
    const teamSheet = this.teamSheetSignal();

    if (teamSheet === null || this.savingSignal()) {
      return;
    }

    const created = teamSheet.sheetVersion === null;
    const etag = created ? undefined : `"${teamSheet.sheetVersion}"`;

    const request: SaveTeamSheetRequest = {
      selection: [...this.selectionSignal()]
        .map(([slotNumber, playerId]) => ({ slotNumber, playerId }))
        .sort((left, right) => left.slotNumber - right.slotNumber),
    };

    this.savingSignal.set(true);
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.validationSignal.set(null);

    this.api.saveTeamSheet(teamSheet.fixtureId, request, etag).subscribe({
      next: (saved) => {
        this.savingSignal.set(false);
        this.conflictSignal.set(false);
        this.teamSheetSignal.set(saved);
        this.selectionSignal.set(selectionOf(saved));
        this.savedMessageSignal.set(
          created ? 'Your side was saved for this fixture.' : 'Your side was updated.',
        );
      },
      error: (error: unknown) => this.onSaveError(error),
    });
  }

  /**
   * Re-applies the selection against the state that arrived after a conflict (§11.2).
   *
   * The sheet is read first and the save is conditional on what that read returns, rather than on whatever
   * version the client happens to hold. A `412` reloads asynchronously, so a manager who clicks reapply the
   * moment the conflict appears would otherwise re-send the version that was just refused and be refused
   * again — a loop with no way out but a page reload.
   */
  reapply(): void {
    this.conflictSignal.set(false);
    this.validationSignal.set(null);

    const teamSheet = this.teamSheetSignal();

    if (teamSheet === null) {
      return;
    }

    this.api.teamSheet(teamSheet.fixtureId).subscribe({
      next: (refreshed) => {
        this.teamSheetSignal.set(refreshed);
        this.save();
      },
      error: () =>
        this.saveErrorSignal.set('Your side could not be reloaded. Reload the page to reapply.'),
    });
  }

  /** Abandons the selection and adopts the server's current side. */
  discardConflict(): void {
    const teamSheet = this.teamSheetSignal();

    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.validationSignal.set(null);
    this.conflictSignal.set(false);
    this.selectionSignal.set(teamSheet === null ? new Map() : selectionOf(teamSheet));
  }

  /** Forgets everything read and edited. Called when the session ends. */
  clear(): void {
    this.fixturesSignal.set(null);
    this.fixturesLoadingSignal.set(false);
    this.fixturesErrorSignal.set(null);
    this.teamSheetSignal.set(null);
    this.selectionSignal.set(new Map());
    this.loadingSignal.set(false);
    this.savingSignal.set(false);
    this.clearMessages();
  }

  private slots(designation: string): readonly TeamSheetSlot[] {
    return (this.teamSheetSignal()?.slots ?? []).filter((slot) => slot.designation === designation);
  }

  private edit(selection: ReadonlyMap<number, string>): void {
    this.selectionSignal.set(selection);

    // An edit invalidates the last refusal and any stale success note, so the manager sees the effect of
    // what they just changed rather than a message about what they changed before.
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.validationSignal.set(null);
    this.conflictSignal.set(false);
  }

  private onSaveError(error: unknown): void {
    this.savingSignal.set(false);

    if (!(error instanceof ApiError)) {
      this.saveErrorSignal.set('Your side could not be saved.');

      return;
    }

    if (error.isPreconditionFailed) {
      // Another device won. Keep the selection, pull the server's state, and let the manager reapply.
      this.conflictSignal.set(true);
      this.saveErrorSignal.set(null);
      this.reload();

      return;
    }

    const validation = error.extension<TeamSheetValidation>('validation');

    if (validation !== null) {
      this.validationSignal.set(validation);

      return;
    }

    this.saveErrorSignal.set(error.detail);
  }

  /** Re-reads the sheet without touching the selection, for a conflict. */
  private reload(): void {
    const teamSheet = this.teamSheetSignal();

    if (teamSheet === null) {
      return;
    }

    this.api.teamSheet(teamSheet.fixtureId).subscribe({
      next: (refreshed) => this.teamSheetSignal.set(refreshed),
      error: () =>
        this.saveErrorSignal.set('Your side could not be reloaded. Reload the page to reapply.'),
    });
  }

  private clearMessages(): void {
    this.saveErrorSignal.set(null);
    this.savedMessageSignal.set(null);
    this.validationSignal.set(null);
    this.conflictSignal.set(false);
  }
}

/** The stored selection of a read response, as the editable map. */
function selectionOf(teamSheet: FixtureTeamSheet): ReadonlyMap<number, string> {
  return new Map(
    teamSheet.slots
      .filter((slot) => slot.player !== null)
      .map((slot) => [slot.slotNumber, slot.player!.id]),
  );
}
