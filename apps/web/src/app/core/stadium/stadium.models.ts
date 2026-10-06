/**
 * The stadium's transport mirror (`STAD-1`…`STAD-6`).
 *
 * Hand-written to match `TouchlineManager.Contracts.World`, in minor units of the one canonical currency.
 * Prices, costs and crowds are never derived here; the server owns every piece of arithmetic that matters
 * and the screen only multiplies a quoted cost by the places a manager is considering.
 */

/** The four kinds of place, as the server's stable codes. */
export type StadiumStandCode = 'standing' | 'seating' | 'covered_seating' | 'vip';

/** One kind of place in the ground, with what it sells for and costs to add. */
export interface StadiumStand {
  readonly stand: StadiumStandCode;
  readonly seats: number;
  readonly ticketPriceMinor: number;
  readonly buildCostMinor: number;
  /** How many of these places a mid-table home match sells. Below `seats` means some sit empty. */
  readonly expectedSold: number;
}

/** A club's stadium. */
export interface Stadium {
  readonly clubId: string;
  readonly level: number;
  readonly maxLevel: number;
  readonly capacity: number;
  readonly maxCapacity: number;
  readonly seatsPerLevel: number;
  /** Places that move the ground up a level, which is when its picture changes. Zero at the top. */
  readonly seatsToNextLevel: number;
  readonly primaryColour: string;
  readonly secondaryColour: string;
  readonly stands: readonly StadiumStand[];
  readonly expectedDemand: number;
  readonly fullHouseMinor: number;
  readonly expectedGateMinor: number;
  readonly availableMinor: number;
  /** The strong entity tag a build order is made against. */
  readonly version: number;
  readonly serverTime: string;
}

/** An order to add places. */
export interface BuildSeatsRequest {
  readonly stand: StadiumStandCode;
  readonly count: number;
}
