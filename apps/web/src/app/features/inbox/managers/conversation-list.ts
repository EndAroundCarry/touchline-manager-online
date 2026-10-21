import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConversationsStore } from '../../../core/messages/conversations-store';
import { formatInstant } from '../../../core/world/presentation';
import {
  FORM_ERROR,
  NAME_LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
} from '../../../shared/forms/control-styles';
import { InboxTabs } from '../inbox-tabs';

/**
 * The Managers section of the inbox: every conversation the manager is in, most recently active first
 * (`COM-10`).
 *
 * Each line is a link to its thread. Unread and blocked are said in words, not only by colour. The preview is
 * rendered as text through interpolation; message bodies are never markup (`COM-13`).
 */
@Component({
  selector: 'app-conversation-list',
  imports: [RouterLink, InboxTabs],
  templateUrl: './conversation-list.html',
})
export class ConversationList {
  private readonly store = inject(ConversationsStore);

  protected readonly conversations = this.store.conversations;
  protected readonly hasMore = this.store.hasMoreConversations;
  protected readonly loading = this.store.listLoading;
  protected readonly loadingMore = this.store.listLoadingMore;
  protected readonly error = this.store.listError;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly nameLinkClass = NAME_LINK;

  constructor() {
    this.store.loadList();
  }

  /** Reads the next page of older conversations. */
  protected loadMore(): void {
    this.store.loadMoreConversations();
  }

  /** Renders an instant in the viewer's local time. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
