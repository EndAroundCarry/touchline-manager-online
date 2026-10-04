import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { formatInstant } from '../../core/world/presentation';
import { ResultGate } from '../../core/match/result-gate';
import { newsRevealKey, ResultRevealStore } from '../../core/match/result-reveal-store';
import { NewsItem } from '../../core/news/news.models';
import { newsCategoryLabel } from '../../core/news/news-presentation';
import { NewsStore } from '../../core/news/news-store';
import {
  LINK,
  FORM_ERROR,
  PAGE_HEADING,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

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
  private readonly gate = inject(ResultGate);
  private readonly reveals = inject(ResultRevealStore);

  protected readonly items = this.store.items;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly error = this.store.error;
  protected readonly hasMore = this.store.hasMore;

  /** How many results on the page are held back, for the "show all" shortcut. */
  protected readonly hiddenCount = computed(
    () => this.items().filter((item) => this.hidden(item)).length,
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;

  constructor() {
    this.store.load();
    this.gate.refresh();
  }

  /**
   * Whether a result item's score is held back.
   *
   * Only the manager's own matches are protected, so an item that names its match is held back while that match
   * is one of the manager's own and unseen — and until the manager's fixtures have been read, so a score is never
   * shown first and hidden after. An item that does not name its match cannot be told from the manager's own, so
   * it is held back until the manager asks for it.
   */
  protected hidden(item: NewsItem): boolean {
    if (item.spoiler === null) {
      return false;
    }

    if (item.matchId === null) {
      return !this.reveals.isRevealed(newsRevealKey(item.id));
    }

    return !this.gate.settled() || this.gate.isHidden(item.matchId);
  }

  /** Shows one result. */
  protected show(item: NewsItem): void {
    this.reveals.reveal(item.matchId ?? newsRevealKey(item.id));
  }

  /** Shows every result on the page that is held back, and the manager's own that are unseen. */
  protected showAll(): void {
    this.gate.revealAll();

    for (const item of this.items()) {
      if (item.spoiler !== null && item.matchId === null) {
        this.reveals.reveal(newsRevealKey(item.id));
      }
    }
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
