import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClubFixture, MyFixtures } from '../../core/competition/competition.models';
import {
  fixtureStatusLabel,
  isUpcoming,
  lockCountdown,
  outcomeLabel,
  roundLabel,
  scoreLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { ResultGate } from '../../core/match/result-gate';
import { formatDeadline, formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

/**
 * The fixtures screen (master plan §11.1).
 *
 * The manager's club's season in one list: what is still to play, what has been played, and the next
 * fixture called out with its deadline. The list is bounded to 34 fixtures per club (§10.5), so it needs no
 * pagination and no virtual scroll.
 *
 * The countdown is the one thing here that moves, so it is driven by a signal on a slow interval rather
 * than left to a one-off render — a deadline that never ticks down would be worse than no countdown. The
 * interval is cleared when the component goes away.
 */
@Component({
  selector: 'app-fixtures',
  imports: [RouterLink],
  templateUrl: './fixtures.html',
})
export class Fixtures implements OnDestroy {
  private readonly store = inject(CompetitionStore);
  private readonly gate = inject(ResultGate);
  private readonly timer: ReturnType<typeof setInterval>;

  /** The clock the countdown is measured against, advanced on an interval. */
  private readonly nowSignal = signal(new Date());

  protected readonly fixtures = this.store.fixtures;
  protected readonly loading = this.store.fixturesLoading;
  protected readonly loadError = this.store.fixturesError;

  /** The next fixture still to be played, or null once the season is done. */
  protected readonly nextFixture = computed<ClubFixture | null>(() =>
    this.nextFrom(this.fixtures()),
  );

  /** The fixtures still ahead of publication, in round order. */
  protected readonly upcoming = computed(() =>
    (this.fixtures()?.fixtures ?? []).filter((fixture) => isUpcoming(fixture.status)),
  );

  /** The fixtures that have published a result. */
  protected readonly results = computed(() =>
    (this.fixtures()?.fixtures ?? []).filter((fixture) => fixture.status === 'published'),
  );

  /** How many of the results listed are still held back, for the "show all" shortcut. */
  protected readonly hiddenCount = computed(
    () => this.results().filter((fixture) => this.gate.isHidden(fixture.matchId)).length,
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    this.store.loadFixtures();

    this.timer = setInterval(() => this.nowSignal.set(new Date()), 30_000);
  }

  /** Stops the countdown's interval so no timer outlives the screen. */
  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  /** Formats a kickoff in the viewer's local time (`CAL-4`). */
  protected kickoff(instant: string): string {
    return formatInstant(instant);
  }

  /** Formats a team-sheet deadline in the viewer's local time, naming the zone (`VOI-4`, `CAL-4`). */
  protected deadline(instant: string): string {
    return formatDeadline(instant);
  }

  /** Names a fixture's lifecycle state. */
  protected status(code: string): string {
    return fixtureStatusLabel(code);
  }

  /** Names the manager's club's side. */
  protected venue(fixture: ClubFixture): string {
    return venueLabel(fixture.venue);
  }

  /** Formats a published scoreline. */
  protected score(fixture: ClubFixture): string {
    return scoreLabel(fixture.homeScore, fixture.awayScore);
  }

  /**
   * Whether a result is held back until the manager has watched the match or asks for it.
   *
   * The scoreline and the outcome are what a match would give away, so neither is shown while this is true.
   */
  protected hidden(fixture: ClubFixture): boolean {
    return this.gate.isHidden(fixture.matchId);
  }

  /** Shows one result. */
  protected show(fixture: ClubFixture): void {
    this.gate.reveal(fixture.matchId);
  }

  /** Shows every result that is held back. */
  protected showAll(): void {
    this.gate.revealAll();
  }

  /** Names a published result from the manager's club's point of view. */
  protected outcome(fixture: ClubFixture): string {
    return outcomeLabel(fixture.outcome);
  }

  /** A round's label. */
  protected round(roundNumber: number): string {
    return roundLabel(roundNumber);
  }

  /** How long until this fixture's sheets lock (`CAL-3`). */
  protected countdown(instant: string): string {
    return lockCountdown(instant, this.nowSignal());
  }

  /** Whether a fixture's side can still be prepared. */
  protected canPrepare(fixture: ClubFixture): boolean {
    return fixture.status === 'scheduled';
  }

  private nextFrom(list: MyFixtures | null): ClubFixture | null {
    if (list === null || list.nextFixtureId === null) {
      return null;
    }

    return list.fixtures.find((fixture) => fixture.id === list.nextFixtureId) ?? null;
  }
}
