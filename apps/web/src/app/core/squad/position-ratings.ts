/**
 * Position-weighted attribute averages.
 *
 * A plain mean treats every attribute in a family as equally useful, which is wrong for a manager choosing a
 * player for a position: a centre back's tackling and marking matter far more to him than his dribbling, and
 * a winger's are the other way round. So each position names how much each of the twenty-eight attributes
 * matters to it, and a family's average is the *weighted* mean of its attributes under those weights.
 *
 * The weights are multipliers on four tiers, and an attribute the position does not single out keeps the
 * neutral multiplier:
 *
 * - **key** (×2): what the position is mainly judged on.
 * - **important** (×1.5): clearly useful.
 * - **normal** (×1): every attribute the position does not mention.
 * - **marginal** (×0.5): helps little or nothing in the position, e.g. a striker's tackling.
 *
 * The mean divides by the *sum of the weights*, not by the number of attributes, so it is a true weighted
 * average: it can never leave the 1–20 range the attributes live on, however many are doubled. (Multiplying
 * the doubled ones and dividing by the count would reward a position for having many key attributes, and
 * push an excellent player past the 20 ceiling.) A player who is 20 at everything scores 20 at every
 * position, and one who is 1 at everything scores 1.
 *
 * Still no single overall (`TRN-4`): there are four averages, one per family, because which family matters
 * most also depends on the position and the manager is the one who weighs them.
 */

import { ATTRIBUTE_COLUMNS, AttributeFamilyKey } from '../training/training-presentation';
import type { PlayerAttributes, SquadPlayer } from './squad.models';

/** How much an attribute matters to a position, from most to least. */
export type AttributeImportance = 'key' | 'important' | 'normal' | 'marginal';

/** The multiplier each importance carries. */
export const IMPORTANCE_WEIGHTS: Readonly<Record<AttributeImportance, number>> = {
  key: 2,
  important: 1.5,
  normal: 1,
  marginal: 0.5,
};

/** The highest and lowest value an attribute — and so an average of them — can take. */
export const ATTRIBUTE_MAX = 20;
const ATTRIBUTE_MIN = 1;

/** The attributes a position singles out; every attribute not listed here is `normal`. */
interface PositionProfile {
  readonly key: readonly string[];
  readonly important: readonly string[];
  readonly marginal: readonly string[];
}

/** The goalkeeper's own skills, which count for little in any outfield position. */
const OUTFIELD_GOALKEEPING = ['handling', 'reflexes', 'oneOnOnes', 'aerialAbility'] as const;

const FULL_BACK: PositionProfile = {
  key: ['pace', 'stamina', 'workRate'],
  important: ['tackling', 'marking', 'crossing', 'positioning', 'anticipation', 'acceleration'],
  marginal: ['finishing', 'heading', 'setPieces', 'vision', 'leadership', ...OUTFIELD_GOALKEEPING],
};

const WINGER: PositionProfile = {
  key: ['dribbling', 'pace', 'acceleration'],
  important: ['crossing', 'firstTouch', 'technique', 'agility'],
  marginal: [
    'tackling',
    'marking',
    'heading',
    'aggression',
    'leadership',
    'strength',
    'jumpingReach',
    ...OUTFIELD_GOALKEEPING,
  ],
};

/** The weights per position code. The keys are the codes the API sends (`gk`, `cb`, ...). */
const POSITION_PROFILES: Readonly<Record<string, PositionProfile>> = {
  gk: {
    key: ['handling', 'reflexes', 'positioning'],
    important: [
      'oneOnOnes',
      'aerialAbility',
      'decisions',
      'anticipation',
      'composure',
      'agility',
      'jumpingReach',
    ],
    marginal: [
      'finishing',
      'dribbling',
      'crossing',
      'setPieces',
      'technique',
      'tackling',
      'marking',
      'heading',
      'aggression',
      'pace',
      'acceleration',
    ],
  },
  cb: {
    key: ['tackling', 'marking', 'positioning'],
    important: [
      'heading',
      'strength',
      'jumpingReach',
      'pace',
      'anticipation',
      'decisions',
      'composure',
      'aggression',
    ],
    marginal: [
      'finishing',
      'crossing',
      'dribbling',
      'setPieces',
      'technique',
      'vision',
      ...OUTFIELD_GOALKEEPING,
    ],
  },
  rb: FULL_BACK,
  lb: FULL_BACK,
  dm: {
    key: ['tackling', 'positioning', 'stamina'],
    important: [
      'marking',
      'passing',
      'anticipation',
      'decisions',
      'workRate',
      'strength',
      'composure',
    ],
    marginal: [
      'finishing',
      'crossing',
      'dribbling',
      'setPieces',
      'pace',
      'acceleration',
      ...OUTFIELD_GOALKEEPING,
    ],
  },
  cm: {
    key: ['passing', 'vision', 'decisions', 'stamina'],
    important: ['firstTouch', 'technique', 'workRate', 'composure'],
    marginal: [
      'finishing',
      'crossing',
      'heading',
      'marking',
      'pace',
      'jumpingReach',
      ...OUTFIELD_GOALKEEPING,
    ],
  },
  am: {
    key: ['passing', 'vision', 'technique'],
    important: [
      'dribbling',
      'firstTouch',
      'finishing',
      'decisions',
      'composure',
      'agility',
      'acceleration',
    ],
    marginal: [
      'tackling',
      'marking',
      'heading',
      'aggression',
      'strength',
      'jumpingReach',
      ...OUTFIELD_GOALKEEPING,
    ],
  },
  rw: WINGER,
  lw: WINGER,
  st: {
    key: ['finishing', 'composure', 'positioning'],
    important: ['firstTouch', 'heading', 'anticipation', 'pace', 'acceleration', 'strength'],
    marginal: [
      'tackling',
      'marking',
      'crossing',
      'setPieces',
      'leadership',
      ...OUTFIELD_GOALKEEPING,
    ],
  },
};

/** The position an unmodelled code is rated as, so a new code still gets an average rather than none. */
const FALLBACK_PROFILE: PositionProfile = { key: [], important: [], marginal: [] };

/** How much an attribute matters to a position. */
export function attributeImportance(position: string, attribute: string): AttributeImportance {
  const profile = POSITION_PROFILES[position] ?? FALLBACK_PROFILE;

  if (profile.key.includes(attribute)) {
    return 'key';
  }

  if (profile.important.includes(attribute)) {
    return 'important';
  }

  return profile.marginal.includes(attribute) ? 'marginal' : 'normal';
}

/** The multiplier an attribute carries at a position. */
export function attributeWeight(position: string, attribute: string): number {
  return IMPORTANCE_WEIGHTS[attributeImportance(position, attribute)];
}

/** The four family averages, each on the 1–20 attribute scale. */
export interface PositionAverages {
  readonly goalkeeping: number;
  readonly technical: number;
  readonly mental: number;
  readonly physical: number;
}

/**
 * The weighted mean of one family's attributes at a position, to one decimal.
 *
 * Clamped to the attribute range as a guard: the weighted mean cannot leave it for in-range inputs, but a
 * payload with an out-of-range value must not put a 21 on screen.
 */
export function familyAverage(
  attributes: PlayerAttributes,
  family: AttributeFamilyKey,
  position: string,
): number {
  let weighted = 0;
  let totalWeight = 0;

  for (const column of ATTRIBUTE_COLUMNS) {
    if (column.family !== family) {
      continue;
    }

    const weight = attributeWeight(position, column.key);

    weighted += weight * column.value(attributes);
    totalWeight += weight;
  }

  const mean = totalWeight === 0 ? 0 : weighted / totalWeight;
  const clamped = Math.min(ATTRIBUTE_MAX, Math.max(ATTRIBUTE_MIN, mean));

  return Math.round(clamped * 10) / 10;
}

/** The four family averages for a player at a position (`TRN-4`: no overall). */
export function positionAverages(attributes: PlayerAttributes, position: string): PositionAverages {
  return {
    goalkeeping: familyAverage(attributes, 'goalkeeping', position),
    technical: familyAverage(attributes, 'technical', position),
    mental: familyAverage(attributes, 'mental', position),
    physical: familyAverage(attributes, 'physical', position),
  };
}

/** A squad player with their averages for the position they are listed at, ready for a sortable table. */
export type RatedPlayer = SquadPlayer & { readonly ratings: PositionAverages };

/** Rates a squad player at their primary position, the position the squad lists them under. */
export function ratedAtPrimary(player: SquadPlayer): RatedPlayer {
  return { ...player, ratings: positionAverages(player.attributes, player.primaryPosition) };
}

/** One attribute as the skills popup shows it: its value, and how much the position weights it. */
export interface SkillRow {
  readonly label: string;
  readonly value: number;

  /** The multiplier the position applies, so the popup can mark what counts most. */
  readonly weight: number;
  readonly importance: AttributeImportance;
}

/** One family's attributes, with the family's average for the position. */
export interface SkillGroup {
  readonly key: AttributeFamilyKey;
  readonly label: string;
  readonly average: number;
  readonly rows: readonly SkillRow[];
}

const FAMILY_HEADINGS: Readonly<Record<AttributeFamilyKey, string>> = {
  goalkeeping: 'Goalkeeping',
  technical: 'Technical',
  mental: 'Mental',
  physical: 'Physical',
};

/**
 * A player's attributes grouped by family, each row carrying the weight the position gives it.
 *
 * The family a position leans on most leads, so a goalkeeper's popup opens on goalkeeping and everyone
 * else's on technical.
 */
export function skillGroups(attributes: PlayerAttributes, position: string): readonly SkillGroup[] {
  const order: readonly AttributeFamilyKey[] =
    position === 'gk'
      ? ['goalkeeping', 'technical', 'mental', 'physical']
      : ['technical', 'mental', 'physical', 'goalkeeping'];

  return order.map((family) => ({
    key: family,
    label: FAMILY_HEADINGS[family],
    average: familyAverage(attributes, family, position),
    rows: ATTRIBUTE_COLUMNS.filter((column) => column.family === family).map((column) => ({
      label: column.label,
      value: column.value(attributes),
      weight: attributeWeight(position, column.key),
      importance: attributeImportance(position, column.key),
    })),
  }));
}
