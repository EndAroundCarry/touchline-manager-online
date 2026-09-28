import { Component, inject } from '@angular/core';
import { ScoutingStore, PLAYER_SORTS } from '../../core/scouting/scouting-store';
import { PlayerSearchResult, PlayerSort } from '../../core/scouting/scouting.models';
import { PlayerAttributes } from '../../core/squad/squad.models';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/** The position families a manager may filter by (`SCT-1`). */
const POSITION_FAMILIES = ['Goalkeeper', 'Defence', 'Midfield', 'Attack'] as const;

/**
 * The scouting screen (master plan §11.1; `SCT-1`, `SCT-3`).
 *
 * A global search over every club's players with exact public attributes, and the manager's own private
 * shortlist beside it. The search is server-side; this screen only holds the filters and the page.
 */
@Component({
  selector: 'app-scouting',
  templateUrl: './scouting.html',
})
export class Scouting {
  private readonly store = inject(ScoutingStore);

  protected readonly results = this.store.results;
  protected readonly shortlist = this.store.shortlist;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly hasMore = this.store.hasMore;
  protected readonly error = this.store.error;

  protected readonly sorts = PLAYER_SORTS;
  protected readonly families = POSITION_FAMILIES;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly formErrorClass = FORM_ERROR;

  constructor() {
    this.store.search();
    this.store.loadShortlist();
  }

  /** Applies the submitted filters and searches from the first page. */
  protected onFilter(event: Event): void {
    event.preventDefault();

    const form = event.target as HTMLFormElement;
    const data = new FormData(form);
    const text = (name: string): string | null => {
      const value = data.get(name);

      return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
    };
    const number = (name: string): number | null => {
      const value = text(name);

      return value === null ? null : Number(value);
    };
    const sort = (data.get('sort') as PlayerSort | null) ?? 'name';
    const position = text('position');

    this.store.setFilters({
      name: text('name'),
      position,
      ageMin: number('ageMin'),
      ageMax: number('ageMax'),
      abilityMin: number('abilityMin'),
      sort,
    });
  }

  /** Reads the next page of results. */
  protected loadMore(): void {
    this.store.loadMore();
  }

  /** Adds a player to the shortlist. */
  protected shortlistPlayer(playerId: string): void {
    this.store.add(playerId, null);
  }

  /** Removes a player from the shortlist. */
  protected unshortlist(playerId: string): void {
    this.store.remove(playerId);
  }

  /** Whether a player is on the shortlist. */
  protected isShortlisted(playerId: string): boolean {
    return this.store.isShortlisted(playerId);
  }

  /** The mean of a player's twenty-eight attributes, for a compact ordering signal (`SCT-1`). */
  protected abilityOf(player: PlayerSearchResult): number {
    const values = attributeValues(player.attributes);

    return values.length === 0
      ? 0
      : Math.round(values.reduce((sum, value) => sum + value, 0) / values.length);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}

/** Flattens an attribute set into its values, in memory only. */
function attributeValues(attributes: PlayerAttributes): number[] {
  return [
    ...Object.values(attributes.technical),
    ...Object.values(attributes.mental),
    ...Object.values(attributes.physical),
    ...Object.values(attributes.goalkeeping),
  ];
}
