import { Component, computed, inject, input, signal } from '@angular/core';
import {
  EVENT_CODES,
  InstructionProblem,
  KEEPER_SLOT,
  MATCH_SUBSTITUTION_LIMIT,
  MAX_INSTRUCTION_NAME_LENGTH,
  MAX_MATCH_INSTRUCTIONS,
  MAX_MINUTE,
  MAX_RULE_SUBSTITUTIONS,
  MAX_SCORE_MARGIN,
  MENTALITY_CODES,
  ChangeKey,
  blankInstruction,
  eventHasSubject,
  eventTakesFamily,
  eventTakesThreshold,
  hasUnplaceableIncoming,
  inertInstructions,
  instructionProblems,
  placedButLeaving,
  plannedSubstitutions,
  withAddedSubstitution,
  withChange,
  withCounterAttackChange,
  withEvent,
  withFormationChoice,
  withMentality,
  withMentalitySteps,
  withOffSelector,
  withOnSelector,
  withPlacement,
  withSubstitution,
  withoutOrphanedPlacements,
  withoutPlacements,
  withoutSubstitution,
} from '../../../core/tactics/match-instructions';
import {
  ShapePlacement,
  naturalPlacements,
  previewShape,
} from '../../../core/tactics/match-instructions-fit';
import {
  EVENT_OPTIONS,
  MENTALITY_CHANGE_OPTIONS,
  OFF_OPTIONS,
  ON_OPTIONS,
  OUTFIELD_FAMILY_OPTIONS,
  SCORE_OPTIONS,
  conditionLabel,
  eventFamilyOptions,
  instructionIssueMessage,
  instructionSummary,
  ratingLabel,
} from '../../../core/tactics/match-instructions-presentation';
import {
  INSTRUCTION_FIELDS,
  InstructionField,
  familyLabel,
  pitchStyle,
  roleLabel,
} from '../../../core/tactics/tactics-presentation';
import { TacticsStore } from '../../../core/tactics/tactics-store';
import {
  FormationSlot,
  MatchInstruction,
  MatchInstructionSubstitution,
  SelectablePlayer,
} from '../../../core/tactics/tactics.models';
import {
  CHECKBOX_INPUT,
  CHECKBOX_ROW,
  LINK_ACTION,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';

/** One instruction as the list draws it. */
interface RuleView {
  readonly index: number;
  readonly ordinal: number;
  readonly rule: MatchInstruction;
  readonly summary: string;
  readonly isOpen: boolean;
  readonly problems: readonly string[];

  /** What deserves a look but does not stop a save: placements the rule never uses. */
  readonly warnings: readonly string[];
  readonly isInert: boolean;
  readonly hasUnplaceableIncoming: boolean;
}

/** A player a picker offers, with a note when the instruction names someone the plan no longer fields. */
interface Choice {
  readonly id: string;
  readonly label: string;
}

/** One slot of the open instruction's new shape, as the small pitch draws it. */
interface ShapeSlotView {
  readonly slotNumber: number;
  readonly style: Record<string, string>;
  readonly label: string;
  readonly player: SelectablePlayer | null;
  readonly isChosen: boolean;
  readonly isOutOfPosition: boolean;
  readonly isSelected: boolean;
  readonly isKeeper: boolean;
}

/** A dot being dragged across the small pitch. */
interface ShapeDrag {
  readonly slotNumber: number;
  readonly dx: number;
  readonly dy: number;

  /** Whether the pointer has moved far enough to count as a drag rather than a click. */
  readonly isActive: boolean;

  /** The slot the dot would swap places with if let go now, or 0. */
  readonly overSlot: number;
}

/** How far the pointer must move, in pixels, before a press on a dot becomes a drag. */
const DRAG_THRESHOLD = 6;

/** How close, in pixels, a dot must be let go to another dot's centre to swap with it. */
const DROP_RADIUS = 30;

/**
 * The Match instructions tab of the tactics screen (`MAT-6`).
 *
 * Rules the manager sets before the match and the engine carries out when their conditions hold: when (the minute, the score, an
 * event) then (a new shape, changed team instructions, substitutions). Each fires at most once, and they are checked in the order
 * listed. The tab edits the same plan draft as the Match tactics tab, through the same store, so the plan saves as one.
 *
 * A new shape is fitted automatically from the plan's starting eleven (`shape-fit.ts`, the same algorithm the server and the engine
 * run), and the manager moves a player by dragging his dot onto another (the two swap places), or by selecting one dot and then another. The rule's substitutions
 * are made first, so a substitute it brings on by name is placed like a starter, and a slot a player is not at home in is marked.
 */
@Component({
  selector: 'app-match-instructions',
  templateUrl: './match-instructions.html',
})
export class MatchInstructions {
  private readonly store = inject(TacticsStore);

  /** Each player's other positions, from the squad read, so the preview fits as the server will. */
  readonly secondaryPositions = input<ReadonlyMap<string, readonly string[]>>(new Map());

  /** Whether a write is allowed; offline or read-only the tab stays editable but nothing is sent. */
  readonly canMutate = input(true);

  protected readonly draft = this.store.draft;

  /** The place of the instruction whose editor is open, or null. */
  protected readonly openIndex = signal<number | null>(null);

  /** The slot of the open instruction's new shape that is selected, or 0. */
  protected readonly selectedSlot = signal(0);

  /** The dot being dragged on the small pitch: its slot, how far it has moved in pixels, and the slot it would swap with. */
  protected readonly drag = signal<ShapeDrag | null>(null);

  /** Where the pointer went down, for measuring how far a drag has gone. */
  private dragOrigin: { readonly x: number; readonly y: number } | null = null;

  /** Whether the click that follows a drag must be ignored, so letting go of a dot does not also select it. */
  private dragEndedOnDot = false;

  protected readonly scoreOptions = SCORE_OPTIONS;
  protected readonly eventOptions = EVENT_OPTIONS;
  protected readonly eventFamilies = eventFamilyOptions();
  protected readonly outfieldFamilies = OUTFIELD_FAMILY_OPTIONS;
  protected readonly offOptions = OFF_OPTIONS;
  protected readonly onOptions = ON_OPTIONS;
  protected readonly mentalityChoices = MENTALITY_CHANGE_OPTIONS;
  protected readonly mentalityNumbers = MENTALITY_CODES.map((code, value) => ({
    value,
    label: INSTRUCTION_FIELDS[0].options.find((option) => option.value === code)?.label ?? code,
  }));

  /** The team instructions a rule can set besides the mentality, each a code. */
  protected readonly changeFields: readonly InstructionField[] = INSTRUCTION_FIELDS.filter(
    (field) => field.key !== 'mentality',
  );

  protected readonly maxRules = MAX_MATCH_INSTRUCTIONS;
  protected readonly maxRuleSubstitutions = MAX_RULE_SUBSTITUTIONS;
  protected readonly substitutionLimit = MATCH_SUBSTITUTION_LIMIT;
  protected readonly maxNameLength = MAX_INSTRUCTION_NAME_LENGTH;
  protected readonly maxMinute = MAX_MINUTE;
  protected readonly maxScoreMargin = MAX_SCORE_MARGIN;
  protected readonly keeperSlot = KEEPER_SLOT;
  protected readonly events = EVENT_CODES;

  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly checkboxClass = CHECKBOX_INPUT;
  protected readonly checkboxRowClass = CHECKBOX_ROW;
  protected readonly linkClass = LINK_ACTION;

  private readonly names = computed(
    () => new Map(this.store.selectablePlayers().map((player) => [player.id, player.fullName])),
  );

  private readonly lineupIds = computed(
    () =>
      new Set(
        (this.draft()?.slots ?? []).flatMap((slot) =>
          slot.playerId === null ? [] : [slot.playerId],
        ),
      ),
  );

  private readonly benchIds = computed(
    () =>
      new Set(
        (this.draft()?.bench ?? []).flatMap((place) =>
          place.playerId === null ? [] : [place.playerId],
        ),
      ),
  );

  /** The instructions, in the order the engine checks them. */
  protected readonly rules = computed(() => this.draft()?.matchInstructions ?? []);

  /** How many substitutions the instructions that are on plan between them, against the five a club may make. */
  protected readonly plannedCount = computed(() => plannedSubstitutions(this.rules()));

  /** Whether the plan is at its limit of instructions. */
  protected readonly isFull = computed(() => this.rules().length >= MAX_MATCH_INSTRUCTIONS);

  /** Every problem with the instructions: what the editor finds itself, and what the server last refused that it did not. */
  private readonly problems = computed<readonly InstructionProblem[]>(() => {
    const local = instructionProblems(this.rules(), this.lineupIds(), this.benchIds());
    const known = new Set(local.map((problem) => `${problem.ordinal}:${problem.code}`));
    const fromServer = (this.store.validation()?.issues ?? [])
      .filter(
        (issue) =>
          issue.instructionOrdinal !== undefined &&
          issue.instructionOrdinal !== null &&
          !known.has(`${issue.instructionOrdinal}:${issue.code}`),
      )
      .map((issue) => ({
        code: issue.code,
        ordinal: issue.instructionOrdinal ?? null,
        playerId: issue.playerId,
      }));

    return [...local, ...fromServer];
  });

  /** The problems with the list as a whole, not any one instruction. */
  protected readonly listProblems = computed(() =>
    this.problems()
      .filter((problem) => problem.ordinal === null)
      .map((problem) => instructionIssueMessage(problem, this.names())),
  );

  /** The instructions as the list draws them. */
  protected readonly ruleViews = computed<readonly RuleView[]>(() => {
    const open = this.openIndex();
    const inert = new Set(inertInstructions(this.rules()));

    return this.rules().map((rule, index) => ({
      index,
      ordinal: index + 1,
      rule,
      summary: instructionSummary(rule, this.names()),
      isOpen: open === index,
      problems: this.problems()
        .filter((problem) => problem.ordinal === index + 1)
        .map((problem) => instructionIssueMessage(problem, this.names())),
      warnings: placedButLeaving(rule).map(
        (id) =>
          `${this.names().get(id) ?? 'A player'} is placed, but this instruction takes him off.`,
      ),
      isInert: inert.has(index + 1),
      hasUnplaceableIncoming: hasUnplaceableIncoming(rule, this.benchIds()),
    }));
  });

  /** The players who start in the plan, for the pickers that name someone to take off or to place. */
  protected readonly starters = computed<readonly SelectablePlayer[]>(() => {
    const byId = new Map(this.store.selectablePlayers().map((player) => [player.id, player]));

    return (this.draft()?.slots ?? []).flatMap((slot) => {
      const player = slot.playerId === null ? undefined : byId.get(slot.playerId);

      return player === undefined ? [] : [player];
    });
  });

  /** The formation presets a rule can move to. */
  protected readonly formationCodes = computed(() =>
    this.store.formations().map((formation) => formation.code),
  );

  /** The open instruction's new shape: each slot of the preset with who the fit puts there. */
  protected readonly shape = computed<readonly ShapeSlotView[] | null>(() => {
    const draft = this.draft();
    const index = this.openIndex();
    const rule = index === null ? undefined : this.rules()[index];
    const formation = this.store
      .formations()
      .find((candidate) => candidate.code === rule?.formation);

    if (draft === null || rule === undefined || formation === undefined) {
      return null;
    }

    const placements = previewShape(
      draft,
      rule,
      this.store.formations(),
      this.store.selectablePlayers(),
      this.secondaryPositions(),
    );
    const bySlot = new Map<number, ShapePlacement>(
      (placements ?? []).map((item) => [item.slotNumber, item]),
    );
    const players = new Map(this.store.selectablePlayers().map((player) => [player.id, player]));
    const selected = this.selectedSlot();

    return formation.slots.map((slot: FormationSlot) => {
      const placement = bySlot.get(slot.slotNumber);
      const player = placement === undefined ? null : (players.get(placement.playerId) ?? null);

      return {
        slotNumber: slot.slotNumber,
        style: pitchStyle(slot),
        label: `Slot ${slot.slotNumber}, ${familyLabel(slot.positionFamily)}, ${roleLabel(slot.role)}: ${player?.fullName ?? 'empty'}${placement?.isChosen ? ', placed by you' : ''}${placement?.isOutOfPosition ? ', out of position' : ''}`,
        player,
        isChosen: placement?.isChosen ?? false,
        isOutOfPosition: placement?.isOutOfPosition ?? false,
        isSelected: selected === slot.slotNumber,
        isKeeper: slot.slotNumber === KEEPER_SLOT,
      };
    });
  });

  /** Whether any slot of the open instruction's new shape holds a player who is not at home in it. */
  protected readonly anyOutOfPosition = computed(
    () => this.shape()?.some((slot) => slot.isOutOfPosition) ?? false,
  );

  /** The slot being edited on the small pitch, for the line under it. */
  protected readonly selectedShapeSlot = computed(
    () => this.shape()?.find((slot) => slot.isSelected) ?? null,
  );

  /** Adds a blank instruction and opens its editor. */
  protected add(): void {
    if (this.isFull()) {
      return;
    }

    const index = this.rules().length;

    this.store.setMatchInstruction(index, blankInstruction());
    this.open(index);
  }

  /** Opens an instruction's editor, or closes it when it is the open one. */
  protected toggle(index: number): void {
    this.open(this.openIndex() === index ? null : index);
  }

  private open(index: number | null): void {
    this.openIndex.set(index);
    this.selectedSlot.set(0);
  }

  /** Removes an instruction, keeping the editor on the one that was open when it is still there. */
  protected remove(index: number): void {
    const open = this.openIndex();

    this.store.removeMatchInstruction(index);
    this.selectedSlot.set(0);

    if (open !== null) {
      this.openIndex.set(open === index ? null : open > index ? open - 1 : open);
    }
  }

  /** Moves an instruction earlier or later; the editor follows it when it was open. */
  protected move(index: number, delta: -1 | 1): void {
    const target = index + delta;

    if (target < 0 || target >= this.rules().length) {
      return;
    }

    const open = this.openIndex();

    this.store.moveMatchInstruction(index, delta);

    if (open === index) {
      this.openIndex.set(target);
    } else if (open === target) {
      this.openIndex.set(index);
    }
  }

  /** Copies an instruction and opens the copy. */
  protected duplicate(index: number): void {
    if (this.isFull()) {
      return;
    }

    this.store.duplicateMatchInstruction(index);
    this.open(index + 1);
  }

  /** Turns the assistant's fatigue substitutions on or off. */
  protected onAssistantChange(event: Event): void {
    this.store.setAssistantFatigue((event.target as HTMLInputElement).checked);
  }

  private update(index: number, rule: MatchInstruction): void {
    this.store.setMatchInstruction(index, rule);
  }

  /** Updates after a substitution changed, dropping the placement of a substitute the rule no longer brings on. */
  private updateAfterSubstitution(index: number, rule: MatchInstruction): void {
    this.update(index, withoutOrphanedPlacements(rule, this.lineupIds(), this.benchIds()));
  }

  protected setEnabled(view: RuleView, event: Event): void {
    this.update(view.index, {
      ...view.rule,
      isEnabled: (event.target as HTMLInputElement).checked,
    });
  }

  protected setName(view: RuleView, event: Event): void {
    this.update(view.index, { ...view.rule, name: (event.target as HTMLInputElement).value });
  }

  protected setFromMinute(view: RuleView, event: Event): void {
    this.update(view.index, { ...view.rule, fromMinute: whole(event, 0) });
  }

  protected setUntilMinute(view: RuleView, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();

    this.update(view.index, {
      ...view.rule,
      untilMinute: value.length === 0 ? null : whole(event, 0),
    });
  }

  protected setScore(view: RuleView, event: Event): void {
    this.update(view.index, { ...view.rule, score: (event.target as HTMLSelectElement).value });
  }

  protected setScoreMargin(view: RuleView, event: Event): void {
    this.update(view.index, { ...view.rule, scoreMargin: whole(event, 1) });
  }

  protected setEvent(view: RuleView, event: Event): void {
    this.update(view.index, withEvent(view.rule, (event.target as HTMLSelectElement).value));
  }

  protected setEventFamily(view: RuleView, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.update(view.index, { ...view.rule, eventFamily: value.length === 0 ? null : value });
  }

  /** Sets a rating threshold typed as a rating (5.7), which the rule keeps in basis points. */
  protected setRatingThreshold(view: RuleView, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    const rating = Number(value);

    this.update(view.index, {
      ...view.rule,
      eventThreshold: value.length === 0 || Number.isNaN(rating) ? null : Math.round(rating * 1000),
    });
  }

  /** Sets a condition threshold typed as a percentage, which the rule keeps in basis points. */
  protected setConditionThreshold(view: RuleView, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    const percent = Number(value);

    this.update(view.index, {
      ...view.rule,
      eventThreshold:
        value.length === 0 || Number.isNaN(percent) ? null : Math.round(percent * 100),
    });
  }

  protected setMentalityThreshold(view: RuleView, event: Event): void {
    this.update(view.index, {
      ...view.rule,
      eventThreshold: Number((event.target as HTMLSelectElement).value),
    });
  }

  /** The rating threshold as the number typed into its box. */
  protected ratingValue(rule: MatchInstruction): string {
    return rule.eventThreshold === null ? '' : ratingLabel(rule.eventThreshold);
  }

  /** The condition threshold as the percentage typed into its box. */
  protected conditionValue(rule: MatchInstruction): string {
    return rule.eventThreshold === null ? '' : conditionLabel(rule.eventThreshold).replace('%', '');
  }

  protected takesFamily(rule: MatchInstruction): boolean {
    return eventTakesFamily(rule.event);
  }

  protected takesThreshold(rule: MatchInstruction): boolean {
    return eventTakesThreshold(rule.event);
  }

  protected hasSubject(rule: MatchInstruction): boolean {
    return eventHasSubject(rule.event);
  }

  protected setFormation(view: RuleView, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.selectedSlot.set(0);
    this.update(
      view.index,
      withFormationChoice(
        view.rule,
        value.length === 0 ? null : value,
        this.lineupIds(),
        this.benchIds(),
      ),
    );
  }

  /** The value a mentality select shows: a step, an absolute mentality, or no change. */
  protected mentalityChoice(rule: MatchInstruction): string {
    const { mentality, mentalitySteps } = rule.changes;

    return mentality !== null ? `set:${mentality}` : `steps:${mentalitySteps}`;
  }

  protected setMentalityChoice(view: RuleView, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const [kind, amount] = value.split(':');

    this.update(
      view.index,
      kind === 'set'
        ? withMentality(view.rule, amount)
        : amount === '0'
          ? withMentality(view.rule, null)
          : withMentalitySteps(view.rule, Number(amount)),
    );
  }

  protected setChange(view: RuleView, key: ChangeKey, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.update(view.index, withChange(view.rule, key, value.length === 0 ? null : value));
  }

  /** The value a team-instruction select shows: the code the rule sets, or empty for no change. */
  protected changeValue(rule: MatchInstruction, field: InstructionField): string {
    return (rule.changes[field.key as ChangeKey] as string | null) ?? '';
  }

  protected counterChoice(rule: MatchInstruction): string {
    const value = rule.changes.counterAttack;

    return value === null ? '' : value ? 'on' : 'off';
  }

  protected setCounterChoice(view: RuleView, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.update(
      view.index,
      withCounterAttackChange(view.rule, value.length === 0 ? null : value === 'on'),
    );
  }

  protected addSubstitution(view: RuleView): void {
    this.update(view.index, withAddedSubstitution(view.rule));
  }

  protected removeSubstitution(view: RuleView, place: number): void {
    this.updateAfterSubstitution(view.index, withoutSubstitution(view.rule, place));
  }

  protected setOff(view: RuleView, place: number, event: Event): void {
    this.update(
      view.index,
      withSubstitution(
        view.rule,
        place,
        withOffSelector(view.rule.substitutions[place], (event.target as HTMLSelectElement).value),
      ),
    );
  }

  protected setOffPlayer(view: RuleView, place: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.update(
      view.index,
      withSubstitution(view.rule, place, {
        ...view.rule.substitutions[place],
        offPlayerId: value.length === 0 ? null : value,
      }),
    );
  }

  protected setOffFamily(view: RuleView, place: number, event: Event): void {
    this.update(
      view.index,
      withSubstitution(view.rule, place, {
        ...view.rule.substitutions[place],
        offFamily: (event.target as HTMLSelectElement).value,
      }),
    );
  }

  protected setOn(view: RuleView, place: number, event: Event): void {
    this.updateAfterSubstitution(
      view.index,
      withSubstitution(
        view.rule,
        place,
        withOnSelector(view.rule.substitutions[place], (event.target as HTMLSelectElement).value),
      ),
    );
  }

  protected setOnPlayer(view: RuleView, place: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.updateAfterSubstitution(
      view.index,
      withSubstitution(view.rule, place, {
        ...view.rule.substitutions[place],
        onPlayerId: value.length === 0 ? null : value,
      }),
    );
  }

  protected setOnFamily(view: RuleView, place: number, event: Event): void {
    this.update(
      view.index,
      withSubstitution(view.rule, place, {
        ...view.rule.substitutions[place],
        onFamily: (event.target as HTMLSelectElement).value,
      }),
    );
  }

  /** The offered "take off" selectors: the subject only makes sense when the event has one. */
  protected offChoicesFor(rule: MatchInstruction) {
    return this.offOptions.filter(
      (option) => option.value !== 'subject' || eventHasSubject(rule.event),
    );
  }

  /** The starters a substitution can take off, plus the named player when the plan no longer starts him. */
  protected offPlayers(substitution: MatchInstructionSubstitution): readonly Choice[] {
    const choices: Choice[] = this.starters().map((player) => ({
      id: player.id,
      label: player.fullName,
    }));

    return withStale(choices, substitution.offPlayerId, this.names(), 'not in the starting eleven');
  }

  /** The substitutes a substitution can bring on, plus the named player when the plan no longer benches him. */
  protected onPlayers(substitution: MatchInstructionSubstitution): readonly Choice[] {
    const byId = new Map(this.store.selectablePlayers().map((player) => [player.id, player]));
    const choices: Choice[] = (this.draft()?.bench ?? []).flatMap((place) => {
      const player = place.playerId === null ? undefined : byId.get(place.playerId);

      return player === undefined ? [] : [{ id: player.id, label: player.fullName }];
    });

    return withStale(choices, substitution.onPlayerId, this.names(), 'not on the bench');
  }

  /**
   * Selects a slot of the new shape, or clears the selection when it is the selected one. With one slot already selected, choosing another
   * swaps the two players, which is the keyboard's way of doing what a drag does. The goalkeeper's slot is fixed.
   */
  protected selectShapeSlot(slotNumber: number): void {
    if (this.dragEndedOnDot) {
      this.dragEndedOnDot = false;

      return;
    }

    if (slotNumber === KEEPER_SLOT) {
      return;
    }

    const current = this.selectedSlot();

    if (current === 0 || current === slotNumber) {
      this.selectedSlot.set(current === slotNumber ? 0 : slotNumber);

      return;
    }

    this.swapSlots(current, slotNumber);
    this.selectedSlot.set(0);
  }

  /** Stands the player of one slot of the open instruction's new shape in another, and the other's player in his. */
  protected swapSlots(from: number, to: number): void {
    const index = this.openIndex();
    const rule = index === null ? undefined : this.rules()[index];
    const slots = this.shape();
    const mover = slots?.find((slot) => slot.slotNumber === from)?.player;
    const other = slots?.find((slot) => slot.slotNumber === to)?.player;

    if (
      index === null ||
      rule === undefined ||
      mover === null ||
      mover === undefined ||
      from === to ||
      from === KEEPER_SLOT ||
      to === KEEPER_SLOT
    ) {
      return;
    }

    const moved = withPlacement(rule, mover.id, to);

    this.update(
      index,
      other === null || other === undefined ? moved : withPlacement(moved, other.id, from),
    );
  }

  /** Starts a press on a dot. It becomes a drag once the pointer has moved a little; until then it is a click. */
  protected startDrag(slot: ShapeSlotView, event: PointerEvent): void {
    this.dragEndedOnDot = false;

    if (slot.isKeeper || slot.player === null || event.button > 0) {
      return;
    }

    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
    this.dragOrigin = { x: event.clientX, y: event.clientY };
    this.drag.set({ slotNumber: slot.slotNumber, dx: 0, dy: 0, isActive: false, overSlot: 0 });
  }

  /** Follows the pointer while a dot is held, and lights the dot it would swap with. */
  protected moveDrag(event: PointerEvent): void {
    const current = this.drag();
    const origin = this.dragOrigin;

    if (current === null || origin === null) {
      return;
    }

    const dx = event.clientX - origin.x;
    const dy = event.clientY - origin.y;

    if (!current.isActive && Math.hypot(dx, dy) < DRAG_THRESHOLD) {
      return;
    }

    this.drag.set({ ...current, dx, dy, isActive: true, overSlot: this.slotUnder(event) });
  }

  /** Lets go of a dot: over the dot it was lit on the two swap places, anywhere else the dot goes back. */
  protected endDrag(): void {
    const current = this.drag();
    const target = current?.isActive ? current.overSlot : 0;

    this.drag.set(null);
    this.dragOrigin = null;

    if (current === null || !current.isActive) {
      return;
    }

    this.dragEndedOnDot = true;

    if (target !== 0) {
      this.swapSlots(current.slotNumber, target);
      this.selectedSlot.set(0);
    }
  }

  /** Gives up a drag the browser took over, such as a touch that became a scroll. */
  protected cancelDrag(): void {
    this.drag.set(null);
    this.dragOrigin = null;
  }

  /** The slot whose dot is nearest the pointer, within reach, other than the one held and the goalkeeper's; 0 when none. */
  private slotUnder(event: PointerEvent): number {
    const held = this.drag()?.slotNumber ?? 0;
    const pitch = (event.currentTarget as HTMLElement).parentElement?.getBoundingClientRect();

    if (pitch === undefined || pitch.width === 0 || pitch.height === 0) {
      return 0;
    }

    const x = event.clientX - pitch.left;
    const y = event.clientY - pitch.top;
    let best = 0;
    let bestDistance = DROP_RADIUS;

    for (const slot of this.shape() ?? []) {
      if (slot.slotNumber === held || slot.isKeeper) {
        continue;
      }

      const centreX = (Number.parseFloat(slot.style['left']) / 100) * pitch.width;
      const centreY = pitch.height - (Number.parseFloat(slot.style['bottom']) / 100) * pitch.height;
      const distance = Math.hypot(x - centreX, y - centreY);

      if (distance < bestDistance) {
        best = slot.slotNumber;
        bestDistance = distance;
      }
    }

    return best;
  }

  /** The left edge of a dot, moved by the drag when it is the one held. */
  protected slotLeft(slot: ShapeSlotView): string {
    const held = this.drag();

    return held?.isActive && held.slotNumber === slot.slotNumber
      ? `calc(${slot.style['left']} + ${held.dx}px)`
      : slot.style['left'];
  }

  /** The bottom edge of a dot, moved by the drag when it is the one held. */
  protected slotBottom(slot: ShapeSlotView): string {
    const held = this.drag();

    return held?.isActive && held.slotNumber === slot.slotNumber
      ? `calc(${slot.style['bottom']} - ${held.dy}px)`
      : slot.style['bottom'];
  }

  /**
   * Places every player where he is at home as far as the squad allows, keeping the placements the manager made. The result is written as
   * placements, so each can be changed after.
   */
  protected placeNaturally(view: RuleView): void {
    const draft = this.draft();

    if (draft === null) {
      return;
    }

    const placements = naturalPlacements(
      draft,
      view.rule,
      this.store.formations(),
      this.store.selectablePlayers(),
      this.secondaryPositions(),
    );

    if (placements !== null) {
      this.update(view.index, { ...view.rule, overrides: [...placements] });
    }
  }

  /** Gives back every placement of the open instruction, leaving the best fit. */
  protected resetPlacements(view: RuleView): void {
    this.update(view.index, withoutPlacements(view.rule));
  }

  /** The classes of a slot marker on the small pitch. */
  protected shapeSlotClasses(slot: ShapeSlotView): string {
    const base =
      'absolute -translate-x-1/2 translate-y-1/2 flex h-11 w-11 flex-col items-center justify-center rounded-full border-2 ' +
      'text-center text-[10px] font-semibold leading-tight shadow focus-visible:outline focus-visible:outline-2 ' +
      'focus-visible:outline-offset-2 focus-visible:outline-accent touch-none';
    const held = this.drag();

    if (held?.isActive && held.slotNumber === slot.slotNumber) {
      return `${base} z-20 cursor-grabbing border-accent bg-panel text-ink opacity-90 shadow-lg ring-2 ring-accent`;
    }

    if (held?.isActive && held.overSlot === slot.slotNumber) {
      return `${base} border-accent bg-panel text-ink ring-2 ring-accent`;
    }

    if (slot.isSelected) {
      return `${base} border-accent bg-panel text-ink ring-2 ring-accent`;
    }

    if (slot.player === null) {
      return `${base} border-dashed border-white/70 bg-black/30 text-white`;
    }

    if (slot.isOutOfPosition) {
      return `${base} border-amber-300 bg-panel text-ink`;
    }

    return slot.isChosen
      ? `${base} border-accent bg-panel text-ink`
      : `${base} border-white/80 bg-panel text-ink`;
  }

  protected familyName(code: string): string {
    return familyLabel(code);
  }

  /** The text of the "applies to" line under the substitutions counter. */
  protected substitutionsNote(): string {
    const planned = this.plannedCount();

    return `${planned} of ${MATCH_SUBSTITUTION_LIMIT} substitutions planned`;
  }
}

/** Reads a whole number from a number box, falling back when it holds none. */
function whole(event: Event, fallback: number): number {
  const value = Number((event.target as HTMLInputElement).value);

  return Number.isFinite(value) ? Math.trunc(value) : fallback;
}

/** Adds the named player to a list of choices when the list does not hold him, so the picker shows what is stored. */
function withStale(
  choices: Choice[],
  id: string | null,
  names: ReadonlyMap<string, string>,
  note: string,
): readonly Choice[] {
  if (id === null || choices.some((choice) => choice.id === id)) {
    return choices;
  }

  return [...choices, { id, label: `${names.get(id) ?? 'Unknown player'} (${note})` }];
}
