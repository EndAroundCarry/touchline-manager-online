/**
 * The preview of a match instruction's new shape: where the plan's starting eleven would stand if the instruction fired at kick-off.
 *
 * The server fits whoever is on the pitch when the instruction really fires; this fits the plan's lineup, which is what the manager can
 * edit in advance. It is the same algorithm (`shape-fit.ts`), so a placement the manager makes here is the one the engine starts from.
 */

import type { PlanDraft } from './tactics-draft';
import { ShapeFitPlayer, ShapeFitSlot, fitShape } from './shape-fit';
import type { FormationPreset, MatchInstruction, SelectablePlayer } from './tactics.models';

/** One player's place in a rule's new shape. */
export interface ShapePlacement {
  readonly playerId: string;
  readonly slotNumber: number;

  /** Whether the manager chose this place, rather than the best fit. */
  readonly isChosen: boolean;
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

/**
 * Where each starter of the plan stands in the rule's new shape, or null when the rule keeps the shape (or names a formation the club
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
  const formation = formations.find((candidate) => candidate.code === rule.formation);

  if (formation === undefined) {
    return null;
  }

  const byId = new Map(players.map((player) => [player.id, player]));
  const onPitch: ShapeFitPlayer[] = [];

  for (const slot of draft.slots) {
    const player = slot.playerId === null ? undefined : byId.get(slot.playerId);

    if (player !== undefined) {
      onPitch.push({
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

  const chosen = new Map(
    rule.overrides.map((placement) => [placement.playerId, placement.slotNumber]),
  );
  const fitted = fitShape(onPitch, shapeSlotsOf(formation), chosen);

  return [...fitted].map(([playerId, slotNumber]) => ({
    playerId,
    slotNumber,
    isChosen: chosen.get(playerId) === slotNumber,
  }));
}
