import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ForumApi } from '../../core/forum/forum-api';
import { ForumThreadList } from '../../core/forum/forum.models';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  NAME_LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';

/**
 * One subforum's threads, pinned first and then the most recently active.
 *
 * "Start a thread" is offered only where the server says threads may be started; in Announcements it says in
 * words that only the game's staff post there. The server decides in the end.
 */
@Component({
  selector: 'app-subforum',
  imports: [RouterLink],
  templateUrl: './subforum.html',
})
export class Subforum {
  /** The subforum, bound from the `:subforumId` route parameter. */
  readonly subforumId = input.required<string>();

  private readonly api = inject(ForumApi);

  protected readonly list = signal<ForumThreadList | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly nameLinkClass = NAME_LINK;

  constructor() {
    effect(() => {
      const id = this.subforumId();

      untracked(() => this.load(id));
    });
  }

  /** Formats an instant for a reader. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }

  private load(id: string): void {
    this.list.set(null);
    this.error.set(null);
    this.loading.set(true);

    this.api.threads(id).subscribe({
      next: (list) => {
        if (id === this.subforumId()) {
          this.list.set(list);
          this.loading.set(false);
        }
      },
      error: (error: unknown) => {
        if (id === this.subforumId()) {
          this.error.set(
            error instanceof ApiError ? error.detail : 'That subforum could not be loaded.',
          );
          this.loading.set(false);
        }
      },
    });
  }
}
