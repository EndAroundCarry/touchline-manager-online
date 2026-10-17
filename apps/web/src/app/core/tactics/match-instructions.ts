/**
 * Match instructions as pure data: the limits, the blank rule, the edits one rule takes, the templates a manager starts from, and the
 * checks that mirror the server's validator (`MAT-6`, `INS-12`).
 *
 * A match instruction is a rule the engine carries out during a match when its conditions hold: when (minute, score, an event) then
 * (a new shape, changed team instructions, substitutions). It fires at most once a match, and the rules are checked in the manager's
 * order. Everything here is immutable and free of Angular, like `tactics-draft.ts`, so the editor holds no rule logic and each edit has
 * one tested home.
 *
 * The codes are the server's. A client that changes the kind of event clears the family and threshold the old kind used, because the
 * server refuses an event with a parameter it does not take: {@link withEvent} does that in one place.
 */

import type {
  MatchInstruction,
  MatchInstructionChanges,
  MatchInstructionOverride,
  MatchInstructionSubstitution,
} from './tactics.models';

/** The most match instructions a plan holds. */
export const MAX_MATCH_INSTRUCTIONS = 10;

/** The most substitutions one match instruction makes. */
export const MAX_RULE_SUBSTITUTIONS = 3;

/** The most substitutions a club may make in a match (`SQ-5`): the enabled instructions may not plan more between them. */
export const MATCH_SUBSTITUTION_LIMIT = 5;

/** The longest name an instruction has. */
export const MAX_INSTRUCTION_NAME_LENGTH = 48;

/** The last minute an instruction may be set for. */
export const MAX_MINUTE = 90;

/** The largest goal margin a score condition names. */
export const MAX_SCORE_MARGIN = 5;

/** The most steps an instruction moves the mentality. */
export const MAX_MENTALITY_STEPS = 2;

/** The slot of the new shape that is the goalkeeper's: nobody else is placed there. */
export const KEEPER_SLOT = 1;

/** The highest a rating or a condition threshold goes, in basis points. */
export const MAX_THRESHOLD = 10_000;

/** The mentalities from the most defensive, so a code's index is the number an opponent-mentality event names. */
export const MENTALITY_CODES: readonly string[] = [
  'defensive',
  'cautious',
  'balanced',
  'positive',
  'attacking',
];

/** The events a rule can wait for, besides the minute and the score. */
export const EVENT_CODES = {
  none: 'none',
  ownSentOff: 'own_sent_off',
  opponentSentOff: 'opponent_sent_off',
  ownBooked: 'own_booked',
  playingBadly: 'playing_badly',
  tired: 'tired',
  opponentMentalityAtLeast: 'opponent_mentality_at_least',
  opponentMentalityAtMost: 'opponent_mentality_at_most',
} as const;

/** The default rating below which a player is "playing badly", in basis points: about one outfield player in ten at 60'. */
export const DEFAULT_BAD_RATING = 5_700;

/** The default condition below which a player is "tired", in basis points. */
export const DEFAULT_TIRED_CONDITION = 6_000;

/** The team instructions a rule can change, each a code (`counterAttack` is a switch). */
export type ChangeKey = Exclude<
  keyof MatchInstructionChanges,
  'mentality' | 'mentalitySteps' | 'counterAttack'
>;

/** A rule that changes nothing about the team's instructions. */
export function noChanges(): MatchInstructionChanges {
  return {
    mentality: null,
    mentalitySteps: 0,
    tempo: null,
    passing: null,
    width: null,
    passFocus: null,
    pressing: null,
    defensiveLine: null,
    tackling: null,
    timeWasting: null,
    counterAttack: null,
  };
}

/** A rule with no condition beyond "from kick-off" and no effect: the blank the editor starts from. */
export function blankInstruction(name = 'New instruction'): MatchInstruction {
  return {
    name,
    isEnabled: true,
    fromMinute: 0,
    untilMinute: null,
    score: 'any',
    scoreMargin: 1,
    event: EVENT_CODES.none,
    eventFamily: null,
    eventThreshold: null,
    formation: null,
    overrides: [],
    changes: noChanges(),
    substitutions: [],
  };
}

/** A substitution of a named player for a named player, with neither named yet. */
export function blankSubstitution(): MatchInstructionSubstitution {
  return {
    off: 'player',
    offPlayerId: null,
    offFamily: null,
    on: 'player',
    onPlayerId: null,
    onFamily: null,
  };
}

/** Whether the event is about one player the rule's substitutions can then take off (`subject`). */
export function eventHasSubject(event: string): boolean {
  return (
    event === EVENT_CODES.ownBooked ||
    event === EVENT_CODES.playingBadly ||
    event === EVENT_CODES.tired
  );
}

/** Whether the event may be narrowed to a position family. */
export function eventTakesFamily(event: string): boolean {
  return event === EVENT_CODES.ownBooked || event === EVENT_CODES.playingBadly;
}

/** Whether the event is judged against a threshold. */
export function eventTakesThreshold(event: string): boolean {
  return (
    event === EVENT_CODES.playingBadly ||
    event === EVENT_CODES.tired ||
    event === EVENT_CODES.opponentMentalityAtLeast ||
    event === EVENT_CODES.opponentMentalityAtMost
  );
}

/**
 * Changes the kind of event the rule waits for.
 *
 * The family and threshold the old kind used are cleared and the new kind gets its default, because the server refuses an event with a
 * parameter it does not take. A substitution that took off "the player the event is about" has no subject any more, so it goes back to
 * naming a player.
 */
export function withEvent(rule: MatchInstruction, event: string): MatchInstruction {
  const threshold =
    event === EVENT_CODES.playingBadly
      ? DEFAULT_BAD_RATING
      : event === EVENT_CODES.tired
        ? DEFAULT_TIRED_CONDITION
        : event === EVENT_CODES.opponentMentalityAtLeast
          ? MENTALITY_CODES.indexOf('positive')
          : event === EVENT_CODES.opponentMentalityAtMost
            ? MENTALITY_CODES.indexOf('cautious')
            : null;

  return {
    ...rule,
    event,
    eventFamily: null,
    eventThreshold: threshold,
    substitutions: eventHasSubject(event)
      ? rule.substitutions
      : rule.substitutions.map((substitution) =>
          substitution.off === 'subject' ? withOffSelector(substitution, 'player') : substitution,
        ),
  };
}

/** Changes how a substitution chooses the player to take off, clearing what the old way used. */
export function withOffSelector(
  substitution: MatchInstructionSubstitution,
  off: string,
): MatchInstructionSubstitution {
  const family = off === 'most_tired_in_family' || off === 'lowest_rated_in_family';

  return {
    ...substitution,
    off,
    offPlayerId: off === 'player' ? substitution.offPlayerId : null,
    offFamily: family ? (substitution.offFamily ?? 'midfield') : null,
  };
}

/** Changes how a substitution chooses the player to bring on, clearing what the old way used. */
export function withOnSelector(
  substitution: MatchInstructionSubstitution,
  on: string,
): MatchInstructionSubstitution {
  return {
    ...substitution,
    on,
    onPlayerId: on === 'player' ? substitution.onPlayerId : null,
    onFamily: on === 'best_fit_in_family' ? (substitution.onFamily ?? 'attack') : null,
  };
}

/** Changes one team instruction the rule sets, or clears it with null. */
export function withChange(
  rule: MatchInstruction,
  key: ChangeKey,
  value: string | null,
): MatchInstruction {
  return { ...rule, changes: { ...rule.changes, [key]: value } };
}

/**
 * Sets the mentality the rule adopts. Choosing one absolute mentality replaces a relative step, because the two read as one decision
 * ("Attacking", or "one step more defensive") and the engine applies the step after the absolute value.
 */
export function withMentality(rule: MatchInstruction, mentality: string | null): MatchInstruction {
  return { ...rule, changes: { ...rule.changes, mentality, mentalitySteps: 0 } };
}

/** Sets the mentality step the rule takes, clearing an absolute mentality for the same reason. */
export function withMentalitySteps(rule: MatchInstruction, steps: number): MatchInstruction {
  return { ...rule, changes: { ...rule.changes, mentality: null, mentalitySteps: steps } };
}

/** Switches the counter-attack on or off for the rest of the match, or leaves it as it is (null). */
export function withCounterAttackChange(
  rule: MatchInstruction,
  value: boolean | null,
): MatchInstruction {
  return { ...rule, changes: { ...rule.changes, counterAttack: value } };
}

/** Whether a rule changes any team instruction. */
export function changesAnything(changes: MatchInstructionChanges): boolean {
  return (
    changes.mentality !== null ||
    changes.mentalitySteps !== 0 ||
    changes.tempo !== null ||
    changes.passing !== null ||
    changes.width !== null ||
    changes.passFocus !== null ||
    changes.pressing !== null ||
    changes.defensiveLine !== null ||
    changes.tackling !== null ||
    changes.timeWasting !== null ||
    changes.counterAttack !== null
  );
}

/**
 * Moves the rule to a formation, or back to keeping the shape (null).
 *
 * The manager's placements are kept for as long as they still make sense: a player who is still in the lineup, in a slot the new shape
 * has and that is not the goalkeeper's. Choosing no formation drops them, because a placement with no new shape is refused.
 */
export function withFormationChoice(
  rule: MatchInstruction,
  formation: string | null,
  lineupPlayerIds: ReadonlySet<string>,
): MatchInstruction {
  return {
    ...rule,
    formation,
    overrides:
      formation === null
        ? []
        : rule.overrides.filter(
            (placement) =>
              lineupPlayerIds.has(placement.playerId) && placement.slotNumber > KEEPER_SLOT,
          ),
  };
}

/**
 * Places a player in a slot of the rule's new shape, or takes his placement back (null).
 *
 * A player and a slot each take one placement, so placing a player where another was, or elsewhere than before, replaces the old one.
 */
export function withPlacement(
  rule: MatchInstruction,
  playerId: string,
  slotNumber: number | null,
): MatchInstruction {
  const others: MatchInstructionOverride[] = rule.overrides.filter(
    (placement) => placement.playerId !== playerId && placement.slotNumber !== slotNumber,
  );

  return {
    ...rule,
    overrides: slotNumber === null ? others : [...others, { playerId, slotNumber }],
  };
}

/** Takes back every placement, leaving the best fit. */
export function withoutPlacements(rule: MatchInstruction): MatchInstruction {
  return { ...rule, overrides: [] };
}

/** Adds a substitution, unless the rule already makes as many as it may. */
export function withAddedSubstitution(rule: MatchInstruction): MatchInstruction {
  return rule.substitutions.length >= MAX_RULE_SUBSTITUTIONS
    ? rule
    : {
        ...rule,
        substitutions: [
          ...rule.substitutions,
          eventHasSubject(rule.event)
            ? withOffSelector(blankSubstitution(), 'subject')
            : blankSubstitution(),
        ],
      };
}

/** Replaces the substitution at an index. */
export function withSubstitution(
  rule: MatchInstruction,
  index: number,
  substitution: MatchInstructionSubstitution,
): MatchInstruction {
  return {
    ...rule,
    substitutions: rule.substitutions.map((current, place) =>
      place === index ? substitution : current,
    ),
  };
}

/** Removes the substitution at an index. */
export function withoutSubstitution(rule: MatchInstruction, index: number): MatchInstruction {
  return { ...rule, substitutions: rule.substitutions.filter((_, place) => place !== index) };
}

/** How many substitutions the enabled rules plan between them. */
export function plannedSubstitutions(rules: readonly MatchInstruction[]): number {
  return rules
    .filter((rule) => rule.isEnabled)
    .reduce((total, rule) => total + rule.substitutions.length, 0);
}

/** What a template needs to know about the plan it is added to. */
export interface TemplateContext {
  /** The formation the plan starts in. */
  readonly planFormation: string;

  /** Every formation preset code the manager can switch to. */
  readonly formations: readonly string[];
}

/** A rule the manager can start from, instead of a blank one. */
export interface InstructionTemplate {
  readonly key: string;
  readonly label: string;

  /** One line saying what the rule does, shown beside the choice. */
  readonly description: string;
  readonly build: (context: TemplateContext) => MatchInstruction;
}

/** Builds a rule from the blank one, so a template names only what it sets. */
function rule(name: string, set: Partial<MatchInstruction>): MatchInstruction {
  return { ...blankInstruction(name), ...set };
}

function steps(mentalitySteps: number): MatchInstructionChanges {
  return { ...noChanges(), mentalitySteps };
}

/** The formation a "planned change" moves to: a 4-3-3 when the plan is not in one already, otherwise the first other preset. */
function plannedFormation(context: TemplateContext): string | null {
  if (context.planFormation !== '4-3-3' && context.formations.includes('4-3-3')) {
    return '4-3-3';
  }

  return context.formations.find((code) => code !== context.planFormation) ?? null;
}

/** The templates the "Add instruction" menu offers, in the order it lists them. The last is the blank rule. */
export const INSTRUCTION_TEMPLATES: readonly InstructionTemplate[] = [
  {
    key: 'protect-lead',
    label: 'Protect a 2-goal lead',
    description:
      "From 70', leading by 2 or more: one step more defensive and time wasting when it suits.",
    build: () =>
      rule('Protect a 2-goal lead', {
        fromMinute: 70,
        score: 'leading',
        scoreMargin: 2,
        changes: { ...steps(-1), timeWasting: 'situational' },
      }),
  },
  {
    key: 'hold-lead',
    label: 'Hold a 1-goal lead late',
    description:
      "From 80', leading: one step more defensive and a fresh defender for the most tired forward.",
    build: () =>
      rule('Hold a 1-goal lead late', {
        fromMinute: 80,
        score: 'leading',
        scoreMargin: 1,
        changes: steps(-1),
        substitutions: [
          {
            off: 'most_tired_in_family',
            offPlayerId: null,
            offFamily: 'attack',
            on: 'best_fit_in_family',
            onPlayerId: null,
            onFamily: 'defence',
          },
        ],
      }),
  },
  {
    key: 'chase',
    label: "Chase the game from 60'",
    description:
      "From 60', losing: one step more attacking and a fresh forward for the most tired midfielder.",
    build: () =>
      rule("Chase the game from 60'", {
        fromMinute: 60,
        score: 'trailing',
        scoreMargin: 1,
        changes: steps(1),
        substitutions: [
          {
            off: 'most_tired_in_family',
            offPlayerId: null,
            offFamily: 'midfield',
            on: 'best_fit_in_family',
            onPlayerId: null,
            onFamily: 'attack',
          },
        ],
      }),
  },
  {
    key: 'all-out',
    label: "All-out attack from 85'",
    description: "From 85', not winning: Attacking, high press, high tempo.",
    build: () =>
      rule("All-out attack from 85'", {
        fromMinute: 85,
        score: 'not_winning',
        changes: { ...noChanges(), mentality: 'attacking', pressing: 'high_press', tempo: 'high' },
      }),
  },
  {
    key: 'ten-men',
    label: 'Down to ten men',
    description:
      'When one of your players is sent off: one step more defensive. The shape drops a forward.',
    build: () => rule('Down to ten men', { event: EVENT_CODES.ownSentOff, changes: steps(-1) }),
  },
  {
    key: 'opponent-ten',
    label: 'Opponent down to ten',
    description: 'When an opponent is sent off: one step more attacking and wider.',
    build: () =>
      rule('Opponent down to ten', {
        event: EVENT_CODES.opponentSentOff,
        changes: { ...steps(1), width: 'wide' },
      }),
  },
  {
    key: 'booked-defender',
    label: 'Booked defender off',
    description: "After 45', when a defender is booked: a fresh defender takes his place.",
    build: () =>
      rule('Booked defender off', {
        fromMinute: 45,
        event: EVENT_CODES.ownBooked,
        eventFamily: 'defence',
        substitutions: [
          {
            off: 'subject',
            offPlayerId: null,
            offFamily: null,
            on: 'best_fit_in_family',
            onPlayerId: null,
            onFamily: 'defence',
          },
        ],
      }),
  },
  {
    key: 'off-day',
    label: 'Off-day player off',
    description:
      "After 55', when a player's rating drops low: a fresh player of his kind comes on.",
    build: () =>
      rule('Off-day player off', {
        fromMinute: 55,
        event: EVENT_CODES.playingBadly,
        eventThreshold: DEFAULT_BAD_RATING,
        substitutions: [
          {
            off: 'subject',
            offPlayerId: null,
            offFamily: null,
            on: 'best_fit_in_family',
            onPlayerId: null,
            onFamily: 'midfield',
          },
        ],
      }),
  },
  {
    key: 'planned-change',
    label: 'Planned change at a minute',
    description: 'At a set minute, whatever the score: a new shape, to surprise the opponent.',
    build: (context) =>
      rule('Planned change', { fromMinute: 60, formation: plannedFormation(context) }),
  },
  {
    key: 'blank',
    label: 'Blank instruction',
    description: 'Start from nothing and set every part yourself.',
    build: () => blankInstruction(),
  },
];

/** One thing wrong with a match instruction, in the server's vocabulary so one set of words serves both. */
export interface InstructionProblem {
  /** The server's issue code, e.g. `INSTRUCTION_MINUTE_RANGE`, or `TOO_MANY_INSTRUCTIONS` for the list. */
  readonly code: string;

  /** The instruction's place in the list from 1, or null when the problem is about the list. */
  readonly ordinal: number | null;
  readonly playerId: string | null;
}

/** Whether the event's family and threshold are the ones its kind needs, and none it does not. Mirrors the server. */
function eventParametersAreValid(rule: MatchInstruction): boolean {
  const { eventThreshold: threshold, eventFamily: family } = rule;

  switch (rule.event) {
    case EVENT_CODES.none:
    case EVENT_CODES.ownSentOff:
    case EVENT_CODES.opponentSentOff:
      return threshold === null && family === null;
    case EVENT_CODES.ownBooked:
      return threshold === null;
    case EVENT_CODES.playingBadly:
      return (
        (threshold !== null || family !== null) &&
        (threshold === null || (threshold >= 0 && threshold <= MAX_THRESHOLD))
      );
    case EVENT_CODES.tired:
      return threshold !== null && threshold >= 0 && threshold <= MAX_THRESHOLD;
    case EVENT_CODES.opponentMentalityAtLeast:
    case EVENT_CODES.opponentMentalityAtMost:
      return (
        family === null &&
        threshold !== null &&
        Number.isInteger(threshold) &&
        threshold >= 0 &&
        threshold < MENTALITY_CODES.length
      );
    default:
      return false;
  }
}

/**
 * Finds what the server would refuse in a plan's match instructions, so the editor can say so before the manager saves.
 *
 * A named player to take off has to be in the plan's lineup and one to bring on on its bench; the server checks the same, and what a
 * club does when that player is unavailable on the day is the snapshot's to decide.
 */
export function instructionProblems(
  rules: readonly MatchInstruction[],
  lineupPlayerIds: ReadonlySet<string>,
  benchPlayerIds: ReadonlySet<string>,
): readonly InstructionProblem[] {
  const problems: InstructionProblem[] = [];

  if (rules.length > MAX_MATCH_INSTRUCTIONS) {
    problems.push({ code: 'TOO_MANY_INSTRUCTIONS', ordinal: null, playerId: null });
  }

  let substitutions = 0;
  let exceeded = false;

  rules.forEach((current, index) => {
    const ordinal = index + 1;
    const add = (code: string, playerId: string | null = null): void => {
      problems.push({ code, ordinal, playerId });
    };
    const name = current.name.trim();

    if (name.length === 0 || name.length > MAX_INSTRUCTION_NAME_LENGTH) {
      add('INSTRUCTION_NAME_INVALID');
    }

    if (
      current.fromMinute < 0 ||
      current.fromMinute > MAX_MINUTE ||
      (current.untilMinute !== null &&
        (current.untilMinute < current.fromMinute || current.untilMinute > MAX_MINUTE))
    ) {
      add('INSTRUCTION_MINUTE_RANGE');
    }

    if (current.scoreMargin < 1 || current.scoreMargin > MAX_SCORE_MARGIN) {
      add('INSTRUCTION_SCORE_MARGIN');
    }

    if (!eventParametersAreValid(current)) {
      add('INSTRUCTION_EVENT_PARAMETER');
    }

    if (Math.abs(current.changes.mentalitySteps) > MAX_MENTALITY_STEPS) {
      add('INSTRUCTION_MENTALITY_STEPS');
    }

    if (current.substitutions.length > MAX_RULE_SUBSTITUTIONS) {
      add('INSTRUCTION_TOO_MANY_SUBSTITUTIONS');
    }

    for (const substitution of current.substitutions) {
      substitutionProblems(substitution, current.event, lineupPlayerIds, benchPlayerIds, add);
    }

    overrideProblems(current, lineupPlayerIds, add);

    if (current.isEnabled) {
      substitutions += current.substitutions.length;
    }

    if (!exceeded && substitutions > MATCH_SUBSTITUTION_LIMIT) {
      exceeded = true;
      add('INSTRUCTIONS_SUBSTITUTION_LIMIT');
    }
  });

  return problems;
}

function substitutionProblems(
  substitution: MatchInstructionSubstitution,
  event: string,
  lineup: ReadonlySet<string>,
  bench: ReadonlySet<string>,
  add: (code: string, playerId?: string | null) => void,
): void {
  if (substitution.off === 'player') {
    if (substitution.offPlayerId === null) {
      add('INSTRUCTION_SUBSTITUTION_SELECTOR');
    } else if (!lineup.has(substitution.offPlayerId)) {
      add('INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP', substitution.offPlayerId);
    }
  } else if (substitution.off === 'subject') {
    if (!eventHasSubject(event)) {
      add('INSTRUCTION_SUBSTITUTION_SELECTOR');
    }
  } else if (substitution.offFamily === null) {
    add('INSTRUCTION_SUBSTITUTION_SELECTOR');
  }

  if (substitution.on === 'player') {
    if (substitution.onPlayerId === null) {
      add('INSTRUCTION_SUBSTITUTION_SELECTOR');
    } else if (!bench.has(substitution.onPlayerId)) {
      add('INSTRUCTION_ON_PLAYER_NOT_ON_BENCH', substitution.onPlayerId);
    }
  } else if (substitution.onFamily === null) {
    add('INSTRUCTION_SUBSTITUTION_SELECTOR');
  }
}

function overrideProblems(
  current: MatchInstruction,
  lineup: ReadonlySet<string>,
  add: (code: string, playerId?: string | null) => void,
): void {
  if (current.overrides.length === 0) {
    return;
  }

  if (current.formation === null) {
    add('INSTRUCTION_OVERRIDE_INVALID');

    return;
  }

  const players = new Set<string>();
  const slots = new Set<number>();

  for (const placement of current.overrides) {
    if (
      !lineup.has(placement.playerId) ||
      placement.slotNumber <= KEEPER_SLOT ||
      placement.slotNumber > 11 ||
      players.has(placement.playerId) ||
      slots.has(placement.slotNumber)
    ) {
      add('INSTRUCTION_OVERRIDE_INVALID', placement.playerId);
    }

    players.add(placement.playerId);
    slots.add(placement.slotNumber);
  }
}

/** The places in the list of enabled instructions that change no shape, no team instruction and make no substitution. */
export function inertInstructions(rules: readonly MatchInstruction[]): readonly number[] {
  return rules.flatMap((current, index) =>
    current.isEnabled &&
    current.formation === null &&
    !changesAnything(current.changes) &&
    current.substitutions.length === 0
      ? [index + 1]
      : [],
  );
}
