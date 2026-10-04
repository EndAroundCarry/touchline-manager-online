/**
 * Transport shapes for the comms module (master plan §10.7, F-41).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Comms`, like the other module models and for the same
 * reason: the OpenAPI-generated client is a later stage, and until then the compiler is the check that these
 * stay in step. Everything is `readonly`.
 *
 * A message carries the rendered English *and* the stable category, so the screen shows a sentence and the
 * client can still group and link by what the message is about.
 */

/** One message in the manager's inbox (`§6.9`). */
export interface InboxMessage {
  readonly id: string;
  readonly category: string;
  readonly templateKey: string;
  readonly title: string;
  readonly body: string;
  readonly relatedEntityId: string | null;
  readonly isRead: boolean;
  readonly createdAt: string;
  readonly readAt: string | null;
  /**
   * What the title and body hold back because it gives a match away: the score, the outcome, the table move.
   * Null when the message has nothing to hide. The screen shows it only when the manager asks, or has
   * already seen the match.
   */
  readonly spoiler: string | null;
}

/** One page of the manager's inbox, newest first (`§10.7`). */
export interface InboxPage {
  readonly messages: readonly InboxMessage[];
  readonly unreadCount: number;
  readonly nextCursor: string | null;
  readonly serverTime: string;
}

/** The lightweight summary the shell polls for the unread badge (`§10.7`, `§11.2`). */
export interface SyncSummary {
  readonly serverTime: string;
  readonly unreadInboxCount: number;
  readonly readOnly: boolean;
  readonly readOnlyMessage: string | null;
}
