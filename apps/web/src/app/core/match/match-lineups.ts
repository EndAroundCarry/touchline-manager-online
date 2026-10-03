import { MatchLineupPlayer, MatchPresentation } from './match.models';

/**
 * Every lineup player by participant, and every name and side by the identity commentary names them by
 * (`replay-v4`).
 *
 * Commentary carries a `playerId` fact, while the pitch, the lineups and the live metrics speak in
 * *participants*. The engine's contract is that commentary names a player by their participant — and the
 * application happens to make a participant's identity the underlying player's — so a commentary identity may
 * be either, and the lookups answer to both. They are built once per presentation rather than searched per
 * frame. The film timeline reads them to put a feed row on the right side and a card on the right token, and
 * the match center reads the same ones for its panels, so the two can never disagree about who somebody is.
 */
export interface LineupIndex {
  readonly byParticipant: ReadonlyMap<string, MatchLineupPlayer>;
  readonly nameByPlayerId: ReadonlyMap<string, string>;
  readonly participantByPlayerId: ReadonlyMap<string, string>;
  readonly sideByPlayerId: ReadonlyMap<string, string>;
  readonly sideByParticipant: ReadonlyMap<string, string>;
}

/** Indexes both lineups of a presentation. A missing lineup contributes nothing. */
export function lineupIndexOf(presentation: MatchPresentation | null): LineupIndex {
  const byParticipant = new Map<string, MatchLineupPlayer>();
  const nameByPlayerId = new Map<string, string>();
  const participantByPlayerId = new Map<string, string>();
  const sideByPlayerId = new Map<string, string>();
  const sideByParticipant = new Map<string, string>();

  for (const [side, lineup] of [
    ['home', presentation?.homeLineup],
    ['away', presentation?.awayLineup],
  ] as const) {
    if (lineup === null || lineup === undefined) {
      continue;
    }

    for (const player of [...lineup.starters, ...lineup.bench]) {
      byParticipant.set(player.participantId, player);
      sideByParticipant.set(player.participantId, side);

      // A commentary identity is the participant by the engine's contract and the player in practice, so
      // each is a key for the same facts.
      for (const identity of [player.playerId, player.participantId]) {
        nameByPlayerId.set(identity, player.name);
        participantByPlayerId.set(identity, player.participantId);
        sideByPlayerId.set(identity, side);
      }
    }
  }

  return {
    byParticipant,
    nameByPlayerId,
    participantByPlayerId,
    sideByPlayerId,
    sideByParticipant,
  };
}
