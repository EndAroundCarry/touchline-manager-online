import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatInstant } from '../../core/world/presentation';
import { newsCategoryLabel } from '../../core/news/news-presentation';
import { NewsStore } from '../../core/news/news-store';
import { LINK, FORM_ERROR, PAGE_HEADING, SECONDARY_BUTTON, STATUS_MESSAGE } from '../../shared/forms/control-styles';

/**
 * The division news feed (`COM-1`, master plan §11.1).
 *
 * Public game data, newest first, and the same message shape the inbox uses: a headline, a sentence,
 * and the shelf it belongs to. "Load older" walks the keyset cursor.
 */
@Component({
  selector: 'app-news',
  imports: [RouterLink],
  templateUrl: './news.html',
})
export class News {
  private readonly store = inject(NewsStore);

  protected readonly items = this.store.items;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly error = this.store.error;
  protected readonly hasMore = this.store.hasMore;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;

  constructor() {
    this.store.load();
  }

  /** Reads the next page. */
  protected loadMore(): void {
    this.store.loadMore();
  }

  /** Names the kind of event an item reports. */
  protected category(code: string): string {
    return newsCategoryLabel(code);
  }

  /** Formats an instant for a reader. */
  protected time(instant: string): string {
    return formatInstant(instant);
  }
}
