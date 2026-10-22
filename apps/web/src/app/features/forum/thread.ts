import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ForumApi } from '../../core/forum/forum-api';
import { BODY_MAX_CHARS, ForumThreadDetail } from '../../core/forum/forum.models';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * One forum thread, oldest post first, with a reply box under it.
 *
 * A post body is shown through interpolation inside a `whitespace-pre-line` element, so a line break is kept and
 * markup is never interpreted. A post by the game's staff says so beside the name.
 */
@Component({
  selector: 'app-forum-thread',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './thread.html',
})
export class ForumThread {
  /** The thread, bound from the `:threadId` route parameter. */
  readonly threadId = input.required<string>();

  private readonly api = inject(ForumApi);

  protected readonly thread = signal<ForumThreadDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);

  protected readonly bodyMax = BODY_MAX_CHARS;
  protected readonly reply = new FormControl('', { nonNullable: true });
  private readonly replyValue = toSignal(this.reply.valueChanges, { initialValue: '' });

  protected readonly replyLength = computed(() => this.replyValue().length);
  protected readonly canSend = computed(() => {
    const length = this.replyValue().trim().length;

    return length > 0 && length <= BODY_MAX_CHARS;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    effect(() => {
      const id = this.threadId();

      untracked(() => {
        this.reply.setValue('');
        this.sendError.set(null);
        this.load(id);
      });
    });
  }

  /** Posts the reply and shows it under the thread once the server has taken it. */
  protected send(event: Event): void {
    event.preventDefault();

    const current = this.thread();

    if (current === null || !this.canSend() || this.sending()) {
      return;
    }

    this.sending.set(true);
    this.sendError.set(null);

    this.api.reply(current.id, this.reply.value.trim()).subscribe({
      next: (post) => {
        this.sending.set(false);
        this.reply.setValue('');
        this.thread.update((thread) =>
          thread === null ? thread : { ...thread, posts: [...thread.posts, post] },
        );
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.sendError.set(
          error instanceof ApiError ? error.detail : 'The reply could not be posted.',
        );
      },
    });
  }

  /** Formats an instant for a reader. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }

  private load(id: string): void {
    this.thread.set(null);
    this.error.set(null);
    this.loading.set(true);

    this.api.thread(id).subscribe({
      next: (thread) => {
        if (id === this.threadId()) {
          this.thread.set(thread);
          this.loading.set(false);
        }
      },
      error: (error: unknown) => {
        if (id === this.threadId()) {
          this.error.set(
            error instanceof ApiError ? error.detail : 'That thread could not be loaded.',
          );
          this.loading.set(false);
        }
      },
    });
  }
}
