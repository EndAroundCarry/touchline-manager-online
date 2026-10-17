/** The skills card's size (`w-[34rem]` and its usual height), which its placement keeps inside the window. */
export const CARD_WIDTH = 544;
export const CARD_HEIGHT = 360;
export const CARD_MARGIN = 8;

/** Where a card is anchored: the pointer's x, and the top and bottom of what it was raised from. */
export interface CardAnchor {
  readonly x: number;
  readonly top: number;
  readonly bottom: number;
}

/** Where a card is drawn, in viewport pixels. */
export interface CardPlacement {
  readonly left: number;
  readonly top: number;
}

/**
 * Puts the card beside its anchor and keeps it inside the window: below the anchor when there is room for it,
 * above otherwise, and never past either side.
 */
export function placeCard(
  anchor: CardAnchor,
  windowWidth: number,
  windowHeight: number,
): CardPlacement {
  const fitsBelow = anchor.bottom + CARD_MARGIN + CARD_HEIGHT < windowHeight;

  return {
    left: Math.max(CARD_MARGIN, Math.min(anchor.x, windowWidth - CARD_WIDTH - CARD_MARGIN)),
    top: Math.max(
      CARD_MARGIN,
      fitsBelow ? anchor.bottom + CARD_MARGIN : anchor.top - CARD_HEIGHT - CARD_MARGIN,
    ),
  };
}
