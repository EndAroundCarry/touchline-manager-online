/**
 * The preview of a match instruction's new shape: where the plan's starting eleven would stand if the instruction fired at kick-off.
 *
 * The server fits whoever is on the pitch when the instruction really fires; this fits the plan's lineup, which is what the manager can
 * edit in advance. It is the same algorithm (`shape-fit.ts`), so a placement the manager makes here is the one the engine starts from.
 *
 * The rule's substitutions are made before the shape is fitted, as in the engine, so a substitute it brings on by name stands in the
 * seat of the man he replaces and can be placed like a starter. A substitution that picks the man to take off by a selector cannot be
 * previewed: nobody knows who leaves until the match.
 */

import type { PlanDraft } from './tactics-draft';
import { namedSwaps } from './match-instructions';
import { ShapeFitPlayer, ShapeFitSlot, fitShape, isOutOfPositionIn } from './shape-fit';
import type {
  FormationPreset,
  MatchInstruction,
  MatchInstructionOverride,
  SelectablePlayer,
} from './tactics.models';

/** One player's place in a rule's new shape. */
export interface ShapePlacement {
  readonly playerId: string;
  readonly slotNumber: number;

  /** Whether the manager chose this place, rather than the best fit. */
  readonly isChosen: boolean;

  /** Whether he is not at home in the slot (`INS-10`). */
  readonly isOutOfPosition: boolean;
}

/** The slots of a formation preset, as the fit takes them. */
export function shapeSlotsOf(formation: FormationPreset): readonly ShapeFitSlot[] {
  return formation.slots.map((slot) => ({
    slot: slot.slotNumber,
    family: slot.positionFamily,
    role: slot.role,
    x: slot.normalizedX,
    y: slot.normalizedY,
  }));
}

/** The shape to fit and the players to fit into it, once the rule's named substitutions are made. */
interface FitInput {
  readonly slots: readonly ShapeFitSlot[];
  readonly onPitch: readonly ShapeFitPlayer[];
}

function fitInput(
  draft: PlanDraft,
  rule: MatchInstruction,
  formations: readonly FormationPreset[],
  players: readonly SelectablePlayer[],
  secondary: ReadonlyMap<string, readonly string[]>,
): FitInput | null {
  const formation = formations.find((candidate) => candidate.code === rule.formation);

  if (formation === undefined) {
    return null;
  }

  const byId = new Map(players.map((player) => [player.id, player]));
  const lineup = new Map<string, ShapeFitPlayer>();

  for (const slot of draft.slots) {
    const player = slot.playerId === null ? undefined : byId.get(slot.playerId);

    if (player !== undefined) {
      lineup.set(player.id, {
        id: player.id,
        position: player.primaryPosition,
        secondary: secondary.get(player.id) ?? [],
        standing: {
          family: slot.positionFamily,
          role: slot.role,
          x: slot.normalizedX,
          y: slot.normalizedY,
        },
      });
    }
  }

  const lineupIds = new Set(lineup.keys());
  const benchIds = new Set(
    draft.bench.flatMap((place) => (place.playerId === null ? [] : [place.playerId])),
  );
  const onPitch = new Map(lineup);

  for (const swap of namedSwaps(rule, lineupIds, benchIds)) {
    const leaving = onPitch.get(swap.offPlayerId);
    const coming = byId.get(swap.onPlayerId);

    if (leaving !== undefined && coming !== undefined) {
      onPitch.delete(swap.offPlayerId);
      onPitch.set(coming.id, {
        id: coming.id,
        position: coming.primaryPosition,
        secondary: secondary.get(coming.id) ?? [],
        standing: leaving.standing,
      });
    }
  }

  return { slots: shapeSlotsOf(formation), onPitch: [...onPitch.values()] };
}

function chosenPlacements(rule: MatchInstruction): ReadonlyMap<string, number> {
  return new Map(rule.overrides.map((placement) => [placement.playerId, placement.slotNumber]));
}

function placementsOf(
  input: FitInput,
  fitted: ReadonlyMap<string, number>,
  chosen: ReadonlyMap<string, number>,
): readonly ShapePlacement[] {
  const slots = new Map(input.slots.map((slot) => [slot.slot, slot]));
  const players = new Map(input.onPitch.map((player) => [player.id, player]));

  return [...fitted].map(([playerId, slotNumber]) => {
    const player = players.get(playerId);
    const slot = slots.get(slotNumber);

    return {
      playerId,
      slotNumber,
      isChosen: chosen.get(playerId) === slotNumber,
      isOutOfPosition:
        player !== undefined && slot !== undefined && isOutOfPositionIn(player, slot),
    };
  });
}

/**
 * Where each player the rule fields stands in its new shape, or null when the rule keeps the shape (or names a formation the club
 * does not know).
 *
 * @param secondary Each player's other positions, from the squad read; a player missing from it is fitted by his primary position alone.
 */
export function previewShape(
  draft: PlanDraft,
  rule: MatchInstruction,
  formations: readonly FormationPreset[],
  players: readonly SelectablePlayer[],
  secondary: ReadonlyMap<string, readonly string[]>,
): readonly ShapePlacement[] | null {
  const input = fitInput(draft, rule, formations, players, secondary);

  if (input === null) {
    return null;
  }

  const chosen = chosenPlacements(rule);

  return placementsOf(input, fitShape(input.onPitch, input.slots, chosen), chosen);
}

/**
 * The placements that put as many players as possible in a slot they are at home in, keeping the ones the manager chose.
 *
 * It starts from the usual fit, which also weighs how far a player walks, and then swaps any two players of the fit whenever that leaves
 * fewer of them out of position. The whole result is returned as placements, so the manager sees every one and can change it. Null
 * when the rule keeps the shape.
 */
export function naturalPlacements(
  draft: PlanDraft,
  rule: MatchInstruction,
  formations: readonly FormationPreset[],
  players: readonly SelectablePlayer[],
  secondary: ReadonlyMap<string, readonly string[]>,
): readonly MatchInstructionOverride[] | null {
  const input = fitInput(draft, rule, formations, players, secondary);

  if (input === null) {
    return null;
  }

  const chosen = chosenPlacements(rule);
  const fitted = fitShape(input.onPitch, input.slots, chosen);
  const slots = new Map(input.slots.map((slot) => [slot.slot, slot]));
  const keeperSlot = input.slots.find((slot) => slot.family === 'goalkeeper')?.slot ?? 0;
  const movable = input.onPitch.filter(
    (player) => !chosen.has(player.id) && fitted.get(player.id) !== keeperSlot,
  );

  const outOfPosition = (player: ShapeFitPlayer, slotNumber: number): number => {
    const slot = slots.get(slotNumber);

    return slot !== undefined && isOutOfPositionIn(player, slot) ? 1 : 0;
  };
  let improved = true;

  while (improved) {
    improved = false;

    const order = movable
      .filter((player) => fitted.has(player.id))
      .sort((left, right) => (fitted.get(left.id) ?? 0) - (fitted.get(right.id) ?? 0));

    for (let left = 0; left < order.length && !improved; left++) {
      for (let right = left + 1; right < order.length && !improved; right++) {
        const one = order[left];
        const other = order[right];
        const oneSlot = fitted.get(one.id) ?? 0;
        const otherSlot = fitted.get(other.id) ?? 0;
        const before = outOfPosition(one, oneSlot) + outOfPosition(other, otherSlot);
        const after = outOfPosition(one, otherSlot) + outOfPosition(other, oneSlot);

        if (after < before) {
          fitted.set(one.id, otherSlot);
          fitted.set(other.id, oneSlot);
          improved = true;
        }
      }
    }
  }

  return [...fitted]
    .filter(([, slotNumber]) => slotNumber !== keeperSlot)
    .map(([playerId, slotNumber]) => ({ playerId, slotNumber }))
    .sort((left, right) => left.slotNumber - right.slotNumber);
}
