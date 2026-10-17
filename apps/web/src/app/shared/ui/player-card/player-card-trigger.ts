import { Directive, ElementRef, OnDestroy, computed, inject, input } from '@angular/core';
import { PLAYER_CARD_ELEMENT_ID } from './player-card-element-id';
import { PlayerCardService } from './player-card.service';

/**
 * Makes a player's name open the skills card on hover or keyboard focus: `<a [appPlayerCard]="player.id">`.
 *
 * A name with no identity (`null`) does nothing, so a template can bind it without guarding. The element's own
 * behaviour is untouched: this only listens.
 */
@Directive({
  selector: '[appPlayerCard]',
  host: {
    '(mouseenter)': 'raise($event)',
    '(mouseleave)': 'put()',
    '(focusin)': 'raise($event)',
    '(focusout)': 'put()',
    '[attr.aria-describedby]': 'describedBy()',
  },
})
export class PlayerCardTrigger implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly cards = inject(PlayerCardService);

  /** The player whose card the name opens. */
  readonly playerId = input<string | null | undefined>(null, { alias: 'appPlayerCard' });

  protected readonly describedBy = computed(() => {
    const id = this.playerId();

    return id && this.cards.card()?.playerId === id ? PLAYER_CARD_ELEMENT_ID : null;
  });

  protected raise(event: MouseEvent | FocusEvent): void {
    const playerId = this.playerId();

    if (!playerId) {
      return;
    }

    const rect = this.host.nativeElement.getBoundingClientRect();

    this.cards.show(playerId, {
      x: event instanceof MouseEvent ? event.clientX + 16 : rect.left + 16,
      top: rect.top,
      bottom: rect.bottom,
    });
  }

  protected put(): void {
    this.cards.hide();
  }

  ngOnDestroy(): void {
    const playerId = this.playerId();

    if (playerId && this.cards.card()?.playerId === playerId) {
      this.cards.hide();
    }
  }
}
