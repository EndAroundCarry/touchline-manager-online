import { Injectable, inject, signal } from '@angular/core';
import { SkillGroup, skillGroups } from '../../../core/squad/position-ratings';
import { positionLabel } from '../../../core/squad/squad-presentation';
import { PlayerSearchResult } from '../../../core/scouting/scouting.models';
import { ScoutingApi } from '../../../core/scouting/scouting-api';
import { CardAnchor, placeCard } from './player-card-placement';

/** How long the pointer rests on a name before its card is asked for, so sweeping across a table asks for nothing. */
const HOVER_DELAY_MS = 150;

/** How long a read stays good: skills move with training, but not within a minute of reading them. */
const CACHE_MS = 60_000;

/** The card the popup is showing: the player's skills for their own position, and where to draw it. */
export interface PlayerCardView {
  readonly playerId: string;
  readonly name: string;
  readonly subtitle: string;
  readonly basisLabel: string;
  readonly groups: readonly SkillGroup[];
  readonly left: number;
  readonly top: number;
}

/**
 * The hover card every player's name opens, wherever it is written: the squad, the market, the match.
 *
 * A name only says which player it is (`appPlayerCard`) and where it is on screen; this reads the player's
 * exact public attributes from scouting, keeps them for a minute, and tells the one popup in the shell what to
 * draw. Only the latest hover counts, so a slow answer for a name the pointer has left is dropped.
 */
@Injectable({ providedIn: 'root' })
export class PlayerCardService {
  private readonly api = inject(ScoutingApi);
  private readonly cache = new Map<
    string,
    { readonly at: number; readonly player: PlayerSearchResult }
  >();
  private current: string | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** The card on screen, or null. */
  readonly card = signal<PlayerCardView | null>(null);

  /** Raises the card for a player, beside an anchor on the page. */
  show(playerId: string, anchor: CardAnchor): void {
    this.clearTimer();
    this.current = playerId;

    const cached = this.cache.get(playerId);

    if (cached !== undefined && Date.now() - cached.at < CACHE_MS) {
      this.open(cached.player, anchor);

      return;
    }

    this.timer = setTimeout(() => {
      this.timer = null;
      this.api.player(playerId).subscribe({
        next: (player) => {
          this.cache.set(playerId, { at: Date.now(), player });

          if (this.current === playerId) {
            this.open(player, anchor);
          }
        },
        // A card that cannot be read is simply not shown; the name still works as it did.
        error: () => undefined,
      });
    }, HOVER_DELAY_MS);
  }

  /** Puts the card away, and forgets any read still on its way. */
  hide(): void {
    this.clearTimer();
    this.current = null;
    this.card.set(null);
  }

  private open(player: PlayerSearchResult, anchor: CardAnchor): void {
    const placement = placeCard(anchor, window.innerWidth, window.innerHeight);
    const position = positionLabel(player.primaryPosition);

    this.card.set({
      playerId: player.playerId,
      name: player.fullName,
      subtitle:
        player.clubName === null
          ? `${position} · age ${player.age}`
          : `${position} · age ${player.age} · ${player.clubName}`,
      basisLabel: position,
      groups: skillGroups(player.attributes, player.primaryPosition),
      left: placement.left,
      top: placement.top,
    });
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
