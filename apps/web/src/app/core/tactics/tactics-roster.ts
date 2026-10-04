/**
 * The tactics screen's player table, as pure data.
 *
 * The tactics read names who may be picked; the squad read says how old they are, how fit, and what they
 * can do. Joining the two here keeps the component free of lookups, and lets the table be unit tested
 * without one. The ratings are position-weighted (`position-ratings.ts`), and the position they are weighted
 * for is a parameter: a player's own by default, or the position of the slot the manager is filling, so
 * "who is best at centre back" is a sort away.
 */

import { PositionAverages, positionAverages } from '../squad/position-ratings';
import type { PlayerAttributes, SquadPlayer } from '../squad/squad.models';
import { POSITION_FAMILY_ORDER } from './tactics-presentation';
import type { SelectablePlayer } from './tactics.models';

/** One row of the player table. */
export interface RosterRow {
  readonly id: string;
  readonly fullName: string;
  readonly shortName: string;

  /** The player's own position code. */
  readonly position: string;
  readonly positionFamily: string;
  readonly isUnavailable: boolean;

  /** The slot the player fills in the plan being edited, which is the shirt they would wear, or null. */
  readonly slotNumber: number | null;

  /** From the squad read: null until it arrives, or when it has no record of the player. */
  readonly age: number | null;
  readonly condition: number | null;
  readonly fatigue: number | null;
  readonly attributes: PlayerAttributes | null;

  /** The position the ratings are weighted for. */
  readonly ratedAs: string;
  readonly ratings: PositionAverages | null;
}

/** A slot's occupant, as far as the table needs to know. */
export interface SlotOccupant {
  readonly slotNumber: number;
  readonly playerId: string | null;
}

/**
 * Builds the table: every selectable player, goalkeepers first and each family by name.
 *
 * @param ratedAs The position to weight the ratings for, or null to weight each player for their own.
 */
export function buildRoster(
  players: readonly SelectablePlayer[],
  squad: readonly SquadPlayer[],
  slots: readonly SlotOccupant[],
  ratedAs: string | null,
): readonly RosterRow[] {
  const details = new Map(squad.map((player) => [player.id, player]));
  const slotByPlayer = new Map(
    slots.flatMap((slot) => (slot.playerId === null ? [] : [[slot.playerId, slot.slotNumber]])),
  );

  return [...players]
    .sort(
      (left, right) =>
        familyRank(left.positionFamily) - familyRank(right.positionFamily) ||
        left.fullName.localeCompare(right.fullName),
    )
    .map((player) => {
      const detail = details.get(player.id) ?? null;
      const basis = ratedAs ?? player.primaryPosition;

      return {
        id: player.id,
        fullName: player.fullName,
        shortName: player.shortName,
        position: player.primaryPosition,
        positionFamily: player.positionFamily,
        isUnavailable: player.isUnavailable,
        slotNumber: slotByPlayer.get(player.id) ?? null,
        age: detail?.age ?? null,
        condition: detail?.state.condition ?? null,
        fatigue: detail?.state.fatigue ?? null,
        attributes: detail?.attributes ?? null,
        ratedAs: basis,
        ratings: detail === null ? null : positionAverages(detail.attributes, basis),
      };
    });
}

function familyRank(family: string): number {
  const rank = POSITION_FAMILY_ORDER.indexOf(family);

  return rank === -1 ? POSITION_FAMILY_ORDER.length : rank;
}
