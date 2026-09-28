import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { ScoutingApi } from './scouting-api';
import { PlayerSearchResult, PlayerSort, ScoutingFilters, ShortlistEntry } from './scouting.models';

/**
 * The scouting module's view state (master plan §11.1; `SCT-1`, `SCT-3`).
 *
 * A feature-scoped store following `FinanceStore`. The search is server-side and keyset-paged; the shortlist
 * is the manager's own and is refreshed from the server after every change, so the screen never guesses.
 */
@Injectable({ providedIn: 'root' })
export class ScoutingStore {
  private readonly api = inject(ScoutingApi);

  private readonly filtersSignal = signal<ScoutingFilters>({
    name: null,
    position: null,
    ageMin: null,
    ageMax: null,
    abilityMin: null,
    sort: 'name',
  });

  private readonly resultsSignal = signal<readonly PlayerSearchResult[]>([]);
  private readonly nextCursorSignal = signal<string | null>(null);
  private readonly shortlistSignal = signal<readonly ShortlistEntry[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly loadingMoreSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  /** The filters the current results were produced with. */
  readonly filters = this.filtersSignal.asReadonly();

  /** The search results read so far. */
  readonly results = this.resultsSignal.asReadonly();

  /** The manager's private shortlist. */
  readonly shortlist = this.shortlistSignal.asReadonly();

  /** Whether a further page of results exists. */
  readonly hasMore = computed(() => this.nextCursorSignal() !== null);

  /** Whether the first search is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Whether a further page is in flight. */
  readonly loadingMore = this.loadingMoreSignal.asReadonly();

  /** Why the last request failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Replaces the filters and searches from the first page. */
  setFilters(filters: Partial<ScoutingFilters>): void {
    this.filtersSignal.set({ ...this.filtersSignal(), ...filters });
    this.search();
  }

  /** Searches from the first page with the current filters. */
  search(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.resultsSignal.set([]);
    this.nextCursorSignal.set(null);

    this.api.search(this.filtersSignal(), null).subscribe({
      next: (page) => {
        this.resultsSignal.set(page.players);
        this.nextCursorSignal.set(page.nextCursor);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => this.fail(error, 'Players could not be loaded.'),
    });
  }

  /** Reads the next page and appends it. */
  loadMore(): void {
    const cursor = this.nextCursorSignal();

    if (cursor === null || this.loadingMoreSignal()) {
      return;
    }

    this.loadingMoreSignal.set(true);

    this.api.search(this.filtersSignal(), cursor).subscribe({
      next: (page) => {
        this.resultsSignal.set([...this.resultsSignal(), ...page.players]);
        this.nextCursorSignal.set(page.nextCursor);
        this.loadingMoreSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingMoreSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'More players could not be loaded.',
        );
      },
    });
  }

  /** Loads the manager's shortlist. */
  loadShortlist(): void {
    this.api.getShortlist().subscribe({
      next: (shortlist) => this.shortlistSignal.set(shortlist.entries),
      error: (error: unknown) => this.fail(error, 'Your shortlist could not be loaded.'),
    });
  }

  /** Adds a player to the shortlist and refreshes it. */
  add(playerId: string, notes: string | null): void {
    this.api.add(playerId, notes).subscribe({
      next: (shortlist) => this.shortlistSignal.set(shortlist.entries),
      error: (error: unknown) => this.fail(error, 'That player could not be shortlisted.'),
    });
  }

  /** Removes a player from the shortlist and refreshes it. */
  remove(playerId: string): void {
    this.api.remove(playerId).subscribe({
      next: (shortlist) => this.shortlistSignal.set(shortlist.entries),
      error: (error: unknown) => this.fail(error, 'That player could not be removed.'),
    });
  }

  /** Whether a player is on the shortlist. */
  isShortlisted(playerId: string): boolean {
    return this.shortlistSignal().some((entry) => entry.playerId === playerId);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.resultsSignal.set([]);
    this.nextCursorSignal.set(null);
    this.shortlistSignal.set([]);
    this.loadingSignal.set(false);
    this.loadingMoreSignal.set(false);
    this.errorSignal.set(null);
  }

  private fail(error: unknown, fallback: string): void {
    this.loadingSignal.set(false);
    this.errorSignal.set(error instanceof ApiError ? error.detail : fallback);
  }
}

/** The sort options a manager may choose. */
export const PLAYER_SORTS: readonly { readonly value: PlayerSort; readonly label: string }[] = [
  { value: 'name', label: 'Name' },
  { value: 'age', label: 'Age' },
  { value: 'ability', label: 'Ability' },
];
