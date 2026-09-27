import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { categoryLabel, messageLink } from '../../core/inbox/inbox-presentation';
import { InboxStore } from '../../core/inbox/inbox-store';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
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
  protected readonly linkClass = LINK;

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

  /** Renders a message's instant in the viewer's local time (`CAL-4`). */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
