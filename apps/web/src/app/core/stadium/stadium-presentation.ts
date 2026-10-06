/**
 * Client-side presentation helpers for the stadium screen (`STAD-1`…`STAD-6`).
 *
 * Small and pure, so they can be unit tested without a component. The server owns every price, cost and
 * crowd; these helpers only name the stands, describe a level in places, and multiply a quoted cost by the
 * number a manager is considering.
 */
import { StadiumStand, StadiumStandCode } from './stadium.models';

/** How a stand is named on screen. */
export interface StandLabel {
  /** The heading, e.g. "Covered seating". */
  readonly name: string;
  /** What one place is called when there is one. */
  readonly singular: string;
  /** What several places are called. */
  readonly plural: string;
  /** One sentence on what the place is. */
  readonly blurb: string;
}

const STAND_LABELS: Readonly<Record<StadiumStandCode, StandLabel>> = {
  standing: {
    name: 'Standing',
    singular: 'standing place',
    plural: 'standing places',
    blurb: 'Terraces open to the weather. The cheapest ticket in the ground.',
  },
  seating: {
    name: 'Seating',
    singular: 'seat',
    plural: 'seats',
    blurb: 'Seats in the open air.',
  },
  covered_seating: {
    name: 'Covered seating',
    singular: 'covered seat',
    plural: 'covered seats',
    blurb: 'Seats under the roof, out of the rain.',
  },
  vip: {
    name: 'VIP',
    singular: 'VIP seat',
    plural: 'VIP seats',
    blurb: 'Hospitality seats. Few of them, and the highest price.',
  },
};

/** Names a stand, falling back to its code so a kind this build does not know still renders. */
export function standLabel(code: string): StandLabel {
  return (
    STAND_LABELS[code as StadiumStandCode] ?? {
      name: code,
      singular: 'place',
      plural: 'places',
      blurb: '',
    }
  );
}

/** The plural name for a count: "1 seat", "10 seats". */
export function placesPhrase(code: string, count: number): string {
  const label = standLabel(code);

  return `${count.toLocaleString()} ${count === 1 ? label.singular : label.plural}`;
}

/** The range of places a level covers, e.g. level 2 is 5,001 to 10,000. */
export function levelRange(
  level: number,
  seatsPerLevel: number,
): { readonly from: number; readonly to: number } {
  return { from: (level - 1) * seatsPerLevel + 1, to: level * seatsPerLevel };
}

/** What the order would cost, in minor units: the quoted cost of one place times the places wanted. */
export function orderCost(stand: StadiumStand, count: number): number {
  return Number.isFinite(count) && count > 0 ? stand.buildCostMinor * Math.floor(count) : 0;
}

/**
 * How full a stand is at a mid-table home match, 0–100.
 *
 * Below 100 means some of the stand sits empty, which is what a manager weighs before building more of it.
 */
export function fillPercent(stand: StadiumStand): number {
  return stand.seats === 0
    ? 0
    : Math.min(100, Math.round((stand.expectedSold / stand.seats) * 100));
}

/** The progress through the current level toward the next, 0–100, for the level bar. */
export function levelProgress(capacity: number, level: number, seatsPerLevel: number): number {
  const range = levelRange(level, seatsPerLevel);

  return Math.min(
    100,
    Math.max(0, Math.round(((capacity - range.from + 1) / seatsPerLevel) * 100)),
  );
}
