import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  INSTRUCTION_FIELDS,
  POSITION_FAMILY_ORDER,
  SelectOption,
  clampPitchCoordinate,
  familyLabel,
  issueMessage,
  pitchStyle,
  roleLabel,
  rolesForFamily,
} from '../../core/tactics/tactics-presentation';
import { positionLabel } from '../../core/squad/squad-presentation';
import { TacticsStore } from '../../core/tactics/tactics-store';
import { SelectablePlayer, TeamInstructions } from '../../core/tactics/tactics.models';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/** The drag payload for a player chip, so a drop can tell an assignment from a move. */
const PLAYER_MIME = 'application/x-touchline-player';

/** The drag payload for a slot marker, so a drop on the pitch repositions rather than assigns. */
const SLOT_MIME = 'application/x-touchline-slot';

/** One slot as the board draws it: the layout, its occupant, and how both should read. */
interface SlotView {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly style: Record<string, string>;
  readonly player: SelectablePlayer | null;
  readonly isOutOfPosition: boolean;
  readonly isUnavailable: boolean;
  readonly isSelected: boolean;
  readonly issueCount: number;
}

/**
 * The tactics screen (master plan §11.1, F-19).
 *
 * A formation board with the eleven slots at their normalized positions (`TAC-9`), the eight team
 * instructions, and the default lineup. A player is assigned by dragging their chip onto a slot, or — the
 * accessible alternative §11.3 requires — by picking them in the assignment table, where a keyboard or a
 * screen reader reaches every slot without dragging anything. The pitch and the table are two views of one
 * draft, so a slot moved on the board reads the same in the table.
 *
 * The plan's version is the ETag (`CONC-1`). A save that loses a race is answered `412`; the board keeps
 * the manager's edits, pulls the server's state, and offers an explicit reapply (§11.2) rather than
 * overwriting the change that won.
 */
@Component({
  selector: 'app-tactics',
  imports: [RouterLink],
  templateUrl: './tactics.html',
})
export class Tactics {
  private readonly store = inject(TacticsStore);

  protected readonly draft = this.store.draft;
  protected readonly plans = this.store.plans;
  protected readonly formations = this.store.formations;
  protected readonly selectedPlan = this.store.selectedPlan;
  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.loadError;
  protected readonly saving = this.store.saving;
  protected readonly saveError = this.store.saveError;
  protected readonly savedMessage = this.store.savedMessage;
  protected readonly validation = this.store.validation;
  protected readonly hasConflict = this.store.hasConflict;
  protected readonly isDirty = this.store.isDirty;
  protected readonly assignedCount = this.store.assignedCount;
  protected readonly tactics = this.store.tactics;

  /** The slot the manager has focused, or 0 when none is. Assigning a player needs a target slot. */
  protected readonly selectedSlot = signal(0);

  protected readonly instructionFields = INSTRUCTION_FIELDS;
  protected readonly maxNameLength = 64;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;

  /** The board: each draft slot with its occupant and the marks it should carry. */
  protected readonly slotViews = computed<readonly SlotView[]>(() => {
    const draft = this.draft();

    if (draft === null) {
      return [];
    }

    const players = this.store.selectablePlayers();
    const byId = new Map(players.map((player) => [player.id, player]));
    const issues = this.store.issuesBySlot();
    const selected = this.selectedSlot();

    return draft.slots.map((slot) => {
      const player = slot.playerId === null ? null : (byId.get(slot.playerId) ?? null);

      return {
        slotNumber: slot.slotNumber,
        positionFamily: slot.positionFamily,
        role: slot.role,
        style: pitchStyle(slot),
        player,
        isOutOfPosition: player !== null && player.positionFamily !== slot.positionFamily,
        isUnavailable: player?.isUnavailable ?? false,
        isSelected: selected === slot.slotNumber,
        issueCount: (issues.get(slot.slotNumber) ?? []).length,
      };
    });
  });

  /** The squad grouped by position family, so a select offers a goalkeeper to a goalkeeper's slot first. */
  protected readonly playerGroups = computed(() => {
    const players = [...this.store.selectablePlayers()].sort((left, right) =>
      left.fullName.localeCompare(right.fullName),
    );

    return POSITION_FAMILY_ORDER.map((family) => ({
      family,
      label: familyLabel(family),
      players: players.filter((player) => player.positionFamily === family),
    })).filter((group) => group.players.length > 0);
  });

  /** The refusal's reasons in words, each naming its slot or player. */
  protected readonly issueMessages = computed(() => {
    const validation = this.validation();

    if (validation === null) {
      return [];
    }

    const names = new Map(
      this.store.selectablePlayers().map((player) => [player.id, player.fullName]),
    );

    return validation.issues.map((issue) => issueMessage(issue, names));
  });

  constructor() {
    this.store.load();
  }

  /** Selects a slot, or clears the selection when it is already the focused one. */
  protected selectSlot(slotNumber: number): void {
    this.selectedSlot.update((current) => (current === slotNumber ? 0 : slotNumber));
  }

  /** Assigns a player to the focused slot. Does nothing until a slot is chosen. */
  protected assignToSelected(playerId: string): void {
    const slot = this.selectedSlot();

    if (slot !== 0) {
      this.store.assignPlayer(slot, playerId);
    }
  }

  /** Empties a slot from the board. */
  protected clearSlot(slotNumber: number): void {
    this.store.clearSlot(slotNumber);
  }

  /** Starts a new plan. */
  protected newPlan(): void {
    this.selectedSlot.set(0);
    this.store.newPlan();
  }

  /** Switches between saved plans, or starts a new one from the select's empty option. */
  protected onPlanChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    if (value.length === 0) {
      this.newPlan();

      return;
    }

    this.selectedSlot.set(0);
    this.store.selectPlan(value);
  }

  /** Renames the plan as the manager types. */
  protected onNameInput(event: Event): void {
    this.store.setName((event.target as HTMLInputElement).value);
  }

  /** Re-lays the plan from a new formation preset. */
  protected onFormationChange(event: Event): void {
    this.store.setFormation((event.target as HTMLSelectElement).value);
  }

  /** Changes one team instruction (`INS-1`…`INS-8`). */
  protected onInstructionChange(key: keyof TeamInstructions, event: Event): void {
    this.store.setInstruction(key, (event.target as HTMLSelectElement).value);
  }

  /** Changes a slot's role from the assignment table (`TAC-8`). */
  protected onRoleChange(slotNumber: number, event: Event): void {
    this.store.setRole(slotNumber, (event.target as HTMLSelectElement).value);
  }

  /** Assigns or clears a slot's occupant from the assignment table. */
  protected onPlayerChange(slotNumber: number, event: Event): void {
    const playerId = (event.target as HTMLSelectElement).value;

    if (playerId.length === 0) {
      this.store.clearSlot(slotNumber);

      return;
    }

    this.store.assignPlayer(slotNumber, playerId);
  }

  /** Saves, or creates the plan when it has never been saved. */
  protected save(): void {
    this.store.save();
  }

  /** Re-applies the draft after a conflict (`§11.2`). */
  protected reapply(): void {
    this.store.reapply();
  }

  /** Adopts the server's state after a conflict. */
  protected discardConflict(): void {
    this.selectedSlot.set(0);
    this.store.discardConflict();
  }

  /** Promotes a plan to the club's default (`INS-11`). */
  protected makeDefault(planId: string): void {
    this.store.makeDefault(planId);
  }

  /** The roles a slot may take, which its family decides (`TAC-8`). */
  protected roleOptions(family: string): readonly SelectOption[] {
    return rolesForFamily(family);
  }

  /** Names a position family. */
  protected family(code: string): string {
    return familyLabel(code);
  }

  /** Names a role. */
  protected role(code: string): string {
    return roleLabel(code);
  }

  /** Names a position. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /**
   * The classes for a slot marker.
   *
   * Every state carries a word as well as a tint — an empty slot says so, an out-of-position pick is
   * labelled, and an unavailable player is named — so a colour is never the only signal (master plan
   * §11.3). The marker's own text is `slotLabel`, which is the accessible name.
   */
  protected slotClasses(slot: SlotView): string {
    const base =
      'absolute -translate-x-1/2 translate-y-1/2 flex h-12 w-12 flex-col items-center justify-center ' +
      'rounded-full border-2 text-center text-[10px] font-semibold leading-tight shadow ' +
      'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white';

    if (slot.issueCount > 0 || slot.isUnavailable) {
      return `${base} border-red-500 bg-red-100 text-red-900`;
    }

    if (slot.isSelected) {
      return `${base} border-amber-400 bg-white text-slate-900 ring-2 ring-amber-300`;
    }

    if (slot.player === null) {
      return `${base} border-white/70 border-dashed bg-emerald-800 text-white`;
    }

    if (slot.isOutOfPosition) {
      return `${base} border-amber-300 bg-amber-50 text-amber-900`;
    }

    return `${base} border-white bg-white text-slate-900`;
  }

  /** The accessible name of a slot marker, describing where it is and who is in it. */
  protected slotLabel(slot: SlotView): string {
    const occupant = slot.player === null ? 'empty' : slot.player.fullName;

    return `Slot ${slot.slotNumber}, ${familyLabel(slot.positionFamily)}, ${roleLabel(slot.role)}: ${occupant}`;
  }

  /** Starts dragging a player chip. */
  protected onPlayerDragStart(event: DragEvent, playerId: string): void {
    event.dataTransfer?.setData(PLAYER_MIME, playerId);

    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  /** Starts moving a slot on the board. */
  protected onSlotDragStart(event: DragEvent, slotNumber: number): void {
    event.dataTransfer?.setData(SLOT_MIME, String(slotNumber));

    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  /** Allows a player chip to be dropped on a slot, and nothing else. */
  protected onSlotDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes(PLAYER_MIME) === true) {
      event.preventDefault();
    }
  }

  /** Assigns the dragged player to the slot it was dropped on. */
  protected onSlotDrop(event: DragEvent, slotNumber: number): void {
    const playerId = event.dataTransfer?.getData(PLAYER_MIME) ?? '';

    if (playerId.length === 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    this.store.assignPlayer(slotNumber, playerId);
    this.selectedSlot.set(slotNumber);
  }

  /** Allows a dragged slot to be dropped on the pitch. */
  protected onPitchDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes(SLOT_MIME) === true) {
      event.preventDefault();
    }
  }

  /**
   * Moves the dragged slot to where it was dropped (`TAC-7`).
   *
   * The board draws depth upward, so the vertical drop position is inverted before it becomes the stored
   * `x` — the number the engine will hash is the number this writes, not a display-only approximation.
   */
  protected onPitchDrop(event: DragEvent): void {
    const raw = event.dataTransfer?.getData(SLOT_MIME) ?? '';
    const slotNumber = Number.parseInt(raw, 10);

    if (Number.isNaN(slotNumber)) {
      return;
    }

    event.preventDefault();

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();

    if (rect.height === 0 || rect.width === 0) {
      return;
    }

    const normalizedX = clampPitchCoordinate(
      (1 - (event.clientY - rect.top) / rect.height) * 10_000,
    );
    const normalizedY = clampPitchCoordinate(((event.clientX - rect.left) / rect.width) * 10_000);

    this.store.moveSlot(slotNumber, normalizedX, normalizedY);
    this.selectedSlot.set(slotNumber);
  }
}
