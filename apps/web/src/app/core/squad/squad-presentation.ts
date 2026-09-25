/**
 * Client-side presentation helpers for the squad module.
 *
 * Small and pure, so they can be unit tested without a component and so the same band, label, and filter
 * rules are used by every screen. The one thing deliberately *not* here is sorting: the squad table is a
 * PrimeNG table (master plan §11.1) and PrimeNG owns sorting, including the `aria-sort` state that makes
 * it announceable.
 */

import type { PlayerAttributes, PlayerState, SquadPlayer } from './squad.models';

/** How strong a value reads: the band is what gives colour-free meaning to an attribute or a state value. */
export type PerformanceBand = 'low' | 'average' | 'strong';

/** A band, its word, and the class that tints the number it belongs to. */
export interface BandStyle {
  /** The band identity. */
  readonly band: PerformanceBand;

  /** The word shown next to the number, so colour is never the only signal (master plan §11.3). */
  readonly label: string;

  /** The Tailwind text colour. */
  readonly className: string;
}

const BAND_STYLES: Record<PerformanceBand, BandStyle> = {
  low: { band: 'low', label: 'Low', className: 'text-red-700' },
  average: { band: 'average', label: 'Average', className: 'text-amber-700' },
  strong: { band: 'strong', label: 'Strong', className: 'text-emerald-800' },
};

/** The highest attribute value that still reads as weak (`TRN-4`'s scale is 1–20). */
const ATTRIBUTE_LOW_MAX = 6;

/** The highest attribute value that still reads as average. */
const ATTRIBUTE_AVERAGE_MAX = 13;

/** The lowest state value that reads as strong, on the 0–100 scale the API returns (`TRN-8`). */
const STATE_STRONG_MIN = 80;

/** The lowest state value that reads as average. */
const STATE_AVERAGE_MIN = 50;

/** Bands a displayed attribute, on the 1–20 scale (`TRN-4`). */
export function attributeBand(value: number): BandStyle {
  if (value <= ATTRIBUTE_LOW_MAX) {
    return BAND_STYLES.low;
  }

  return value <= ATTRIBUTE_AVERAGE_MAX ? BAND_STYLES.average : BAND_STYLES.strong;
}

/**
 * Bands a state value on the 0–100 scale the API returns (`TRN-8`).
 *
 * The direction matters: high condition is good and high fatigue is bad, so the same number means
 * opposite things on two columns of the same table.
 */
export function stateBand(value: number, higherIsBetter: boolean): BandStyle {
  const quality = higherIsBetter ? value : 100 - value;

  if (quality < STATE_AVERAGE_MIN) {
    return BAND_STYLES.low;
  }

  return quality < STATE_STRONG_MIN ? BAND_STYLES.average : BAND_STYLES.strong;
}

/** The position family a position code belongs to, mirroring `PlayerPositions.FamilyOf` on the server. */
export type PositionFamily = 'goalkeeper' | 'defence' | 'midfield' | 'attack';

const POSITION_FAMILIES: Record<string, PositionFamily> = {
  gk: 'goalkeeper',
  rb: 'defence',
  cb: 'defence',
  lb: 'defence',
  dm: 'midfield',
  cm: 'midfield',
  am: 'midfield',
  rw: 'attack',
  lw: 'attack',
  st: 'attack',
};

const POSITION_LABELS: Record<string, string> = {
  gk: 'Goalkeeper',
  rb: 'Right back',
  cb: 'Centre back',
  lb: 'Left back',
  dm: 'Defensive midfield',
  cm: 'Central midfield',
  am: 'Attacking midfield',
  rw: 'Right wing',
  lw: 'Left wing',
  st: 'Striker',
};

const FAMILY_LABELS: Record<PositionFamily, string> = {
  goalkeeper: 'Goalkeepers',
  defence: 'Defenders',
  midfield: 'Midfielders',
  attack: 'Attackers',
};

const SQUAD_STATUS_LABELS: Record<string, string> = {
  key_player: 'Key player',
  first_team: 'First team',
  rotation: 'Rotation',
  prospect: 'Prospect',
};

const FOOT_LABELS: Record<string, string> = {
  left: 'Left',
  right: 'Right',
  both: 'Either',
};

/** A position family and its label, for the filter control. */
export interface PositionFamilyOption {
  /** The family identity, or an empty string for "any". */
  readonly value: string;

  /** The label shown in the select. */
  readonly label: string;
}

/** The filter choices, in pitch order, with "any" first. */
export const POSITION_FAMILY_OPTIONS: readonly PositionFamilyOption[] = [
  { value: '', label: 'Any position' },
  { value: 'goalkeeper', label: FAMILY_LABELS.goalkeeper },
  { value: 'defence', label: FAMILY_LABELS.defence },
  { value: 'midfield', label: FAMILY_LABELS.midfield },
  { value: 'attack', label: FAMILY_LABELS.attack },
];

/** Names a position code, falling back to the raw code so an unmodelled one is visible rather than blank. */
export function positionLabel(code: string): string {
  return POSITION_LABELS[code] ?? code;
}

/** Names a position family, for grouping headers. */
export function familyLabel(family: PositionFamily): string {
  return FAMILY_LABELS[family];
}

/** Resolves the family a position code belongs to. */
export function positionFamilyOf(code: string): PositionFamily | null {
  return POSITION_FAMILIES[code] ?? null;
}

/** Names a squad status, falling back to the raw code. */
export function squadStatusLabel(code: string): string {
  return SQUAD_STATUS_LABELS[code] ?? code;
}

/** Names a preferred foot. */
export function footLabel(code: string): string {
  return FOOT_LABELS[code] ?? code;
}

/** Describes an injury or suspension in the unit it costs: fixtures, never days (`TRN-12`). */
export function availabilityLabel(type: string, remainingFixtures: number): string {
  const kind = type === 'suspension' ? 'Suspended' : 'Injured';
  const unit = remainingFixtures === 1 ? 'fixture' : 'fixtures';

  return `${kind} · ${remainingFixtures} ${unit}`;
}

/** One named attribute and its value. */
export interface AttributeRow {
  /** The attribute's name. */
  readonly label: string;

  /** The displayed value, 1–20. */
  readonly value: number;
}

/** One attribute family and its members, in the order the profile renders them (`TRN-2`). */
export interface AttributeGroup {
  /** The family identity. */
  readonly key: string;

  /** The family's heading. */
  readonly label: string;

  /** The family's attributes. */
  readonly rows: readonly AttributeRow[];
}

/**
 * Groups an attribute set into its four families.
 *
 * The order is the product's, not the payload's: a profile is read family by family, and a goalkeeper's
 * goalkeeping attributes are the ones a manager looks at first even though set pieces come first in the
 * storage order.
 */
export function attributeGroups(attributes: PlayerAttributes): readonly AttributeGroup[] {
  return [
    {
      key: 'goalkeeping',
      label: 'Goalkeeping',
      rows: [
        { label: 'Handling', value: attributes.goalkeeping.handling },
        { label: 'Reflexes', value: attributes.goalkeeping.reflexes },
        { label: 'One-on-ones', value: attributes.goalkeeping.oneOnOnes },
        { label: 'Aerial ability', value: attributes.goalkeeping.aerialAbility },
      ],
    },
    {
      key: 'technical',
      label: 'Technical',
      rows: [
        { label: 'Finishing', value: attributes.technical.finishing },
        { label: 'Passing', value: attributes.technical.passing },
        { label: 'Crossing', value: attributes.technical.crossing },
        { label: 'Dribbling', value: attributes.technical.dribbling },
        { label: 'First touch', value: attributes.technical.firstTouch },
        { label: 'Tackling', value: attributes.technical.tackling },
        { label: 'Marking', value: attributes.technical.marking },
        { label: 'Heading', value: attributes.technical.heading },
        { label: 'Technique', value: attributes.technical.technique },
        { label: 'Set pieces', value: attributes.technical.setPieces },
      ],
    },
    {
      key: 'mental',
      label: 'Mental',
      rows: [
        { label: 'Decisions', value: attributes.mental.decisions },
        { label: 'Vision', value: attributes.mental.vision },
        { label: 'Positioning', value: attributes.mental.positioning },
        { label: 'Composure', value: attributes.mental.composure },
        { label: 'Anticipation', value: attributes.mental.anticipation },
        { label: 'Work rate', value: attributes.mental.workRate },
        { label: 'Aggression', value: attributes.mental.aggression },
        { label: 'Leadership', value: attributes.mental.leadership },
      ],
    },
    {
      key: 'physical',
      label: 'Physical',
      rows: [
        { label: 'Pace', value: attributes.physical.pace },
        { label: 'Acceleration', value: attributes.physical.acceleration },
        { label: 'Stamina', value: attributes.physical.stamina },
        { label: 'Strength', value: attributes.physical.strength },
        { label: 'Agility', value: attributes.physical.agility },
        { label: 'Jumping reach', value: attributes.physical.jumpingReach },
      ],
    },
  ];
}

/** One state measure with its already-resolved band and direction. */
export interface StateRow {
  /** The measure's name. */
  readonly label: string;

  /** The user-facing value, 0–100 (`TRN-8`). */
  readonly value: number;

  /** The band, with the word and tint that keep colour from being the only signal. */
  readonly band: BandStyle;

  /** Whether a higher value is the better outcome, which fatigue inverts. */
  readonly higherIsBetter: boolean;
}

/** Builds the four state rows for a profile, in the order a manager reads them. */
export function stateRows(state: PlayerState): readonly StateRow[] {
  return [
    { label: 'Condition', value: state.condition, band: stateBand(state.condition, true), higherIsBetter: true },
    { label: 'Fatigue', value: state.fatigue, band: stateBand(state.fatigue, false), higherIsBetter: false },
    { label: 'Morale', value: state.morale, band: stateBand(state.morale, true), higherIsBetter: true },
    {
      label: 'Match sharpness',
      value: state.matchSharpness,
      band: stateBand(state.matchSharpness, true),
      higherIsBetter: true,
    },
  ];
}

/** What the squad table is currently showing, applied on the client (`SQ-3` bounds it to 25 rows). */
export interface SquadFilter {
  /** Free text matched against the player's name, case-insensitively. */
  readonly name: string;

  /** A position family, or an empty string for every position. */
  readonly positionFamily: string;

  /** When true, only players with no open injury or suspension. */
  readonly availableOnly: boolean;
}

/** The filter with nothing applied. */
export const NO_SQUAD_FILTER: SquadFilter = { name: '', positionFamily: '', availableOnly: false };

/**
 * Applies the squad filter.
 *
 * A pure function over the already-read squad rather than a server query: §10.3 bounds the response to 25
 * players and accepts client-side table work, and a round trip per keystroke would be a worse trade than
 * filtering twenty-two rows in memory.
 */
export function filterSquad(
  players: readonly SquadPlayer[],
  filter: SquadFilter,
): readonly SquadPlayer[] {
  const name = filter.name.trim().toLowerCase();

  return players.filter((player) => {
    if (name.length > 0 && !player.fullName.toLowerCase().includes(name)) {
      return false;
    }

    if (filter.positionFamily.length > 0) {
      const family = positionFamilyOf(player.primaryPosition);

      if (family !== filter.positionFamily) {
        return false;
      }
    }

    return !filter.availableOnly || player.availability.length === 0;
  });
}
