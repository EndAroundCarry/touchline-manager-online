/**
 * Client-side presentation helpers for the structures screen (`STRC-1`…`STRC-9`).
 *
 * Small and pure, so they can be unit tested without a component. The server owns every amount, bonus and
 * estimate; these helpers only name them, and decide in words why an upgrade cannot be ordered.
 */
import { MOOD_BANDS } from '../fans/fan-presentation';
import { preferredLocale, preferredTimeZone } from '../world/presentation';
import { Structure, StructureKind } from './structures.models';

/** The buildings in the order their tabs are shown. */
export const STRUCTURE_KINDS: readonly StructureKind[] = ['snack_bar', 'merchandise'];

const KIND_LABELS: Record<StructureKind, string> = {
  snack_bar: 'Snack bar',
  merchandise: 'Merchandise shop',
};

const RESULT_BONUS_LABELS: Record<string, string> = {
  underdog_win: 'Win as the underdog',
  even_win: 'Win in an even game',
  favourite_win: 'Win as the favourite',
  underdog_draw: 'Draw as the underdog',
  none: 'Any other result',
};

const RESULT_LABELS: Record<string, string> = { win: 'Won', draw: 'Drawn', loss: 'Lost' };

/** Names a building. */
export function kindLabel(kind: StructureKind): string {
  return KIND_LABELS[kind];
}

/** Who spends the amount a building quotes: a spectator at a home match, or a fan each week. */
export function customerPhrase(kind: StructureKind): string {
  return kind === 'snack_bar' ? 'per spectator at a home match' : 'per fan, each week';
}

/** Names one line of a bonus table: a result for the snack bar, a mood word for the shop. */
export function bonusLabel(reason: string): string {
  return (
    RESULT_BONUS_LABELS[reason] ?? MOOD_BANDS.find((band) => band.code === reason)?.phrase ?? reason
  );
}

/** Names a home result. */
export function resultLabel(result: string): string {
  return RESULT_LABELS[result] ?? result;
}

/** A bonus in basis points as a percentage that carries its sign: `+30%`, or `0%` for none. */
export function bonusPercent(bp: number): string {
  const percent = bp / 100;

  return percent > 0 ? `+${percent}%` : `${percent}%`;
}

/** The level pips, one per level, true for each level reached. */
export function levelPips(level: number, maxLevel: number): readonly boolean[] {
  return Array.from({ length: maxLevel }, (_, index) => index < level);
}

/** Names a week by its first day, e.g. "Week of 19 Oct". */
export function weekLabel(weekOf: string, locale = preferredLocale()): string {
  // A date-only value is a calendar day, not an instant, so it is read and shown in UTC and never shifted by the zone.
  const day = new Date(`${weekOf}T00:00:00Z`);

  return `Week of ${new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(day)}`;
}

/** When work finishes, e.g. "Thu 23 Oct, 14:00", in the manager's time zone. */
export function readyOn(
  instant: string,
  locale = preferredLocale(),
  timeZone = preferredTimeZone(),
): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone,
  }).format(new Date(instant));
}

/** How long an upgrade takes, e.g. "1 day" or "3 days". */
export function buildDaysPhrase(days: number): string {
  return `${days} ${days === 1 ? 'day' : 'days'}`;
}

/**
 * Why the next level cannot be ordered now, or null when it can.
 *
 * The server refuses the same cases (`STRC-3`); saying so before the click spares a round trip, and the reason
 * is shown beside the disabled button so the manager is never left guessing.
 */
export function upgradeBlockedReason(
  structure: Structure,
  availableCashMinor: number,
  canMutate: boolean,
): string | null {
  if (structure.building !== null) {
    return `Work on level ${structure.building.targetLevel} is already under way.`;
  }

  if (structure.next === null) {
    return 'This is the highest level there is.';
  }

  if (!canMutate) {
    return 'The game is in read-only mode.';
  }

  if (structure.next.costMinor > availableCashMinor) {
    return 'The club does not hold enough cash for this upgrade.';
  }

  return null;
}
