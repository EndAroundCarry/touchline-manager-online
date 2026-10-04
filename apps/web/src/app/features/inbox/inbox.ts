import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  categoryLabel,
  linkLabel,
  messageLink,
  revealKeyFor,
} from '../../core/inbox/inbox-presentation';
import { InboxMessage } from '../../core/inbox/inbox.models';
import { InboxStore } from '../../core/inbox/inbox-store';
import { ResultRevealStore } from '../../core/match/result-reveal-store';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

/**
 * The inbox screen (master plan §11.1, F-41).
 *
 * What the game told the manager: the results their club played, its table moves, its players' cards and
 * injuries, and the sides the game had to repair. The shelf name is text rather than a colour, an unread
 * message says so in words, and each line links to the match, player, or fixture it is about where there is
 * one — so the list is reachable by a keyboard and a screen reader, not only by eye (§11.3).
 *
 * A message about a match never says how it went. The score, the outcome and the table move are held back as
 * the message's spoiler and shown only when the manager chooses "Show result", or has already watched the
 * match, so the match is something to watch before it is something to read.
 *
 * The page walk is a cursor, so "load older" asks for what comes after the last message read rather than for
 * a numbered page that a message arriving in between would shift.
 */
@Component({
  selector: 'app-inbox',
  imports: [RouterLink],
  templateUrl: './inbox.html',
})
export class Inbox {
  private readonly store = inject(InboxStore);
  private readonly reveals = inject(ResultRevealStore);

  protected readonly messages = this.store.messages;
  protected readonly unreadCount = this.store.unreadCount;
  protected readonly hasMore = this.store.hasMore;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly error = this.store.error;
  protected readonly actionError = this.store.actionError;
  protected readonly markingId = this.store.markingId;
  protected readonly markingAll = this.store.markingAll;
  protected readonly unreadOnly = this.store.unreadOnly;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    this.store.load();
  }

  /** Switches the list between every message and the unread ones. */
  protected toggleUnreadOnly(): void {
    this.store.setUnreadOnly(!this.unreadOnly());
  }

  /** Marks one message read. */
  protected markRead(messageId: string): void {
    this.store.markRead(messageId);
  }

  /** Marks every unread message read. */
  protected markAllRead(): void {
    this.store.markAllRead();
  }

  /** Reads the next page of older messages. */
  protected loadMore(): void {
    this.store.loadMore();
  }

  /** Names the shelf a message sits on. */
  protected category(code: string): string {
    return categoryLabel(code);
  }

  /** The route a message points at, or null when it points nowhere. */
  protected link(category: string, relatedEntityId: string | null): string | null {
    return messageLink(category, relatedEntityId);
  }

  /** The words of the link into a message's entity. */
  protected linkText(category: string): string {
    return linkLabel(category);
  }

  /**
   * Whether a message's hidden result is showing: the manager asked for it here, or has seen the match, in
   * the viewer or from another message, which remembers it under the same key.
   */
  protected isRevealed(message: InboxMessage): boolean {
    return this.reveals.isRevealed(revealKeyFor(message));
  }

  /** Shows a message's hidden result. */
  protected reveal(message: InboxMessage): void {
    this.reveals.reveal(revealKeyFor(message));
  }

  /** Renders a message's instant in the viewer's local time (`CAL-4`). */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
