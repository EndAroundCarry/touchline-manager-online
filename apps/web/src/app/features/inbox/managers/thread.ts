import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConversationsStore } from '../../../core/messages/conversations-store';
import { BODY_MAX_CHARS } from '../../../core/messages/messages.models';
import { formatInstant } from '../../../core/world/presentation';
import {
  DESTRUCTIVE_BUTTON,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';

/**
 * One conversation, oldest message first (`COM-10`, `COM-12`, `COM-15`, `COM-16`).
 *
 * Opening it marks what was addressed to the reader read. A body is shown through interpolation inside a
 * `whitespace-pre-line` element, so a line break is kept and markup is never interpreted (`COM-13`). Blocking
 * is a two-step action, a button and then a confirmation in words, because it ends the conversation for both
 * sides until it is lifted; lifting it is one press.
 */
@Component({
  selector: 'app-conversation-thread',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './thread.html',
})
export class ConversationThread {
  /** The conversation, bound from the `:conversationId` route parameter. */
  readonly conversationId = input.required<string>();

  private readonly store = inject(ConversationsStore);

  protected readonly thread = this.store.thread;
  protected readonly messages = this.store.threadMessages;
  protected readonly hasEarlier = this.store.hasEarlier;
  protected readonly loading = this.store.threadLoading;
  protected readonly loadingMore = this.store.threadLoadingMore;
  protected readonly error = this.store.threadError;
  protected readonly sending = this.store.sending;
  protected readonly sendError = this.store.sendError;
  protected readonly blocking = this.store.blocking;
  protected readonly blockError = this.store.blockError;

  protected readonly bodyMax = BODY_MAX_CHARS;
  protected readonly reply = new FormControl('', { nonNullable: true });
  private readonly replyValue = toSignal(this.reply.valueChanges, { initialValue: '' });

  protected readonly replyLength = computed(() => this.replyValue().length);
  protected readonly canSend = computed(() => {
    const length = this.replyValue().trim().length;

    return length > 0 && length <= BODY_MAX_CHARS;
  });

  /** Whether the block confirmation is showing. */
  protected readonly confirmingBlock = signal(false);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly destructiveButtonClass = DESTRUCTIVE_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    effect(() => {
      const id = this.conversationId();

      untracked(() => {
        this.confirmingBlock.set(false);
        this.reply.setValue('');
        this.store.openThread(id);
      });
    });
  }

  /** Sends the reply and clears the box once the server has taken it. */
  protected send(event: Event): void {
    event.preventDefault();

    if (!this.canSend() || this.sending()) {
      return;
    }

    this.store.reply(this.reply.value.trim(), () => this.reply.setValue(''));
  }

  /** Reads the next page of earlier messages. */
  protected loadEarlier(): void {
    this.store.loadEarlier();
  }

  /** Asks the reader to confirm a block. */
  protected askBlock(): void {
    this.confirmingBlock.set(true);
  }

  /** Withdraws the block confirmation. */
  protected cancelBlock(): void {
    this.confirmingBlock.set(false);
  }

  /** Blocks the other manager. */
  protected block(): void {
    this.confirmingBlock.set(false);
    this.store.block();
  }

  /** Lifts the block on the other manager. */
  protected unblock(): void {
    this.store.unblock();
  }

  /** Renders an instant in the viewer's local time. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
