/**
 * Transport shapes for direct messages between managers (`COM-10`…`COM-16`).
 *
 * Hand-written mirror of `Tactical11Manager.Contracts.Comms.DirectMessage*`, like the inbox models and for the
 * same reason: the compiler is the check that the two stay in step. Everything is `readonly`.
 */

/** The other person in a conversation, as the reader sees them now. */
export interface ConversationParticipant {
  readonly managerId: string;
  readonly displayName: string;
  /** The club they hold now, or null when they hold none. */
  readonly clubId: string | null;
  readonly clubName: string | null;
}

/** One line of the conversation list. */
export interface ConversationSummary {
  readonly id: string;
  readonly subject: string;
  readonly otherManager: ConversationParticipant;
  readonly lastMessageAt: string;
  readonly lastMessagePreview: string | null;
  readonly unreadCount: number;
  readonly blockedByMe: boolean;
}

/** One page of the manager's conversations, most recently active first. */
export interface ConversationList {
  readonly conversations: readonly ConversationSummary[];
  /** How many direct messages addressed to the reader are unread, for the tab. */
  readonly unreadCount: number;
  readonly nextCursor: string | null;
  readonly serverTime: string;
}

/** One message in a thread, from the reader's side. */
export interface DirectMessage {
  readonly id: string;
  readonly fromMe: boolean;
  /** Always shown as text, never as markup (`COM-13`). */
  readonly body: string;
  readonly sentAt: string;
  readonly readAt: string | null;
}

/** One page of a thread. The messages come newest first. */
export interface Conversation {
  readonly id: string;
  readonly subject: string;
  readonly otherManager: ConversationParticipant;
  readonly messages: readonly DirectMessage[];
  readonly nextCursor: string | null;
  readonly blockedByMe: boolean;
  readonly canReply: boolean;
  readonly replyBlockedReason: string | null;
  readonly serverTime: string;
}

/** What a start or a reply returns: where the message went. */
export interface SentDirectMessage {
  readonly conversationId: string;
  readonly messageId: string;
  readonly sentAt: string;
}

/** The limits a message is written under (`COM-13`), repeated here for the counters. */
export const SUBJECT_MAX_CHARS = 80;
export const BODY_MAX_CHARS = 1000;
