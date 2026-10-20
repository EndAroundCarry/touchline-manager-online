import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { CompetitionApi } from '../../core/competition/competition-api';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import {
  isUpcoming,
  outcomeLabel,
  roundLabel,
  scoreLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { ResultGate } from '../../core/match/result-gate';
import { PositionAverages, positionAverages } from '../../core/squad/position-ratings';
import {
  attributeBand,
  positionAbbreviation,
  positionLabel,
} from '../../core/squad/squad-presentation';
import { SquadApi } from '../../core/squad/squad-api';
import { ClubOverview } from '../../core/squad/squad.models';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK,
  LINK_ACTION,
  NAME_LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';
import { ClubCrest } from '../../shared/ui/club-crest/club-crest';
import { PlayerCardTrigger } from '../../shared/ui/player-card/player-card-trigger';

/** The club page's tabs. */
export type ClubTab = 'squad' | 'fixtures';

/** A squad row: the public player with the averages for the position they are listed at. */
type ClubSquadRow = ClubOverview['players'][number] & { readonly ratings: PositionAverages };

/**
 * Any club's page (master plan §11.1): who it is, the players it holds, and the season it has played and has
 * still to play, so a manager can study a rival before meeting it, or look back at a game a club played.
 *
 * It is a public view. The squad shows what scouting shows (exact attributes, never condition or contracts), and
 * the fixtures are the club's own list read from its side. A result that belongs to the manager's own club and
 * has not been seen yet stays behind the same gate the fixtures screen uses, so a rival's page cannot give away
 * the manager's own unseen match.
 */
@Component({
  selector: 'app-club',
  imports: [DecimalPipe, RouterLink, ClubCrest, PlayerCardTrigger],
  templateUrl: './club.html',
})
export class ClubPage {
  /** The club identity, bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly squadApi = inject(SquadApi);
  private readonly competitionApi = inject(CompetitionApi);
  private readonly gate = inject(ResultGate);

  protected readonly club = signal<ClubOverview | null>(null);
  protected readonly season = signal<MyFixtures | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** The fixtures could be missing without the squad being so: a club with no division this season has none. */
  protected readonly fixturesError = signal<string | null>(null);

  /** The tabs, in the order they are shown. The squad is the default. */
  protected readonly tabs: readonly { readonly key: ClubTab; readonly label: string }[] = [
    { key: 'squad', label: 'Squad' },
    { key: 'fixtures', label: 'Fixtures' },
  ];

  /** The tab currently open. */
  protected readonly activeTab = signal<ClubTab>('squad');

  /** The players, goalkeepers first as the server lists them, each rated for the position they are listed at. */
  protected readonly players = computed<readonly ClubSquadRow[]>(() =>
    (this.club()?.players ?? []).map((player) => ({
      ...player,
      ratings: positionAverages(player.attributes, player.primaryPosition),
    })),
  );

  /** The fixtures still ahead of publication, in round order. */
  protected readonly upcoming = computed(() =>
    (this.season()?.fixtures ?? []).filter((fixture) => isUpcoming(fixture.status)),
  );

  /** The fixtures that have published a result, most recent first, so the latest game is the first to analyse. */
  protected readonly results = computed(() =>
    (this.season()?.fixtures ?? [])
      .filter((fixture) => fixture.status === 'published')
      .slice()
      .reverse(),
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly actionLinkClass = LINK_ACTION;
  protected readonly nameLinkClass = NAME_LINK;

  constructor() {
    // Opening a rival from a club's fixture list stays on this route with a new identity, so the screen reads
    // again whenever the identity changes rather than once.
    effect(() => {
      const clubId = this.id();

      untracked(() => this.load(clubId));
    });
  }

  /** Reads the club and its season. */
  private load(clubId: string): void {
    this.gate.refresh();

    this.club.set(null);
    this.season.set(null);
    this.activeTab.set('squad');
    this.loading.set(true);
    this.loadError.set(null);
    this.fixturesError.set(null);

    this.squadApi.club(clubId).subscribe({
      next: (club) => {
        if (clubId !== this.id()) {
          return;
        }

        this.club.set(club);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        if (clubId !== this.id()) {
          return;
        }

        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'That club could not be loaded.',
        );
      },
    });

    this.competitionApi.clubFixtures(clubId).subscribe({
      next: (season) => {
        if (clubId === this.id()) {
          this.season.set(season);
        }
      },
      error: (error: unknown) => {
        if (clubId === this.id()) {
          this.fixturesError.set(
            error instanceof ApiError ? error.detail : 'The fixtures could not be loaded.',
          );
        }
      },
    });
  }

  /** Opens a tab. */
  protected selectTab(tab: ClubTab): void {
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
    (document.getElementById(`club-tab-${this.tabs[target].key}`) as HTMLElement | null)?.focus();
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** The short form of a position code. */
  protected positionShort(code: string): string {
    return positionAbbreviation(code);
  }

  /** The tint for a skill average. */
  protected skillClass(value: number): string {
    return attributeBand(value).className;
  }

  /** Formats a kickoff in the viewer's local time. */
  protected kickoff(instant: string): string {
    return formatInstant(instant);
  }

  /** Names the club's side in a fixture. */
  protected venue(fixture: ClubFixture): string {
    return venueLabel(fixture.venue);
  }

  /** A round's label. */
  protected round(roundNumber: number): string {
    return roundLabel(roundNumber);
  }

  /** The scoreline, the home side's goals first, as the names beside it are ordered. */
  protected score(fixture: ClubFixture): string {
    return scoreLabel(fixture.homeScore, fixture.awayScore);
  }

  /** Names a published result from this club's point of view. */
  protected outcome(fixture: ClubFixture): string {
    return outcomeLabel(fixture.outcome);
  }

  /** Whether a result is held back: it is one of the viewer's own matches, and unseen. */
  protected hidden(fixture: ClubFixture): boolean {
    return this.gate.isHidden(fixture.matchId);
  }

  /** Shows one result. */
  protected show(fixture: ClubFixture): void {
    this.gate.reveal(fixture.matchId);
  }
}
