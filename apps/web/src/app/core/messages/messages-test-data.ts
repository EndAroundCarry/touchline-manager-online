import {
  Conversation,
  ConversationList,
  ConversationParticipant,
  ConversationSummary,
  DirectMessage,
} from './messages.models';

/** Builders for the direct-message specs, so each spec states only what it is about. */

export function participant(
  overrides: Partial<ConversationParticipant> = {},
): ConversationParticipant {
  return {
    managerId: 'manager-b',
    displayName: 'Sam Rivera',
    clubId: 'club-b',
    clubName: 'Vale Rovers',
    ...overrides,
  };
}

export function directMessage(overrides: Partial<DirectMessage> = {}): DirectMessage {
  return {
    id: 'dm-1',
    fromMe: false,
    body: 'Fancy a friendly before the cup?',
    sentAt: '2026-10-20T10:00:00Z',
    readAt: null,
    ...overrides,
  };
}

export function summary(overrides: Partial<ConversationSummary> = {}): ConversationSummary {
  return {
    id: 'conversation-1',
    subject: 'A friendly',
    otherManager: participant(),
    lastMessageAt: '2026-10-20T10:00:00Z',
    lastMessagePreview: 'Fancy a friendly before the cup?',
    unreadCount: 1,
    blockedByMe: false,
    ...overrides,
  };
}

export function conversationList(overrides: Partial<ConversationList> = {}): ConversationList {
  return {
    conversations: [summary()],
    unreadCount: 1,
    nextCursor: null,
    serverTime: '2026-10-20T12:00:00Z',
    ...overrides,
  };
}

/** A thread page; messages are newest first, as the server sends them. */
export function conversation(overrides: Partial<Conversation> = {}): Conversation {
  return {
    id: 'conversation-1',
    subject: 'A friendly',
    otherManager: participant(),
    messages: [directMessage()],
    nextCursor: null,
    blockedByMe: false,
    canReply: true,
    replyBlockedReason: null,
    serverTime: '2026-10-20T12:00:00Z',
    ...overrides,
  };
}
