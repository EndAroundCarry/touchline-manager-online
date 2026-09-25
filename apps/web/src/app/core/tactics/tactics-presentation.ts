/**
 * Client-side presentation helpers for the tactics module.
 *
 * Small and pure, so the same labels, option lists, and issue wording are used by the pitch, the
 * accessible assignment table, and the instruction pickers — and so they can be unit tested without a
 * component. The codes are the server's (master plan §10.4); this file only names them.
 */

import type { TacticalPlanIssue } from './tactics.models';

/** An option in a select: the stable code, and the words a manager reads. */
export interface SelectOption {
  /** The stable code the server expects. */
  readonly value: string;

  /** The label shown in the control. */
  readonly label: string;
}

/** The eight instructions, their headings, and their allowed values (`INS-1`…`INS-8`). */
export interface InstructionField {
  /** The request/response property the instruction maps to. */
  readonly key:
    | 'mentality'
    | 'tempo'
    | 'passing'
    | 'width'
    | 'pressing'
    | 'defensiveLine'
    | 'tackling'
    | 'timeWasting';

  /** The heading shown above the control. */
  readonly label: string;

  /** The allowed values, in the order the rule set lists them. */
  readonly options: readonly SelectOption[];
}

/** The eight instruction pickers, in the order a manager reads them. */
export const INSTRUCTION_FIELDS: readonly InstructionField[] = [
  {
    key: 'mentality',
    label: 'Mentality',
    options: [
      { value: 'defensive', label: 'Defensive' },
      { value: 'cautious', label: 'Cautious' },
      { value: 'balanced', label: 'Balanced' },
      { value: 'positive', label: 'Positive' },
      { value: 'attacking', label: 'Attacking' },
    ],
  },
  {
    key: 'tempo',
    label: 'Tempo',
    options: [
      { value: 'low', label: 'Low' },
      { value: 'normal', label: 'Normal' },
      { value: 'high', label: 'High' },
    ],
  },
  {
    key: 'passing',
    label: 'Passing',
    options: [
      { value: 'short', label: 'Short' },
      { value: 'mixed', label: 'Mixed' },
      { value: 'direct', label: 'Direct' },
    ],
  },
  {
    key: 'width',
    label: 'Width',
    options: [
      { value: 'narrow', label: 'Narrow' },
      { value: 'normal', label: 'Normal' },
      { value: 'wide', label: 'Wide' },
    ],
  },
  {
    key: 'pressing',
    label: 'Pressing',
    options: [
      { value: 'low_block', label: 'Low block' },
      { value: 'mid_block', label: 'Mid block' },
      { value: 'high_press', label: 'High press' },
    ],
  },
  {
    key: 'defensiveLine',
    label: 'Defensive line',
    options: [
      { value: 'deep', label: 'Deep' },
      { value: 'normal', label: 'Normal' },
      { value: 'high', label: 'High' },
    ],
  },
  {
    key: 'tackling',
    label: 'Tackling',
    options: [
      { value: 'stay_on_feet', label: 'Stay on feet' },
      { value: 'normal', label: 'Normal' },
      { value: 'aggressive', label: 'Aggressive' },
    ],
  },
  {
    key: 'timeWasting',
    label: 'Time wasting',
    options: [
      { value: 'off', label: 'Off' },
      { value: 'situational', label: 'Situational' },
      { value: 'on', label: 'On' },
    ],
  },
];

/** The four position families and their headings, mirroring `PositionFamilies` on the server. */
const POSITION_FAMILY_LABELS: Record<string, string> = {
  goalkeeper: 'Goalkeeper',
  defence: 'Defence',
  midfield: 'Midfield',
  attack: 'Attack',
};

/** The family order the pitch and the assignment table render, own goal first. */
export const POSITION_FAMILY_ORDER: readonly string[] = [
  'goalkeeper',
  'defence',
  'midfield',
  'attack',
];

const ROLE_LABELS: Record<string, string> = {
  goalkeeper: 'Goalkeeper',
  centre_back: 'Centre back',
  full_back: 'Full back',
  wing_back: 'Wing back',
  defensive_midfielder: 'Defensive midfield',
  central_midfielder: 'Central midfield',
  attacking_midfielder: 'Attacking midfield',
  winger: 'Winger',
  striker: 'Striker',
};

/**
 * The roles each family may name (`TAC-8`).
 *
 * A slot's role and family must agree, and the server refuses a mismatch. Offering only the family's own
 * roles is how the screen makes a refused combination unpickable rather than reporting it afterwards.
 */
const ROLES_BY_FAMILY: Record<string, readonly string[]> = {
  goalkeeper: ['goalkeeper'],
  defence: ['centre_back', 'full_back', 'wing_back'],
  midfield: ['defensive_midfielder', 'central_midfielder', 'attacking_midfielder'],
  attack: ['winger', 'striker'],
};

/** Names a position family, falling back to the raw code so an unmodelled one is visible. */
export function familyLabel(code: string): string {
  return POSITION_FAMILY_LABELS[code] ?? code;
}

/** Names a role, falling back to the raw code. */
export function roleLabel(code: string): string {
  return ROLE_LABELS[code] ?? code;
}

/** The roles a family may name, as select options. */
export function rolesForFamily(family: string): readonly SelectOption[] {
  const roles = ROLES_BY_FAMILY[family] ?? [];

  return roles.map((role) => ({ value: role, label: roleLabel(role) }));
}

/** The tallest the pitch coordinate axis is, and the scale `TAC-9` stores positions on. */
const PITCH_MAX = 10_000;

/** The percentage of an axis a normalized coordinate sits at. */
function percent(value: number): string {
  return `${(value / 100).toFixed(2)}%`;
}

/**
 * Places a slot on the pitch.
 *
 * `x` is depth — 0 at the club's own goal line (`TAC-9`) — and the board draws the club attacking
 * upward, so depth becomes `bottom` and width becomes `left`. The same numbers the engine will hash are
 * the numbers on screen, which is why the board is a rendering of the layout rather than a second copy.
 */
export function pitchStyle(slot: {
  normalizedX: number;
  normalizedY: number;
}): Record<string, string> {
  return { bottom: percent(slot.normalizedX), left: percent(slot.normalizedY) };
}

/** Clamps a dropped position back inside the pitch (`TAC-9`). */
export function clampPitchCoordinate(value: number): number {
  if (Number.isNaN(value)) {
    return 0;
  }

  return Math.min(PITCH_MAX, Math.max(0, Math.round(value)));
}

/** Describes a validation issue in words, given the squad so a player can be named (`§10.4`). */
export function issueMessage(
  issue: TacticalPlanIssue,
  playerNameById: ReadonlyMap<string, string>,
): string {
  const slot = issue.slotNumber;
  const player =
    issue.playerId === null ? null : (playerNameById.get(issue.playerId) ?? 'that player');

  switch (issue.code) {
    case 'SLOT_COUNT':
      return 'A plan names exactly eleven slots.';
    case 'SLOT_NUMBER':
      return `Slot ${slot ?? ''} is outside the eleven.`;
    case 'DUPLICATE_SLOT_NUMBER':
      return `Two slots share number ${slot ?? ''}.`;
    case 'COORDINATE_OUT_OF_BOUNDS':
      return `Slot ${slot ?? ''} sits off the pitch.`;
    case 'OVERLAPPING_SLOTS':
      return `Slot ${slot ?? ''} sits on the same point as another slot.`;
    case 'ROLE_FAMILY_MISMATCH':
      return `Slot ${slot ?? ''}'s role does not match its position family.`;
    case 'DUPLICATE_PLAYER':
      return `${player} is picked in more than one slot.`;
    case 'PLAYER_NOT_ELIGIBLE':
      return `${player} is not a selectable member of your squad.`;
    case 'PLAYER_UNAVAILABLE':
      return `${player} is injured or suspended and cannot be picked (TRN-12).`;
    case 'SELECTION_INCOMPLETE':
      return 'Pick all eleven players, or leave the lineup empty — a half-filled side would field short (SQ-4).';
    default:
      return 'That plan is not valid.';
  }
}
