/**
 * Client-side presentation helpers for the inbox (`F-41`, master plan §11.1).
 *
 * Small and pure, so they can be unit tested without a component. The server owns the wording of a message
 * and the stable category; these helpers only turn a category into a shelf name and decide where a message
 * links, falling back to the code rather than showing nothing when a new one appears.
 */

const CATEGORY_LABELS: Record<string, string> = {
  result: 'Results',
  table: 'Table',
  discipline: 'Discipline',
  injury: 'Injuries',
  squad: 'Your squad',
};

/** Names the shelf a message sits on, falling back to the code. */
export function categoryLabel(code: string): string {
  return CATEGORY_LABELS[code] ?? code;
}

/**
 * The route a message points at, or null when it points nowhere.
 *
 * A result links to its match, a card or injury to the player, and a repaired side to the fixture it was
 * frozen for — the same destinations the rest of the shell uses, so a message is a way into the game rather
 * than a dead end. A table move names no entity and links nowhere.
 */
export function messageLink(category: string, relatedEntityId: string | null): string | null {
  if (relatedEntityId === null) {
    return null;
  }

  switch (category) {
    case 'result':
      return `/matches/${relatedEntityId}`;

    case 'discipline':
    case 'injury':
      return `/players/${relatedEntityId}`;

    case 'squad':
      return `/fixtures/${relatedEntityId}/prepare`;

    default:
      return null;
  }
}
