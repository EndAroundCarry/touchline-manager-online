import { focusFamilyLabel, intensityLabel, teamFocusLabel } from './training-presentation';

/**
 * One session in a player's training history.
 *
 * PLACEHOLDER: training is being reworked, so there is no server read for a player's past sessions yet.
 * {@link sampleTrainingHistory} stands in with deterministic dummy data so the Training report tab can be
 * built and reviewed. When the rework lands, the tab keeps this shape and swaps the source for a store read.
 */
export interface TrainingHistoryEntry {
  /** A stable key for the session. */
  readonly id: string;

  /** The season the session belongs to, as a number. */
  readonly seasonNumber: number;

  /** The matchday week within that season. */
  readonly week: number;

  /** The club-wide focus that week, already worded. */
  readonly teamFocus: string;

  /** The player's individual focus family that week, or "Team plan". */
  readonly individualFocus: string;

  /** The training intensity, already worded. */
  readonly intensity: string;

  /** What the session did to the player, already worded. */
  readonly outcome: string;
}

const TEAM_FOCUS_CYCLE = ['balanced', 'fitness', 'technical', 'tactical', 'recovery'];
const INTENSITY_CYCLE = ['normal', 'intense', 'light', 'normal'];
const FAMILY_CYCLE = ['', 'technical', 'physical', 'mental', ''];
const OUTCOMES = [
  'No visible change',
  '+1 Passing',
  'Condition recovered',
  '+1 Stamina',
  'No visible change',
  '+1 Decisions',
  'Slight fatigue',
  '+1 Finishing',
];

/** A small stable hash of a string, so a player always gets the same sample history. */
function seedOf(text: string): number {
  let hash = 0;

  for (let index = 0; index < text.length; index++) {
    hash = (hash * 31 + text.charCodeAt(index)) >>> 0;
  }

  return hash;
}

/**
 * Builds a deterministic, clearly-sample training history for a player, newest first.
 *
 * @param playerId The player's identity, which seeds the history so it is stable between visits.
 * @param currentSeason The season to count back from.
 * @param weeks How many sessions to produce.
 */
export function sampleTrainingHistory(
  playerId: string,
  currentSeason: number,
  weeks = 12,
): readonly TrainingHistoryEntry[] {
  const seed = seedOf(playerId);
  const entries: TrainingHistoryEntry[] = [];

  for (let offset = 0; offset < weeks; offset++) {
    const weekNumber = weeks - offset;
    const pick = seed + offset;
    const family = FAMILY_CYCLE[pick % FAMILY_CYCLE.length];

    entries.push({
      id: `${playerId}-${currentSeason}-${weekNumber}`,
      seasonNumber: currentSeason,
      week: weekNumber,
      teamFocus: teamFocusLabel(TEAM_FOCUS_CYCLE[pick % TEAM_FOCUS_CYCLE.length]),
      individualFocus: family === '' ? 'Team plan' : focusFamilyLabel(family),
      intensity: intensityLabel(INTENSITY_CYCLE[(pick >> 2) % INTENSITY_CYCLE.length]),
      outcome: OUTCOMES[(pick >> 1) % OUTCOMES.length],
    });
  }

  return entries;
}
