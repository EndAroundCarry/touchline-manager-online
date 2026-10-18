/**
 * Fits the players of a lineup into a new formation: who takes which slot when a match instruction changes the shape (`MAT-6`).
 *
 * The third copy of one algorithm. The match engine fits whoever is on the pitch when an instruction fires, the domain fits a plan's
 * lineup on the server, and this is the live preview the Match instructions tab draws while the manager edits. One table of cases
 * (`tests/shared/shape-fit-cases.json`) pins all three, so a change to the numbers or the tie-breaks has to be made in each.
 *
 * Pure and without a random draw: a greedy assignment followed by a swap pass. Every (player, slot) pair has a cost, pairs are taken
 * cheapest first, and then any two players who cost less swapped trade places. The cost is the player's unfamiliarity with the slot,
 * weighted by {@link FAMILIARITY_WEIGHT}, plus how far the slot is from where he stands now.
 */

import { positionFamilyOf } from '../squad/squad-presentation';
import { isNaturalForRole } from './tactics-presentation';

/** A player wholly at home in the slot plays at full strength, in basis points. */
const CERTAIN = 10_000;

/** A player in the slot's family but not a position its role asks for, in basis points: the engine's unfamiliar-role penalty. */
const UNFAMILIAR_ROLE_BASIS_POINTS = 9_400;

/** A player covered by a secondary position, or by a position the role names, in basis points: the engine's secondary-position penalty. */
const SECONDARY_POSITION_BASIS_POINTS = 9_600;

/** A player outside the slot's family altogether, in basis points: the engine's out-of-position penalty. */
const OUT_OF_POSITION_BASIS_POINTS = 8_800;

/**
 * How many times a basis point of unfamiliarity weighs against a unit of the 0…10,000 board distance. The penalties are small (a
 * player out of position plays at 88%, a gap of 1,200), so at 5 being out of position costs as much as a walk of 60% of the pitch's
 * length.
 */
const FAMILIARITY_WEIGHT = 5;

/** How much a unit across the pitch counts against a unit along it, in percent: the pitch is 68 m wide and 105 m long. */
const LATERAL_DISTANCE_PERCENT = 65;

/** The position family of a goalkeeper. */
const GOALKEEPER_FAMILY = 'goalkeeper';

/** A slot of a shape: its number from 1, the family and role it asks for, and where it stands on the 0…10,000 board. */
export interface ShapeFitSlot {
  readonly slot: number;
  readonly family: string;
  readonly role: string;

  /** Depth: 0 at the club's own goal line. */
  readonly x: number;

  /** Width across the pitch. */
  readonly y: number;
}

/** A player on the pitch, as the fit sees him: who he is, where he plays, and the slot he fills now. */
export interface ShapeFitPlayer {
  readonly id: string;

  /** His primary position code. */
  readonly position: string;

  /** His other position codes. */
  readonly secondary: readonly string[];

  /** The slot he fills now. */
  readonly standing: Omit<ShapeFitSlot, 'slot'>;
}

/**
 * Compares two player ids the way the server does. A canonical lower-case id sorts in the same order as .NET's `Guid.CompareTo`, so a
 * tie breaks on the same player in all three copies.
 */
function compareIds(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

/** How well a player suits a slot, as a basis-point multiplier: the engine's familiarity grading (`INS-10`). */
export function familiarityOf(player: ShapeFitPlayer, slot: ShapeFitSlot): number {
  if (positionFamilyOf(player.position) === slot.family) {
    return isNaturalForRole(slot.role, player.position) ? CERTAIN : UNFAMILIAR_ROLE_BASIS_POINTS;
  }

  if (
    isNaturalForRole(slot.role, player.position) ||
    player.secondary.some(
      (position) =>
        positionFamilyOf(position) === slot.family || isNaturalForRole(slot.role, position),
    )
  ) {
    return SECONDARY_POSITION_BASIS_POINTS;
  }

  return OUT_OF_POSITION_BASIS_POINTS;
}

/**
 * Whether a player is not at home in a slot: outside its family, and neither he nor one of his other positions is one the role asks
 * for (`INS-10`). The server's out-of-position warning and the main board's marker mean the same.
 */
export function isOutOfPositionIn(player: ShapeFitPlayer, slot: ShapeFitSlot): boolean {
  return familiarityOf(player, slot) === OUT_OF_POSITION_BASIS_POINTS;
}

/** What it costs to put a player in a slot: his unfamiliarity with it, weighted, plus how far it is from where he stands. */
export function costOf(player: ShapeFitPlayer, slot: ShapeFitSlot): number {
  const unfamiliarity = CERTAIN - familiarityOf(player, slot);
  const along = Math.abs(player.standing.x - slot.x);
  const across = Math.trunc(
    (Math.abs(player.standing.y - slot.y) * LATERAL_DISTANCE_PERCENT) / 100,
  );

  return unfamiliarity * FAMILIARITY_WEIGHT + along + across;
}

/**
 * Fits the players of a lineup into a shape.
 *
 * @param onPitch The players, with the slots they fill now.
 * @param newSlots The new shape's slots, numbered from 1; the slot with the goalkeeper's family is his.
 * @param overrides The manager's own placements, new slot number by player id, or null. They are placed first.
 * @returns The new slot number each placed player takes. When there are fewer players than slots, the slots nobody takes are missing
 * from the values: the ones cheapest to lose, the most advanced first.
 */
export function fitShape(
  onPitch: readonly ShapeFitPlayer[],
  newSlots: readonly ShapeFitSlot[],
  overrides: ReadonlyMap<string, number> | null,
): Map<string, number> {
  const slots = [...newSlots].sort((left, right) => left.slot - right.slot);

  if (new Set(slots.map((slot) => slot.slot)).size !== slots.length) {
    throw new Error("The new shape's slot numbers must be distinct.");
  }

  const assigned = new Map<string, number>();
  const taken = new Set<number>();

  // The goalkeeper stays in goal: the first slot of his family is his. If he has gone, nobody is put there.
  const keeperSlot = slots.find((slot) => slot.family === GOALKEEPER_FAMILY) ?? null;
  const keeper = onPitch.find((player) => player.standing.family === GOALKEEPER_FAMILY) ?? null;

  if (keeperSlot !== null) {
    taken.add(keeperSlot.slot);

    if (keeper !== null) {
      assigned.set(keeper.id, keeperSlot.slot);
    }
  }

  const players = onPitch
    .filter((player) => player.id !== keeper?.id)
    .sort((left, right) => compareIds(left.id, right.id));

  placeOverrides(players, slots, overrides, keeperSlot, assigned, taken);

  const free = players.filter((player) => !assigned.has(player.id));
  let open = slots.filter((slot) => !taken.has(slot.slot));

  if (free.length < open.length) {
    open = withoutCheapestToLose(free, open, open.length - free.length);
  }

  const pairs: { cost: number; slot: number; id: string }[] = [];

  for (const player of free) {
    for (const slot of open) {
      pairs.push({ cost: costOf(player, slot), slot: slot.slot, id: player.id });
    }
  }

  pairs.sort(
    (left, right) =>
      left.cost - right.cost || left.slot - right.slot || compareIds(left.id, right.id),
  );

  for (const { slot, id } of pairs) {
    if (assigned.has(id) || taken.has(slot)) {
      continue;
    }

    assigned.set(id, slot);
    taken.add(slot);
  }

  improve(free, open, assigned);

  return assigned;
}

/**
 * Swaps two players' slots whenever that lowers what the two cost together, until no swap does. The greedy pass takes the cheapest
 * pairs first, so the last slot goes to whoever is left; a pairwise swap can put a winger where a striker was left. Players are visited
 * in slot order and the first strictly improving swap is taken, so the answer does not depend on the order the caller lists anyone in,
 * and every swap lowers the total, so it ends.
 */
function improve(
  free: readonly ShapeFitPlayer[],
  open: readonly ShapeFitSlot[],
  assigned: Map<string, number>,
): void {
  const slotOf = new Map(open.map((slot) => [slot.slot, slot]));
  const placed = free.filter((player) => assigned.has(player.id));
  let improved = true;

  while (improved) {
    improved = false;

    const order = [...placed].sort(
      (left, right) => (assigned.get(left.id) ?? 0) - (assigned.get(right.id) ?? 0),
    );

    for (let left = 0; left < order.length && !improved; left++) {
      for (let right = left + 1; right < order.length && !improved; right++) {
        const one = order[left];
        const other = order[right];
        const oneSlot = slotOf.get(assigned.get(one.id) ?? 0);
        const otherSlot = slotOf.get(assigned.get(other.id) ?? 0);

        if (oneSlot === undefined || otherSlot === undefined) {
          continue;
        }

        const before = costOf(one, oneSlot) + costOf(other, otherSlot);
        const after = costOf(one, otherSlot) + costOf(other, oneSlot);

        if (after < before) {
          assigned.set(one.id, otherSlot.slot);
          assigned.set(other.id, oneSlot.slot);
          improved = true;
        }
      }
    }
  }
}

/**
 * Places the manager's own choices first, in slot order then identity order. A choice is ignored when its player has left the pitch, is
 * the goalkeeper, names the goalkeeper's slot or a slot that does not exist, or when the slot is already taken.
 */
function placeOverrides(
  players: readonly ShapeFitPlayer[],
  slots: readonly ShapeFitSlot[],
  overrides: ReadonlyMap<string, number> | null,
  keeperSlot: ShapeFitSlot | null,
  assigned: Map<string, number>,
  taken: Set<number>,
): void {
  if (overrides === null) {
    return;
  }

  const onPitch = new Set(players.map((player) => player.id));
  const ordered = [...overrides].sort(
    ([leftId, leftSlot], [rightId, rightSlot]) =>
      leftSlot - rightSlot || compareIds(leftId, rightId),
  );

  for (const [id, slotNumber] of ordered) {
    if (
      !onPitch.has(id) ||
      assigned.has(id) ||
      taken.has(slotNumber) ||
      slotNumber === keeperSlot?.slot ||
      !slots.some((slot) => slot.slot === slotNumber)
    ) {
      continue;
    }

    assigned.set(id, slotNumber);
    taken.add(slotNumber);
  }
}

/**
 * Leaves some slots empty when the side is a player short: the ones that are cheapest to lose. A slot's loss costs least when even its
 * best candidate is a poor fit for it, so the slots to drop are those whose cheapest candidate costs most, the most advanced first when
 * that ties (a side down to ten drops a forward), then the lower slot number.
 */
function withoutCheapestToLose(
  free: readonly ShapeFitPlayer[],
  open: readonly ShapeFitSlot[],
  drop: number,
): ShapeFitSlot[] {
  const dropped = new Set(
    open
      .map((slot) => ({
        slot,
        best: free.length === 0 ? 0 : Math.min(...free.map((player) => costOf(player, slot))),
      }))
      .sort(
        (left, right) =>
          right.best - left.best || right.slot.x - left.slot.x || left.slot.slot - right.slot.slot,
      )
      .slice(0, drop)
      .map((item) => item.slot.slot),
  );

  return open.filter((slot) => !dropped.has(slot.slot));
}
