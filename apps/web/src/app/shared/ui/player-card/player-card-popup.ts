import { Component, inject } from '@angular/core';
import { PLAYER_CARD_ELEMENT_ID } from './player-card-element-id';
import { PlayerCardService } from './player-card.service';
import { PlayerSkillsCard } from './player-skills-card';

/**
 * The one hover card, placed once in the shell. It takes no pointer events, so it never steals the hover from
 * the name that raised it.
 */
@Component({
  selector: 'app-player-card-popup',
  imports: [PlayerSkillsCard],
  template: `
    @if (cards.card(); as card) {
      <div
        [id]="elementId"
        role="tooltip"
        class="pointer-events-none fixed z-50 w-[34rem] max-w-[calc(100vw-1rem)] rounded-lg border border-line-strong bg-panel p-3 text-xs shadow-xl"
        [style.left.px]="card.left"
        [style.top.px]="card.top"
      >
        <app-player-skills-card
          [name]="card.name"
          [subtitle]="card.subtitle"
          [basisLabel]="card.basisLabel"
          [groups]="card.groups"
        />
      </div>
    }
  `,
})
export class PlayerCardPopup {
  protected readonly cards = inject(PlayerCardService);
  protected readonly elementId = PLAYER_CARD_ELEMENT_ID;
}
