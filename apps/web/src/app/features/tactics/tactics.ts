import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { SkillGroup, skillGroups } from '../../core/squad/position-ratings';
import { SquadStore } from '../../core/squad/squad-store';
import { attributeBand, positionLabel, stateBand } from '../../core/squad/squad-presentation';
import { Squad } from '../../core/squad/squad.models';
import {
  INSTRUCTION_FIELDS,
  SelectOption,
  familyLabel,
  issueMessage,
  pitchStyle,
  positionForRole,
  roleLabel,
  rolesForFamily,
} from '../../core/tactics/tactics-presentation';
import { RosterRow, buildRoster } from '../../core/tactics/tactics-roster';
import { TacticsStore } from '../../core/tactics/tactics-store';
import { SelectablePlayer, TeamInstructions } from '../../core/tactics/tactics.models';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/** The drag payload for a player row, so a drop on a slot can assign them. */
const PLAYER_MIME = 'application/x-touchline-player';

/** The skills popup's size (`w-[34rem]` and its usual height), which its placement keeps inside the window. */
const POPUP_WIDTH = 544;
const POPUP_HEIGHT = 360;
const POPUP_MARGIN = 8;

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

/** What the skills popup is showing, and where. */
interface SkillsPopup {
  readonly row: RosterRow;
  readonly groups: readonly SkillGroup[];
  readonly basisLabel: string;
  readonly left: number;
  readonly top: number;
}

/** The position the table's ratings are weighted for while a slot is selected. */
interface RatingBasis {
  readonly position: string;
  readonly slotNumber: number;
  readonly roleLabel: string;
}

/**
 * The tactics screen (master plan §11.1, F-19).
 *
 * A formation board with the eleven slots at the positions the chosen formation dictates (`TAC-9`), the
 * eight team instructions, and the default lineup. The slots are fixed: choosing a different formation is
 * the only way to change where they stand. A player is assigned by dragging their row in the player table
 * onto a slot, or — the accessible alternative §11.3 requires — by selecting a slot and pressing the
 * player's name, so a keyboard or a screen reader reaches every slot without dragging anything. The pitch
 * and the table are two views of one draft.
 *
 * The table rates every player for a position, so the better player for it is easy to see: their own
 * position by default, and the selected slot's while one is selected. Hovering (or focusing) a row opens a
 * popup with all twenty-eight skills, the ones that position weights most marked.
 *
 * The plan's version is the ETag (`CONC-1`). A save that loses a race is answered `412`; the board keeps
 * the manager's edits, pulls the server's state, and offers an explicit reapply (§11.2) rather than
 * overwriting the change that won.
 */
@Component({
  selector: 'app-tactics',
  imports: [DecimalPipe, RouterLink, TableModule],
  templateUrl: './tactics.html',
})
export class Tactics {
  private readonly store = inject(TacticsStore);
  private readonly squadStore = inject(SquadStore);
  private readonly maintenance = inject(MaintenanceStore);
  private requestedClubId: string | null = null;

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

  /**
   * The squad read for this club: who is how old and how fit, and every attribute. Held here rather than
   * taken from the shared squad store, so the screen never shows another visit's, or another club's, state.
   */
  private readonly squad = signal<Squad | null>(null);

  /** Whether the squad details could not be read, so the table can say what it is missing. */
  protected readonly detailsError = signal(false);

  /** The row whose skills are open in the popup, or null. */
  protected readonly popup = signal<SkillsPopup | null>(null);

  /** Whether a write is allowed; offline or read-only the board stays editable but nothing is sent (Â§11.4). */
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly instructionFields = INSTRUCTION_FIELDS;
  protected readonly maxNameLength = 64;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;
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

  /** The slot being edited, for the role picker under the board. */
  protected readonly selectedSlotView = computed(
    () => this.slotViews().find((slot) => slot.isSelected) ?? null,
  );

  /** The position the ratings are weighted for: the selected slot's, or null for each player's own. */
  protected readonly ratingBasis = computed<RatingBasis | null>(() => {
    const slot = this.selectedSlotView();
    const position = slot === null ? null : positionForRole(slot.role);

    return slot === null || position === null
      ? null
      : { position, slotNumber: slot.slotNumber, roleLabel: roleLabel(slot.role) };
  });

  /** The player table: everyone who may be picked, rated for the basis. */
  protected readonly roster = computed<RosterRow[]>(() => [
    ...buildRoster(
      this.store.selectablePlayers(),
      this.squad()?.players ?? [],
      this.draft()?.slots ?? [],
      this.ratingBasis()?.position ?? null,
    ),
  ]);

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

    // The squad read needs the club, which arrives with the tactics read. It is made once per visit.
    effect(() => {
      const clubId = this.tactics()?.clubId ?? null;

      if (clubId === null || clubId === this.requestedClubId) {
        return;
      }

      this.requestedClubId = clubId;
      this.squadStore.loadSquad(clubId).subscribe({
        next: (squad) => this.squad.set(squad),
        error: () => this.detailsError.set(true),
      });
    });
  }

  /** Selects a slot, or clears the selection when it is already the focused one. */
  protected selectSlot(slotNumber: number): void {
    this.selectedSlot.update((current) => (current === slotNumber ? 0 : slotNumber));
  }

  /** Assigns a player to the focused slot. Does nothing until a slot is chosen, or for an unavailable player. */
  protected assignToSelected(row: RosterRow): void {
    const slot = this.selectedSlot();

    if (slot !== 0 && !row.isUnavailable) {
      this.store.assignPlayer(slot, row.id);
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

  /** Changes a slot's role (`TAC-8`). */
  protected onRoleChange(slotNumber: number, event: Event): void {
    this.store.setRole(slotNumber, (event.target as HTMLSelectElement).value);
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
      'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent';

    if (slot.issueCount > 0 || slot.isUnavailable) {
      return `${base} border-red-400 bg-[#2a1416] text-red-200`;
    }

    if (slot.isSelected) {
      return `${base} border-accent bg-panel text-ink ring-2 ring-accent`;
    }

    if (slot.player === null) {
      return `${base} border-white/70 border-dashed bg-black/30 text-white`;
    }

    if (slot.isOutOfPosition) {
      return `${base} border-amber-400 bg-[#2b2310] text-amber-200`;
    }

    return `${base} border-white/80 bg-panel text-ink`;
  }

  /** The accessible name of a slot marker, describing where it is, who is in it, and its state. */
  protected slotLabel(slot: SlotView): string {
    const position = `${familyLabel(slot.positionFamily)}, ${roleLabel(slot.role)}`;

    if (slot.player === null) {
      return `Slot ${slot.slotNumber}, ${position}: empty`;
    }

    // The state is in the accessible name, not only in the visual words the marker renders — the marker
    // marks those `aria-hidden`, so a screen-reader manager would otherwise miss it (§11.3).
    const state = slot.isOutOfPosition
      ? ', out of position'
      : slot.isUnavailable
        ? ', unavailable'
        : '';

    return `Slot ${slot.slotNumber}, ${position}: ${slot.player.fullName}${state}`;
  }

  /** Starts dragging a player row. An unavailable player cannot be picked, so their row does not drag. */
  protected onPlayerDragStart(event: DragEvent, row: RosterRow): void {
    if (row.isUnavailable) {
      event.preventDefault();

      return;
    }

    this.popup.set(null);
    event.dataTransfer?.setData(PLAYER_MIME, row.id);

    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = 'move';
    }
  }

  /** Allows a player row to be dropped on a slot, and nothing else. */
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

  /** Opens the skills popup for a row, beside the pointer (or the row, for the keyboard). */
  protected showSkills(event: MouseEvent | FocusEvent, row: RosterRow): void {
    if (row.attributes === null) {
      return;
    }

    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const pointerX = event instanceof MouseEvent ? event.clientX + 16 : rect.left + 16;
    const fitsBelow = rect.bottom + POPUP_MARGIN + POPUP_HEIGHT < window.innerHeight;
    const basis = this.ratingBasis();

    this.popup.set({
      row,
      groups: skillGroups(row.attributes, row.ratedAs),
      basisLabel: basis === null ? positionLabel(row.position) : basis.roleLabel,
      left: Math.max(
        POPUP_MARGIN,
        Math.min(pointerX, window.innerWidth - POPUP_WIDTH - POPUP_MARGIN),
      ),
      top: Math.max(
        POPUP_MARGIN,
        fitsBelow ? rect.bottom + POPUP_MARGIN : rect.top - POPUP_HEIGHT - POPUP_MARGIN,
      ),
    });
  }

  /** Closes the skills popup. */
  protected hideSkills(): void {
    this.popup.set(null);
  }

  /** The tint for an attribute or an average, on the 1–20 scale. */
  protected skillClass(value: number | null): string {
    return value === null ? 'text-muted' : attributeBand(value).className;
  }

  /** The tint for a state value, which fatigue reads the other way round. */
  protected stateClass(value: number | null, higherIsBetter: boolean): string {
    return value === null ? 'text-muted' : stateBand(value, higherIsBetter).className;
  }

  /** The band word beside a state value, so colour is never the only signal (§11.3). */
  protected stateWord(value: number | null, higherIsBetter: boolean): string {
    return value === null ? '' : stateBand(value, higherIsBetter).label;
  }

  /** How a multiplier reads beside an attribute in the popup; nothing for the neutral ×1. */
  protected multiplierLabel(weight: number): string {
    return weight === 1 ? '' : `×${weight}`;
  }

  /** What pressing a row's name does, as the button's accessible name. */
  protected pickLabel(row: RosterRow): string {
    const slot = this.selectedSlot();

    if (row.isUnavailable) {
      return `${row.fullName}, unavailable`;
    }

    return slot === 0
      ? `${row.fullName}. Select a slot on the pitch first to pick them.`
      : `Pick ${row.fullName} for slot ${slot}`;
  }
}
