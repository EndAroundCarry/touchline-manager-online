import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ForumApi } from '../../core/forum/forum-api';
import { ForumOverview } from '../../core/forum/forum.models';
import { formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, NAME_LINK, PAGE_HEADING } from '../../shared/forms/control-styles';

/**
 * The forum's front page: the international forum and the forum of the manager's own country, each with its
 * subforums.
 *
 * The international forum carries its language rule on this page and on every page below it, so nobody has to
 * open a thread to learn that posts there are in English. A manager who runs no club has no country, so only the
 * international forum is listed and the page says why.
 */
@Component({
  selector: 'app-forum',
  imports: [RouterLink],
  templateUrl: './forum.html',
})
export class Forum {
  private readonly api = inject(ForumApi);

  protected readonly overview = signal<ForumOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly nameLinkClass = NAME_LINK;

  constructor() {
    this.api.overview().subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.error.set(error instanceof ApiError ? error.detail : 'The forum could not be loaded.');
        this.loading.set(false);
      },
    });
  }

  /** Whether the manager has a country forum to show. */
  protected hasCountrySection(overview: ForumOverview): boolean {
    return overview.sections.some((section) => section.countryId !== null);
  }

  /** Formats an instant for a reader. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
