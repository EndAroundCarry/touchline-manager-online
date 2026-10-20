/**
 * Client-side presentation helpers for the squad module.
 *
 * Small and pure, so they can be unit tested without a component and so the same band, label, and filter
 * rules are used by every screen. The one thing deliberately *not* here is sorting: the squad table is a
 * PrimeNG table (master plan §11.1) and PrimeNG owns sorting, including the `aria-sort` state that makes
 * it announceable.
 */

import { averageRatingLabel } from '../competition/competition-presentation';
import type {
  PlayerAttributes,
  PlayerMatchStat,
  PlayerSeasonStats,
  PlayerState,
  SquadPlayer,
} from './squad.models';

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
  low: { band: 'low', label: 'Low', className: 'text-red-300' },
  average: { band: 'average', label: 'Average', className: 'text-amber-300' },
  strong: { band: 'strong', label: 'Strong', className: 'text-emerald-300' },
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
  rm: 'midfield',
  lm: 'midfield',
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
  rm: 'Right midfield',
  lm: 'Left midfield',
};

const POSITION_ABBREVIATIONS: Record<string, string> = {
  gk: 'GK',
  rb: 'RB',
  cb: 'CB',
  lb: 'LB',
  dm: 'DM',
  cm: 'CM',
  am: 'AM',
  rw: 'RW',
  lw: 'LW',
  st: 'FC',
  rm: 'RM',
  lm: 'LM',
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

/**
 * The short code a table shows for a position (`FC` for a striker), with the full name left to
 * {@link positionLabel} for the hover. Falls back to the raw code on the same terms as that function.
 */
export function positionAbbreviation(code: string): string {
  return POSITION_ABBREVIATIONS[code] ?? code;
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

const RELEASE_BLOCKED_LABELS: Record<string, string> = {
  RELEASE_PLAYER_LISTED: 'This player is listed for transfer. Withdraw the listing first.',
  RELEASE_SQUAD_TOO_SMALL: 'A club needs at least 18 players, so this player cannot be released.',
  RELEASE_GOALKEEPERS_TOO_FEW:
    'A club needs at least two goalkeepers, so this player cannot be released.',
  RELEASE_SEASON_ROLLOVER: 'Contracts cannot change while the season rolls over. Try again soon.',
  RELEASE_INSUFFICIENT_FUNDS: 'Your available cash cannot cover the payout.',
};

/**
 * Says why a release is refused, from the server's stable code (`CON-12`). An unknown code still reads as a
 * refusal rather than as nothing.
 */
export function releaseBlockedLabel(code: string): string {
  return RELEASE_BLOCKED_LABELS[code] ?? 'This player cannot be released right now.';
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

  /** How far the attribute is towards its next point, between -1 and 1; 0 when it has made none (`TRN-10`). */
  readonly progress: number;
}

/**
 * Words for an attribute's progress towards its next point: two decimals, never reaching a whole point,
 * with a minus sign for progress towards a loss. Empty when there is none worth showing.
 */
export function progressText(progress: number): string {
  const magnitude = Math.min(0.99, Math.round(Math.abs(progress) * 100) / 100);

  if (magnitude === 0) {
    return '';
  }

  return `${progress < 0 ? '−' : ''}${magnitude.toFixed(2)}`;
}

/** What a screen reader hears for an attribute's progress, or an empty string when it has none to show. */
export function progressReadOut(progress: number, value: number): string {
  const text = progressText(progress);

  if (text === '') {
    return '';
  }

  return progress > 0
    ? `${text} of the way to ${value + 1}`
    : `${text.slice(1)} of the way to dropping to ${value - 1}`;
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
 * storage order. The progress map is the player's `attributeProgress`; an attribute missing from it has none.
 */
export function attributeGroups(
  attributes: PlayerAttributes,
  progress: Readonly<Record<string, number>> = {},
): readonly AttributeGroup[] {
  const row = (label: string, key: string, value: number): AttributeRow => ({
    label,
    value,
    progress: progress[key] ?? 0,
  });

  return [
    {
      key: 'goalkeeping',
      label: 'Goalkeeping',
      rows: [
        row('Handling', 'handling', attributes.goalkeeping.handling),
        row('Reflexes', 'reflexes', attributes.goalkeeping.reflexes),
        row('One-on-ones', 'oneOnOnes', attributes.goalkeeping.oneOnOnes),
        row('Aerial ability', 'aerialAbility', attributes.goalkeeping.aerialAbility),
      ],
    },
    {
      key: 'technical',
      label: 'Technical',
      rows: [
        row('Finishing', 'finishing', attributes.technical.finishing),
        row('Passing', 'passing', attributes.technical.passing),
        row('Crossing', 'crossing', attributes.technical.crossing),
        row('Dribbling', 'dribbling', attributes.technical.dribbling),
        row('First touch', 'firstTouch', attributes.technical.firstTouch),
        row('Tackling', 'tackling', attributes.technical.tackling),
        row('Marking', 'marking', attributes.technical.marking),
        row('Heading', 'heading', attributes.technical.heading),
        row('Technique', 'technique', attributes.technical.technique),
        row('Set pieces', 'setPieces', attributes.technical.setPieces),
      ],
    },
    {
      key: 'mental',
      label: 'Mental',
      rows: [
        row('Decisions', 'decisions', attributes.mental.decisions),
        row('Vision', 'vision', attributes.mental.vision),
        row('Positioning', 'positioning', attributes.mental.positioning),
        row('Composure', 'composure', attributes.mental.composure),
        row('Anticipation', 'anticipation', attributes.mental.anticipation),
        row('Work rate', 'workRate', attributes.mental.workRate),
        row('Aggression', 'aggression', attributes.mental.aggression),
        row('Leadership', 'leadership', attributes.mental.leadership),
      ],
    },
    {
      key: 'physical',
      label: 'Physical',
      rows: [
        row('Pace', 'pace', attributes.physical.pace),
        row('Acceleration', 'acceleration', attributes.physical.acceleration),
        row('Stamina', 'stamina', attributes.physical.stamina),
        row('Strength', 'strength', attributes.physical.strength),
        row('Agility', 'agility', attributes.physical.agility),
        row('Jumping reach', 'jumpingReach', attributes.physical.jumpingReach),
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
    {
      label: 'Condition',
      value: state.condition,
      band: stateBand(state.condition, true),
      higherIsBetter: true,
    },
    {
      label: 'Fatigue',
      value: state.fatigue,
      band: stateBand(state.fatigue, false),
      higherIsBetter: false,
    },
    {
      label: 'Morale',
      value: state.morale,
      band: stateBand(state.morale, true),
      higherIsBetter: true,
    },
    {
      label: 'Match sharpness',
      value: state.matchSharpness,
      band: stateBand(state.matchSharpness, true),
      higherIsBetter: true,
    },
  ];
}

/** One column of the Statistics tab's season table (`STA-2`). */
export interface StatColumn {
  /** The short heading. */
  readonly label: string;

  /** The full name, read out and shown on hover where the heading is abbreviated. */
  readonly title: string;

  /** Formats the column's cell from a season line. */
  readonly value: (stats: PlayerSeasonStats) => string;
}

/** One column of the Statistics tab's match-by-match table, on the same terms as {@link StatColumn}. */
export interface MatchStatColumn {
  /** The short heading. */
  readonly label: string;

  /** The full name, read out and shown on hover where the heading is abbreviated. */
  readonly title: string;

  /** Formats the column's cell from one match line. */
  readonly value: (match: PlayerMatchStat) => string;
}

/** Whether a position code (lower-case, as the API sends it) is the goalkeeper's. */
export function isGoalkeeperPosition(position: string | null | undefined): boolean {
  return position === 'gk';
}

const APPS: StatColumn = {
  label: 'Apps',
  title: 'Appearances',
  value: (stats) => `${stats.appearances}`,
};

const STARTS: StatColumn = {
  label: 'Starts',
  title: 'Starts',
  value: (stats) => `${stats.starts}`,
};

const MINUTES: StatColumn = {
  label: 'Min',
  title: 'Minutes played',
  value: (stats) => `${stats.minutesPlayed}`,
};

const GOALS: StatColumn = {
  label: 'Goals',
  title: 'Goals',
  value: (stats) => `${stats.goals}`,
};

const ASSISTS: StatColumn = {
  label: 'Assists',
  title: 'Assists',
  value: (stats) => `${stats.assists}`,
};

const PASSES: StatColumn = {
  label: 'Passes',
  title: 'Passes completed / attempted',
  value: (stats) => ratioLabel(stats.passesCompleted, stats.passesAttempted),
};

const PASS_PERCENT: StatColumn = {
  label: 'Pass %',
  title: 'Pass accuracy',
  value: (stats) => percentageLabel(stats.passesCompleted, stats.passesAttempted),
};

const YELLOW: StatColumn = {
  label: 'Yellow',
  title: 'Yellow cards',
  value: (stats) => `${stats.yellowCards}`,
};

const RED: StatColumn = {
  label: 'Red',
  title: 'Red cards',
  value: (stats) => `${stats.redCards}`,
};

const RATING: StatColumn = {
  label: 'Rating',
  title: 'Average rating',
  value: (stats) => averageRatingLabel(stats.averageRating),
};

/**
 * The columns of the season table for an outfield player, in the order a manager reads them: how much, what
 * they did, what it cost.
 *
 * One definition drives both the season rows and the career row, so the two cannot format a number
 * differently, and the passes, crosses, tackles, take-ons and set pieces read as `completed / attempted`
 * with their rate beside the ones that are not obvious from the pair.
 */
export const OUTFIELD_STAT_COLUMNS: readonly StatColumn[] = [
  APPS,
  STARTS,
  MINUTES,
  GOALS,
  ASSISTS,
  PASSES,
  PASS_PERCENT,
  {
    label: 'Crosses',
    title: 'Crosses completed / attempted, corners not counted',
    value: (stats) => ratioLabel(stats.crossesCompleted, stats.crossesAttempted),
  },
  {
    label: 'Tackles',
    title: 'Tackles won / attempted',
    value: (stats) => ratioLabel(stats.tacklesWon, stats.tacklesAttempted),
  },
  {
    label: 'Dribbles',
    title: 'Dribbles won / attempted',
    value: (stats) => ratioLabel(stats.dribblesCompleted, stats.dribblesAttempted),
  },
  {
    label: 'Dribble %',
    title: 'Dribble success',
    value: (stats) => percentageLabel(stats.dribblesCompleted, stats.dribblesAttempted),
  },
  { label: 'Shots', title: 'Shots', value: (stats) => `${stats.shots}` },
  {
    label: 'On tgt',
    title: 'Shots on target',
    value: (stats) => `${stats.shotsOnTarget}`,
  },
  {
    label: 'Pens',
    title: 'Penalties scored / taken',
    value: (stats) => ratioLabel(stats.penaltiesScored, stats.penaltiesTaken),
  },
  {
    label: 'Free kicks',
    title: 'Direct free kicks scored / taken',
    value: (stats) => ratioLabel(stats.freeKicksScored, stats.freeKicksTaken),
  },
  YELLOW,
  RED,
  RATING,
];

/**
 * The columns of the season table for a goalkeeper: the saves and penalties saved stand where an outfield
 * player's crosses, tackles, take-ons and shots would, since a keeper's season is told by the shots he faced.
 */
export const KEEPER_STAT_COLUMNS: readonly StatColumn[] = [
  APPS,
  STARTS,
  MINUTES,
  PASSES,
  PASS_PERCENT,
  {
    label: 'Saves',
    title: 'Saves / shots on target faced',
    value: (stats) => ratioLabel(stats.saves, stats.shotsOnTargetFaced),
  },
  {
    label: 'Save %',
    title: 'Save percentage',
    value: (stats) => percentageLabel(stats.saves, stats.shotsOnTargetFaced),
  },
  {
    label: 'Pens saved',
    title: 'Penalties saved / faced',
    value: (stats) => ratioLabel(stats.penaltiesSaved, stats.penaltiesFaced),
  },
  GOALS,
  ASSISTS,
  YELLOW,
  RED,
  RATING,
];

/** The season table's columns for a player of the given position (a lower-case code, `gk` for a keeper). */
export function statColumnsFor(position: string | null | undefined): readonly StatColumn[] {
  return isGoalkeeperPosition(position) ? KEEPER_STAT_COLUMNS : OUTFIELD_STAT_COLUMNS;
}

/** The cells of one season line, in the order of the given columns. */
export function statCells(
  stats: PlayerSeasonStats,
  columns: readonly StatColumn[],
): readonly string[] {
  return columns.map((column) => column.value(stats));
}

const MATCH_MINUTES: MatchStatColumn = {
  label: 'Min',
  title: 'Minutes played',
  value: (match) => `${match.minutesPlayed}`,
};

const MATCH_GOALS: MatchStatColumn = {
  label: 'Goals',
  title: 'Goals',
  value: (match) => `${match.goals}`,
};

const MATCH_ASSISTS: MatchStatColumn = {
  label: 'Assists',
  title: 'Assists',
  value: (match) => `${match.assists}`,
};

const MATCH_PASSES: MatchStatColumn = {
  label: 'Passes',
  title: 'Passes completed / attempted',
  value: (match) => ratioLabel(match.passesCompleted, match.passesAttempted),
};

const MATCH_YELLOW: MatchStatColumn = {
  label: 'Yellow',
  title: 'Yellow cards',
  value: (match) => `${match.yellowCards}`,
};

const MATCH_RED: MatchStatColumn = {
  label: 'Red',
  title: 'Red cards',
  value: (match) => `${match.redCards}`,
};

const MATCH_RATING: MatchStatColumn = {
  label: 'Rating',
  title: 'Match rating',
  value: (match) => averageRatingLabel(match.rating),
};

/** The match-by-match columns for an outfield player, after the round, the fixture and the result. */
export const OUTFIELD_MATCH_COLUMNS: readonly MatchStatColumn[] = [
  MATCH_MINUTES,
  MATCH_GOALS,
  MATCH_ASSISTS,
  MATCH_PASSES,
  {
    label: 'Crs',
    title: 'Crosses completed / attempted, corners not counted',
    value: (match) => ratioLabel(match.crossesCompleted, match.crossesAttempted),
  },
  {
    label: 'Tkl',
    title: 'Tackles won / attempted',
    value: (match) => ratioLabel(match.tacklesWon, match.tacklesAttempted),
  },
  {
    label: 'Dribbles',
    title: 'Dribbles won / attempted',
    value: (match) => ratioLabel(match.dribblesCompleted, match.dribblesAttempted),
  },
  { label: 'Shots', title: 'Shots', value: (match) => `${match.shots}` },
  {
    label: 'On tgt',
    title: 'Shots on target',
    value: (match) => `${match.shotsOnTarget}`,
  },
  {
    label: 'Pen',
    title: 'Penalties scored / taken',
    value: (match) => ratioLabel(match.penaltiesScored, match.penaltiesTaken),
  },
  {
    label: 'FK',
    title: 'Direct free kicks scored / taken',
    value: (match) => ratioLabel(match.freeKicksScored, match.freeKicksTaken),
  },
  MATCH_YELLOW,
  MATCH_RED,
  MATCH_RATING,
];

/** The match-by-match columns for a goalkeeper: saves and penalties saved in place of take-ons and shots. */
export const KEEPER_MATCH_COLUMNS: readonly MatchStatColumn[] = [
  MATCH_MINUTES,
  MATCH_GOALS,
  MATCH_ASSISTS,
  MATCH_PASSES,
  {
    label: 'Saves',
    title: 'Saves / shots on target faced',
    value: (match) => ratioLabel(match.saves, match.shotsOnTargetFaced),
  },
  {
    label: 'Pens',
    title: 'Penalties saved / faced',
    value: (match) => ratioLabel(match.penaltiesSaved, match.penaltiesFaced),
  },
  MATCH_YELLOW,
  MATCH_RED,
  MATCH_RATING,
];

/** The match-by-match columns for a player of the given position (a lower-case code, `gk` for a keeper). */
export function matchColumnsFor(position: string | null | undefined): readonly MatchStatColumn[] {
  return isGoalkeeperPosition(position) ? KEEPER_MATCH_COLUMNS : OUTFIELD_MATCH_COLUMNS;
}

/** The cells of one match line, in the order of the given columns. */
export function matchCells(
  match: PlayerMatchStat,
  columns: readonly MatchStatColumn[],
): readonly string[] {
  return columns.map((column) => column.value(match));
}

/** A completed count against an attempted one, as `completed / attempted` (`engine-v7`). */
export function ratioLabel(completed: number, attempted: number): string {
  return `${completed}/${attempted}`;
}

/**
 * A completed count as a whole percentage of an attempted one, or a dash when nothing was attempted — a
 * player who never took a dribble has no success rate rather than a rate of zero (`engine-v7`).
 */
function percentageLabel(completed: number, attempted: number): string {
  return attempted === 0 ? '\u2014' : `${Math.round((completed / attempted) * 100)}%`;
}

/** The number of seasons a player has appeared in, as a label (`STA-2`). */
export function seasonsPlayedLabel(seasonsPlayed: number): string {
  return `${seasonsPlayed} ${seasonsPlayed === 1 ? 'season' : 'seasons'}`;
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
