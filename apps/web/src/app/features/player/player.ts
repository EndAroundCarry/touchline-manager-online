import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { averageRatingLabel } from '../../core/competition/competition-presentation';
import {
  attributeGroups,
  availabilityLabel,
  footLabel,
  positionLabel,
  seasonStatRows,
  seasonsPlayedLabel,
  squadStatusLabel,
  stateRows,
} from '../../core/squad/squad-presentation';
import { SquadStore } from '../../core/squad/squad-store';
import { sampleTrainingHistory } from '../../core/training/training-history';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import { AttributeValue } from '../../shared/ui/attribute-value/attribute-value';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
  SELECT_INPUT,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

/** The profile's tabs. */
export type PlayerTab = 'attributes' | 'training' | 'statistics' | 'contract';

/** The statistics drop-down's value for the season in progress. */
const CURRENT_SEASON = 'current';

/**
 * The player profile (master plan §11.1, F-17), in four tabs: Attributes (the default), Training report,
 * Statistics, and Contract.
 *
 * The attribute grid is the reason this screen exists. Each family is one row of compact tiles that fits
 * the screen width, rendered through `AttributeValue` so every attribute carries its number and, for
 * assistive technology, the word for its band — §11.3 forbids a colour being the only signal, and a test
 * asserts both halves render.
 *
 * The season summary is the player's line of the division leaderboard's projection (`STA-2`), read with
 * the profile rather than recomputed; a player who has not taken the pitch has none, and the screen says so
 * rather than showing a row of zeros.
 */
@Component({
  selector: 'app-player',
  imports: [RouterLink, AttributeValue],
  templateUrl: './player.html',
})
export class PlayerProfile implements OnInit {
  /** The player identity, bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly store = inject(SquadStore);

  protected readonly player = this.store.player;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** The attribute families, in the order the profile shows them. */
  protected readonly attributeFamilies = computed(() => {
    const player = this.player();

    return player === null ? [] : attributeGroups(player.attributes);
  });

  /** The four state measures with their bands resolved. */
  protected readonly stateMeasures = computed(() => {
    const player = this.player();

    return player === null ? [] : stateRows(player.state);
  });

  /** This season's summary lines, or an empty list before the player has appeared (`STA-2`). */
  protected readonly seasonStats = computed(() => {
    const stats = this.player()?.seasonStats;

    return stats === null || stats === undefined ? [] : seasonStatRows(stats);
  });

  /** The player's career totals as summary lines, or an empty list before they have ever appeared (`STA-2`). */
  protected readonly careerTotals = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined ? [] : seasonStatRows(career.totals);
  });

  /** The player's seasons, most recent first, or an empty list (`STA-2`). */
  protected readonly careerSeasons = computed(() => this.player()?.careerStats?.seasons ?? []);

  /** How many seasons the player has appeared in, as a phrase (`STA-2`). */
  protected readonly seasonsPlayed = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined ? '' : seasonsPlayedLabel(career.seasonsPlayed);
  });

  /** The tabs, in the order they are shown. Attributes is the default (F-17). */
  protected readonly tabs: readonly { readonly key: PlayerTab; readonly label: string }[] = [
    { key: 'attributes', label: 'Attributes' },
    { key: 'training', label: 'Training report' },
    { key: 'statistics', label: 'Statistics' },
    { key: 'contract', label: 'Contract' },
  ];

  /** The tab currently open. */
  protected readonly activeTab = signal<PlayerTab>('attributes');

  /** The statistics season chosen in the drop-down: `current` or a past season's number. */
  protected readonly selectedSeason = signal<string>(CURRENT_SEASON);

  /** The season the manager is in, derived from the contract when there is one. */
  protected readonly currentSeasonNumber = computed(() => {
    const player = this.player();
    const contract = player?.contract;

    if (contract !== null && contract !== undefined && contract.seasonsRemaining > 0) {
      return contract.endSeasonNumber - contract.seasonsRemaining + 1;
    }

    const seasons = player?.careerStats?.seasons ?? [];

    return seasons.reduce((latest, season) => Math.max(latest, season.seasonNumber), 0) + 1;
  });

  /** The sample training history, newest first. Placeholder until the training rework (see the model). */
  protected readonly trainingHistory = computed(() => {
    const player = this.player();

    return player === null ? [] : sampleTrainingHistory(player.id, this.currentSeasonNumber());
  });

  /** The drop-down's choices: this season, then each earlier season the player has a line for. */
  protected readonly seasonOptions = computed(() => [
    { value: CURRENT_SEASON, label: `Current season (${this.currentSeasonNumber()})` },
    ...this.careerSeasons().map((season) => ({
      value: `${season.seasonNumber}`,
      label: `${season.seasonLabel} · ${season.clubName}`,
    })),
  ]);

  /** The statistic lines for the chosen season, or an empty list when the player has none for it. */
  protected readonly selectedStats = computed(() => {
    const choice = this.selectedSeason();

    if (choice === CURRENT_SEASON) {
      return this.seasonStats();
    }

    const season = this.careerSeasons().find((item) => `${item.seasonNumber}` === choice);

    return season === undefined ? [] : seasonStatRows(season.stats);
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly selectClass = SELECT_INPUT;
  protected readonly linkClass = LINK;

  /** Reads the profile for the player the route names. */
  ngOnInit(): void {
    const playerId = this.id();

    this.loading.set(true);
    this.loadError.set(null);

    this.store.loadPlayer(playerId).subscribe({
      next: () => this.loading.set(false),
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'That player could not be loaded.',
        );
      },
    });
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** Names a squad status. */
  protected squadStatus(code: string): string {
    return squadStatusLabel(code);
  }

  /** Names a preferred foot. */
  protected foot(code: string): string {
    return footLabel(code);
  }

  /** Formats a career season's average rating to one decimal, or a dash before there is one (`TRN-8`). */
  protected rating(value: number | null): string {
    return averageRatingLabel(value);
  }

  /** Describes an injury or suspension in fixtures. */
  protected availability(type: string, remainingFixtures: number): string {
    return availabilityLabel(type, remainingFixtures);
  }

  /** Opens a tab. */
  protected selectTab(tab: PlayerTab): void {
    this.activeTab.set(tab);
  }

  /** Moves between tabs with the arrow keys, Home and End, as the tabs pattern expects. */
  protected onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % this.tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % this.tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;

    if (target < 0) {
      return;
    }

    event.preventDefault();
    this.selectTab(this.tabs[target].key);
    (document.getElementById(`player-tab-${this.tabs[target].key}`) as HTMLElement | null)?.focus();
  }

  /** Chooses the season whose statistics are shown. */
  protected selectSeason(value: string): void {
    this.selectedSeason.set(value);
  }
}
