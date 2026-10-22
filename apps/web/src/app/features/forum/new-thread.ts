import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ForumApi } from '../../core/forum/forum-api';
import {
  BODY_MAX_CHARS,
  ForumThreadList,
  THREAD_BODY_MIN_CHARS,
  TITLE_MAX_CHARS,
  TITLE_MIN_CHARS,
} from '../../core/forum/forum.models';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The form that starts a thread.
 *
 * The page reads the subforum again rather than trusting the link, so Announcements, which a manager cannot
 * post in, says so before a form is offered, and the language rule of the international forum is shown above
 * the fields. The counters only show the limits; the server decides in the end.
 */
@Component({
  selector: 'app-new-thread',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './new-thread.html',
})
export class NewThread {
  /** The subforum, bound from the `:subforumId` route parameter. */
  readonly subforumId = input.required<string>();

  private readonly api = inject(ForumApi);
  private readonly router = inject(Router);

  protected readonly subforum = signal<ForumThreadList | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);

  protected readonly titleMax = TITLE_MAX_CHARS;
  protected readonly bodyMax = BODY_MAX_CHARS;

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true }),
    body: new FormControl('', { nonNullable: true }),
  });

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  protected readonly titleLength = computed(() => (this.values().title ?? '').length);
  protected readonly bodyLength = computed(() => (this.values().body ?? '').length);

  /** Whether both fields are within their limits once trimmed. */
  protected readonly canSend = computed(() => {
    const title = (this.values().title ?? '').trim().length;
    const body = (this.values().body ?? '').trim().length;

    return (
      title >= TITLE_MIN_CHARS &&
      title <= TITLE_MAX_CHARS &&
      body >= THREAD_BODY_MIN_CHARS &&
      body <= BODY_MAX_CHARS
    );
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    effect(() => {
      const id = this.subforumId();

      untracked(() => this.load(id));
    });
  }

  /** Starts the thread and opens it. */
  protected send(): void {
    const id = this.subforumId();

    if (!this.canSend() || this.sending()) {
      return;
    }

    const { title, body } = this.form.getRawValue();

    this.sending.set(true);
    this.sendError.set(null);

    this.api.createThread(id, title.trim(), body.trim()).subscribe({
      next: (created) => {
        this.sending.set(false);
        void this.router.navigate(['/forum/threads', created.threadId]);
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.sendError.set(
          error instanceof ApiError ? error.detail : 'The thread could not be started.',
        );
      },
    });
  }

  private load(id: string): void {
    this.subforum.set(null);
    this.loadError.set(null);
    this.loading.set(true);

    this.api.threads(id).subscribe({
      next: (list) => {
        if (id === this.subforumId()) {
          this.subforum.set(list);
          this.loading.set(false);
        }
      },
      error: (error: unknown) => {
        if (id === this.subforumId()) {
          this.loadError.set(
            error instanceof ApiError ? error.detail : 'That subforum could not be loaded.',
          );
          this.loading.set(false);
        }
      },
    });
  }
}
