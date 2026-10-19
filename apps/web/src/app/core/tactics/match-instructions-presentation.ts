/**
 * The words for match instructions (`MAT-6`): option lists for the editor's selects, the one-line summary a rule's card shows, and the
 * wording of what the server (or the editor's own checks) refuses.
 *
 * Small and pure, like `tactics-presentation.ts`, so the summary can be pinned by a unit test rather than found by looking at a page.
 */

import { EVENT_CODES, MENTALITY_CODES, type InstructionProblem } from './match-instructions';
import { COUNTER_ATTACK, INSTRUCTION_FIELDS, type SelectOption } from './tactics-presentation';
import type { MatchInstruction, MatchInstructionSubstitution } from './tactics.models';

/** The score conditions, in the order the editor lists them. */
export const SCORE_OPTIONS: readonly SelectOption[] = [
  { value: 'any', label: 'Any score' },
  { value: 'leading', label: 'Leading by' },
  { value: 'level', label: 'Level' },
  { value: 'trailing', label: 'Losing by' },
  { value: 'not_winning', label: 'Not winning' },
  { value: 'not_losing', label: 'Not losing' },
];

/** The events a rule can wait for, in the order the editor lists them. */
export const EVENT_OPTIONS: readonly SelectOption[] = [
  { value: EVENT_CODES.none, label: 'Nothing in particular' },
  { value: EVENT_CODES.ownSentOff, label: 'One of my players is sent off' },
  { value: EVENT_CODES.opponentSentOff, label: 'An opponent is sent off' },
  { value: EVENT_CODES.ownBooked, label: 'One of my players is booked' },
  { value: EVENT_CODES.playingBadly, label: 'A player is playing badly' },
  { value: EVENT_CODES.tired, label: 'A player is tired' },
  { value: EVENT_CODES.opponentMentalityAtLeast, label: "The opponent's mentality is at least" },
  { value: EVENT_CODES.opponentMentalityAtMost, label: "The opponent's mentality is at most" },
];

/** The outfield families a rule can aim at. The goalkeeper is named, never picked by a family. */
export const OUTFIELD_FAMILY_OPTIONS: readonly SelectOption[] = [
  { value: 'defence', label: 'Defence' },
  { value: 'midfield', label: 'Midfield' },
  { value: 'attack', label: 'Attack' },
];

/** How a substitution chooses the player to take off. */
export const OFF_OPTIONS: readonly SelectOption[] = [
  { value: 'player', label: 'A named player' },
  { value: 'subject', label: 'The player the event is about' },
  { value: 'most_tired_in_family', label: 'The most tired player of a family' },
  { value: 'lowest_rated_in_family', label: 'The lowest rated player of a family' },
];

/** How a substitution chooses the player to bring on. */
export const ON_OPTIONS: readonly SelectOption[] = [
  { value: 'player', label: 'A named substitute' },
  { value: 'best_fit_in_family', label: 'The freshest substitute who fits' },
];

/** The mentality choices of a rule: no change, a relative step, or an absolute mentality. */
export const MENTALITY_CHANGE_OPTIONS: readonly SelectOption[] = [
  { value: 'steps:0', label: 'No change' },
  { value: 'steps:2', label: 'Two steps more attacking' },
  { value: 'steps:1', label: 'One step more attacking' },
  { value: 'steps:-1', label: 'One step more defensive' },
  { value: 'steps:-2', label: 'Two steps more defensive' },
  ...INSTRUCTION_FIELDS[0].options.map((option) => ({
    value: `set:${option.value}`,
    label: option.label,
  })),
];

/** The word for one player of a family. */
export function familyNoun(family: string): string {
  switch (family) {
    case 'goalkeeper':
      return 'goalkeeper';
    case 'defence':
      return 'defender';
    case 'midfield':
      return 'midfielder';
    case 'attack':
      return 'forward';
    default:
      return 'player';
  }
}

/** A rating threshold in basis points as the number a manager reads: 5,700 is 5.7. */
export function ratingLabel(basisPoints: number): string {
  return (basisPoints / 1000).toFixed(1);
}

/** A condition threshold in basis points as a percentage. */
export function conditionLabel(basisPoints: number): string {
  return `${Math.round(basisPoints / 100)}%`;
}

function mentalityLabel(index: number | null): string {
  const code = index === null ? undefined : MENTALITY_CODES[index];

  return (
    INSTRUCTION_FIELDS[0].options.find((option) => option.value === code)?.label ?? 'a mentality'
  );
}

function capitalise(text: string): string {
  return text.length === 0 ? text : text[0].toUpperCase() + text.slice(1);
}

/** What the rule waits for, in words: the minutes, the score and the event. */
function whenText(rule: MatchInstruction): string {
  const minutes =
    rule.untilMinute !== null
      ? rule.fromMinute === 0
        ? `until ${rule.untilMinute}'`
        : `from ${rule.fromMinute}' to ${rule.untilMinute}'`
      : rule.fromMinute > 0
        ? `from ${rule.fromMinute}'`
        : null;
  const conditions = [scoreText(rule), eventText(rule)].filter(
    (text): text is string => text !== null,
  );

  if (minutes === null && conditions.length === 0) {
    return 'From kick-off';
  }

  if (conditions.length === 0) {
    return capitalise(minutes ?? '');
  }

  const joined = conditions.join(' and ');

  return minutes === null ? `If ${joined}` : `${capitalise(minutes)}, if ${joined}`;
}

function scoreText(rule: MatchInstruction): string | null {
  switch (rule.score) {
    case 'leading':
      return `leading by ${rule.scoreMargin} or more`;
    case 'trailing':
      return `losing by ${rule.scoreMargin} or more`;
    case 'level':
      return 'level';
    case 'not_winning':
      return 'not winning';
    case 'not_losing':
      return 'not losing';
    default:
      return null;
  }
}

function eventText(rule: MatchInstruction): string | null {
  const noun = rule.eventFamily === null ? 'player' : familyNoun(rule.eventFamily);

  switch (rule.event) {
    case EVENT_CODES.ownSentOff:
      return 'one of your players is sent off';
    case EVENT_CODES.opponentSentOff:
      return 'an opponent is sent off';
    case EVENT_CODES.ownBooked:
      return `one of your ${rule.eventFamily === null ? 'players' : `${noun}s`} is booked`;
    case EVENT_CODES.playingBadly:
      return rule.eventThreshold === null
        ? `the lowest-rated ${noun} is playing badly`
        : `a ${noun} is rated below ${ratingLabel(rule.eventThreshold)}`;
    case EVENT_CODES.tired:
      return `a player's condition drops below ${conditionLabel(rule.eventThreshold ?? 0)}`;
    case EVENT_CODES.opponentMentalityAtLeast:
      return `the opponent's mentality is at least ${mentalityLabel(rule.eventThreshold)}`;
    case EVENT_CODES.opponentMentalityAtMost:
      return `the opponent's mentality is at most ${mentalityLabel(rule.eventThreshold)}`;
    default:
      return null;
  }
}

/** The words for a team instruction a rule sets. */
function changeText(key: string, code: string): string | null {
  const field = INSTRUCTION_FIELDS.find((candidate) => candidate.key === key);
  const option = field?.options.find((candidate) => candidate.value === code);

  if (field === undefined || option === undefined) {
    return null;
  }

  switch (field.key) {
    case 'mentality':
      return option.label;
    case 'pressing':
      return option.label.toLowerCase();
    default:
      return `${field.label.toLowerCase()} ${option.label.toLowerCase()}`;
  }
}

function stepsText(steps: number): string | null {
  if (steps === 0) {
    return null;
  }

  const count = Math.abs(steps) === 1 ? 'one step' : 'two steps';

  return `${count} more ${steps > 0 ? 'attacking' : 'defensive'}`;
}

function substitutionText(
  substitution: MatchInstructionSubstitution,
  event: string,
  names: ReadonlyMap<string, string>,
): string {
  const on =
    substitution.on === 'best_fit_in_family'
      ? `a fresh ${familyNoun(substitution.onFamily ?? '')}`
      : ((substitution.onPlayerId === null ? undefined : names.get(substitution.onPlayerId)) ??
        'a substitute');
  const family = familyNoun(substitution.offFamily ?? '');
  const subject =
    event === EVENT_CODES.ownBooked
      ? 'booked'
      : event === EVENT_CODES.tired
        ? 'tired'
        : 'badly playing';
  const off =
    substitution.off === 'subject'
      ? `the ${subject} player`
      : substitution.off === 'most_tired_in_family'
        ? `the most tired ${family}`
        : substitution.off === 'lowest_rated_in_family'
          ? `the lowest-rated ${family}`
          : ((substitution.offPlayerId === null
              ? undefined
              : names.get(substitution.offPlayerId)) ?? 'a player');

  return `${on} on for ${off}`;
}

/** What the rule does, in words: the shape, the team instructions and the substitutions. */
function thenText(rule: MatchInstruction, names: ReadonlyMap<string, string>): string {
  const parts: string[] = [];
  const { changes } = rule;

  if (rule.formation !== null) {
    parts.push(rule.formation);
  }

  if (changes.mentality !== null) {
    parts.push(changeText('mentality', changes.mentality) ?? changes.mentality);
  }

  const steps = stepsText(changes.mentalitySteps);

  if (steps !== null) {
    parts.push(steps);
  }

  for (const field of INSTRUCTION_FIELDS) {
    const value = field.key === 'mentality' ? null : changes[field.key];

    if (value !== null) {
      const text = changeText(field.key, value);

      if (text !== null) {
        parts.push(text);
      }
    }
  }

  if (changes.counterAttack !== null) {
    parts.push(`${COUNTER_ATTACK.label.toLowerCase()} ${changes.counterAttack ? 'on' : 'off'}`);
  }

  for (const substitution of rule.substitutions) {
    parts.push(substitutionText(substitution, rule.event, names));
  }

  return parts.length === 0 ? 'changes nothing' : parts.join(', ');
}

/**
 * One line saying when a rule fires and what it does, e.g. "From 60', if losing by 1 or more → 4-2-4, Attacking, high press, Okafor on
 * for the most tired midfielder".
 *
 * @param names Player names by id, for the substitutions that name someone.
 */
export function instructionSummary(
  rule: MatchInstruction,
  names: ReadonlyMap<string, string>,
): string {
  return `${whenText(rule)} → ${thenText(rule, names)}`;
}

/** Describes what is wrong with a match instruction, without saying which one: the caller names it. */
export function instructionIssueMessage(
  problem: Pick<InstructionProblem, 'code' | 'playerId'>,
  names: ReadonlyMap<string, string>,
): string {
  const player = problem.playerId === null ? null : (names.get(problem.playerId) ?? 'That player');

  switch (problem.code) {
    case 'TOO_MANY_INSTRUCTIONS':
      return 'A plan holds at most ten match instructions.';
    case 'INSTRUCTION_NAME_INVALID':
      return 'Give the instruction a name of up to 48 characters.';
    case 'INSTRUCTION_MINUTE_RANGE':
      return 'The minutes must be between 0 and 90, and the last minute cannot be before the first.';
    case 'INSTRUCTION_SCORE_MARGIN':
      return 'The goal margin must be between 1 and 5.';
    case 'INSTRUCTION_EVENT_PARAMETER':
      return 'Finish the event: it needs a rating, a condition or a mentality, or a family, in range.';
    case 'INSTRUCTION_MENTALITY_STEPS':
      return 'The mentality can move by at most two steps.';
    case 'INSTRUCTION_TOO_MANY_SUBSTITUTIONS':
      return 'An instruction makes at most three substitutions.';
    case 'INSTRUCTION_SUBSTITUTION_SELECTOR':
      return 'A substitution is missing a player or a family, or takes off "the player the event is about" with no such event.';
    case 'INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP':
      return `${player ?? 'The player to take off'} is not in this plan's starting eleven.`;
    case 'INSTRUCTION_ON_PLAYER_NOT_ON_BENCH':
      return `${player ?? 'The player to bring on'} is not on this plan's bench.`;
    case 'INSTRUCTION_OVERRIDE_INVALID':
      return `${player ?? 'A placement'} cannot be placed there: it needs a new formation, a starter or a substitute this instruction brings on, and a free slot other than the goalkeeper's.`;
    case 'INSTRUCTIONS_SUBSTITUTION_LIMIT':
      return 'Between them the instructions plan more than five substitutions, the most a club may make. Turn one off or remove a substitution.';
    case 'INSTRUCTION_DOES_NOTHING':
      return 'This instruction changes no shape, no team instruction and makes no substitution.';
    default:
      return 'That instruction is not valid.';
  }
}

/** Whether an issue code is about match instructions rather than the slots and lineup. */
export function isInstructionIssue(code: string): boolean {
  return code === 'TOO_MANY_INSTRUCTIONS' || code.startsWith('INSTRUCTION');
}

/** The family names for a select that may also say "any". */
export function eventFamilyOptions(): readonly SelectOption[] {
  return [{ value: '', label: 'Any outfield player' }, ...OUTFIELD_FAMILY_OPTIONS];
}
