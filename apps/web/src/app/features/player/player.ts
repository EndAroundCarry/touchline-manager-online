import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { averageRatingLabel } from '../../core/competition/competition-presentation';
import { ResultGate } from '../../core/match/result-gate';
import {
  attributeGroups,
  availabilityLabel,
  footLabel,
  isGoalkeeperPosition,
  matchCells,
  matchColumnsFor,
  positionLabel,
  releaseBlockedLabel,
  statCells,
  statColumnsFor,
  seasonsPlayedLabel,
  squadStatusLabel,
  stateRows,
} from '../../core/squad/squad-presentation';
import { PlayerRelease } from '../../core/squad/squad.models';
import { SquadStore } from '../../core/squad/squad-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import { AttributeValue } from '../../shared/ui/attribute-value/attribute-value';
import { ClubCrest } from '../../shared/ui/club-crest/club-crest';
import { ExperienceLevel } from '../../shared/ui/experience-level/experience-level';
import { PlayerPortrait } from '../../shared/ui/player-portrait/player-portrait';
import { FALLBACK_KIT, PortraitKit } from '../../shared/ui/player-portrait/portrait-designs';
import { PlayerTraining } from './player-training/player-training';
import {
  DESTRUCTIVE_BUTTON,
  FORM_ERROR,
  LINK,
  NAME_LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
  SELECT_INPUT,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';
import { ResultHidden } from '../../shared/ui/result-hidden/result-hidden';

/** The profile's tabs. */
export type PlayerTab = 'attributes' | 'training' | 'statistics' | 'contract';

/** The tabs another club's player shows. */
const PUBLIC_TABS: readonly PlayerTab[] = ['attributes', 'statistics'];

/** Where the release flow on the Contract tab has got to (`CON-12`). */
export type ReleaseStep = 'idle' | 'quoting' | 'confirming' | 'releasing' | 'released';

/** The statistics drop-down's value for the season in progress. */
const CURRENT_SEASON = 'current';

/**
 * The player profile (master plan §11.1, F-17), in four tabs: Attributes (the default), Training,
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
  imports: [
    RouterLink,
    AttributeValue,
    ClubCrest,
    ExperienceLevel,
    PlayerPortrait,
    PlayerTraining,
    ResultHidden,
  ],
  templateUrl: './player.html',
})
export class PlayerProfile implements OnInit {
  /** The player identity, bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly store = inject(SquadStore);
  private readonly gate = inject(ResultGate);

  protected readonly player = this.store.player;

  /**
   * Whether the statistics are held back. A player's season and match figures count the goals and results of
   * every match they played, so while a result of the manager's own is unseen they could give it away.
   */
  protected readonly resultsHeldBack = this.gate.hiding;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** The attribute families, in the order the profile shows them. */
  protected readonly attributeFamilies = computed(() => {
    const player = this.player();

    return player === null ? [] : attributeGroups(player.attributes, player.attributeProgress);
  });

  /** The shirt the portrait wears: the colours and crest of the club the player belongs to. */
  protected readonly kit = computed<PortraitKit>(() => {
    const club = this.player()?.club;

    return club === undefined
      ? FALLBACK_KIT
      : {
          primary: club.primaryColour,
          secondary: club.secondaryColour,
          crestShape: club.crestShape,
          crestPattern: club.crestPattern,
        };
  });

  /**
   * Whether the player is the manager's own. Another club's player shows the public half only: the attributes and
   * the statistics, never the state, the contract or the training, which are that club's to see.
   */
  protected readonly isOwn = computed(() => this.player()?.isOwnClub ?? false);

  /** The four state measures with their bands resolved. */
  protected readonly stateMeasures = computed(() => {
    const state = this.player()?.state;

    return state === null || state === undefined ? [] : stateRows(state);
  });

  /** Whether the player is a goalkeeper, whose statistics are the saves he made rather than the tackles and shots. */
  protected readonly isKeeper = computed(() =>
    isGoalkeeperPosition(this.player()?.primaryPosition),
  );

  /** The columns of the season table: a keeper's set or an outfield player's (`STA-2`). */
  protected readonly statColumns = computed(() => statColumnsFor(this.player()?.primaryPosition));

  /** The columns of the match table, after the round, the fixture and the result: again a keeper's or an outfield player's. */
  protected readonly matchColumns = computed(() => matchColumnsFor(this.player()?.primaryPosition));

  /** The classes of a numeric statistics cell or heading; every column stays, and a narrow screen scrolls the table sideways. */
  protected readonly statCellClass = 'px-2 py-2 text-right';

  /** One table row per season the player has a line for, most recent first (`STA-2`). */
  protected readonly seasonTableRows = computed(() =>
    this.careerSeasons().map((season) => ({
      key: `${season.seasonNumber}-${season.clubId}`,
      season: season.seasonLabel,
      clubId: season.clubId,
      club: season.clubName,
      cells: statCells(season.stats, this.statColumns()),
    })),
  );

  /** The career row that closes the table, or null before the player has ever appeared (`STA-2`). */
  protected readonly careerRow = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined
      ? null
      : statCells(career.totals, this.statColumns());
  });

  /** The player's seasons, most recent first, or an empty list (`STA-2`). */
  protected readonly careerSeasons = computed(() => this.player()?.careerStats?.seasons ?? []);

  /** How many seasons the player has appeared in, as a phrase (`STA-2`). */
  protected readonly seasonsPlayed = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined ? '' : seasonsPlayedLabel(career.seasonsPlayed);
  });

  /** Every tab, in the order they are shown. Attributes is the default (F-17). */
  private readonly allTabs: readonly { readonly key: PlayerTab; readonly label: string }[] = [
    { key: 'attributes', label: 'Attributes' },
    { key: 'training', label: 'Training' },
    { key: 'statistics', label: 'Statistics' },
    { key: 'contract', label: 'Contract' },
  ];

  /** The tabs this viewer may open: all four on their own player, the attributes and the statistics on any other. */
  protected readonly tabs = computed(() =>
    this.isOwn() ? this.allTabs : this.allTabs.filter((tab) => PUBLIC_TABS.includes(tab.key)),
  );

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

  /** The drop-down's choices: this season, then each earlier season the player has a line for. */
  protected readonly seasonOptions = computed(() => [
    { value: CURRENT_SEASON, label: `Current season (${this.currentSeasonNumber()})` },
    ...this.careerSeasons().map((season) => ({
      value: `${season.seasonNumber}`,
      label: `${season.seasonLabel} · ${season.clubName}`,
    })),
  ]);

  protected readonly matchesLoading = signal(true);
  protected readonly matchesError = signal<string | null>(null);

  /** The per-match rows for the chosen season, newest first (`STA-2`). */
  protected readonly matchRows = computed(() => {
    const history = this.store.playerMatches();
    const choice = this.selectedSeason();

    // The store holds the last player read, so a row set that is not this player's is ignored.
    if (history === null || history.playerId !== this.id()) {
      return [];
    }

    const season = choice === CURRENT_SEASON ? this.currentSeasonNumber() : Number(choice);

    return history.matches
      .filter((match) => match.seasonNumber === season)
      .map((match) => ({ match, cells: matchCells(match, this.matchColumns()) }));
  });

  protected readonly releaseStep = signal<ReleaseStep>('idle');
  protected readonly releaseError = signal<string | null>(null);
  protected readonly released = signal<PlayerRelease | null>(null);
  protected readonly releaseQuote = this.store.releaseQuote;

  /** Why the release is refused right now, in words, or null when it is allowed. */
  protected readonly releaseBlocked = computed(() => {
    const code = this.releaseQuote()?.blockedReason;

    return code === null || code === undefined ? null : releaseBlockedLabel(code);
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly destructiveButtonClass = DESTRUCTIVE_BUTTON;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly selectClass = SELECT_INPUT;
  protected readonly linkClass = LINK;
  protected readonly nameLinkClass = NAME_LINK;

  /** Reads the profile for the player the route names. */
  ngOnInit(): void {
    const playerId = this.id();

    this.gate.refresh();

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

    this.matchesLoading.set(true);
    this.matchesError.set(null);

    this.store.loadPlayerMatches(playerId).subscribe({
      next: () => this.matchesLoading.set(false),
      error: (error: unknown) => {
        this.matchesLoading.set(false);
        this.matchesError.set(
          error instanceof ApiError ? error.detail : 'The match statistics could not be loaded.',
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
    const tabs = this.tabs();
    const last = tabs.length - 1;
    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;

    if (target < 0) {
      return;
    }

    event.preventDefault();
    this.selectTab(tabs[target].key);
    (document.getElementById(`player-tab-${tabs[target].key}`) as HTMLElement | null)?.focus();
  }

  /** Asks what releasing the player costs, and opens the confirmation (`CON-12`). */
  protected startRelease(): void {
    const contract = this.player()?.contract;

    if (contract === null || contract === undefined) {
      return;
    }

    this.releaseError.set(null);
    this.releaseStep.set('quoting');

    this.store.quoteRelease(contract.id).subscribe({
      next: () => this.releaseStep.set('confirming'),
      error: (error: unknown) => this.failRelease(error, 'The cost could not be worked out.'),
    });
  }

  /** Releases the player on the quoted terms. The quoted version makes a stale confirmation fail rather than pay. */
  protected confirmRelease(): void {
    const quote = this.releaseQuote();

    if (quote === null || quote.blockedReason !== null || this.releaseStep() !== 'confirming') {
      return;
    }

    this.releaseError.set(null);
    this.releaseStep.set('releasing');

    this.store.releasePlayer(quote.contractId, quote.contractVersion).subscribe({
      next: (release) => {
        this.released.set(release);
        this.releaseStep.set('released');
      },
      error: (error: unknown) => this.failRelease(error, 'The player could not be released.'),
    });
  }

  /** Closes the confirmation without releasing anyone. */
  protected cancelRelease(): void {
    this.store.clearReleaseQuote();
    this.releaseError.set(null);
    this.releaseStep.set('idle');
  }

  private failRelease(error: unknown, fallback: string): void {
    // The quote may be stale or refused now; a fresh one is asked for when the manager tries again.
    this.store.clearReleaseQuote();
    this.releaseStep.set('idle');
    this.releaseError.set(error instanceof ApiError ? error.detail : fallback);
  }

  /** Chooses the season whose statistics are shown. */
  protected selectSeason(value: string): void {
    this.selectedSeason.set(value);
  }
}
