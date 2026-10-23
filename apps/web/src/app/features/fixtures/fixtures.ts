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
import { FanExpectation } from '../../core/fans/fan.models';
import { FanStore } from '../../core/fans/fan-store';
import { ResultGate } from '../../core/match/result-gate';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { formatDeadline, formatInstant } from '../../core/world/presentation';
import { ClubCrest } from '../../shared/ui/club-crest/club-crest';
import { FanExpectationChip } from '../../shared/ui/fan-expectation-chip/fan-expectation-chip';
import {
  FORM_ERROR,
  LINK_ACTION,
  NAME_LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';

/** The fixtures screen's tabs. */
export type FixturesTab = 'upcoming' | 'results';

/**
 * The fixtures screen (master plan §11.1).
 *
 * The manager's club's season in two tabs: what is still to play (with the next fixture called out beside its
 * deadline) and what has been played, newest first, so the latest result is never a scroll away. The list is
 * bounded to 30 fixtures per club (§10.5), so it needs no pagination and no virtual scroll.
 *
 * The countdown is the one thing here that moves, so it is driven by a signal on a slow interval rather
 * than left to a one-off render — a deadline that never ticks down would be worse than no countdown. The
 * interval is cleared when the component goes away.
 */
@Component({
  selector: 'app-fixtures',
  imports: [RouterLink, ClubCrest, FanExpectationChip],
  templateUrl: './fixtures.html',
})
export class Fixtures implements OnDestroy {
  private readonly store = inject(CompetitionStore);
  private readonly gate = inject(ResultGate);
  private readonly onboarding = inject(OnboardingStore);
  private readonly fanStore = inject(FanStore);
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

  /**
   * What the fans expect from the next fixture, or null while it is unread or belongs to a match that is no longer
   * next: the fans' read and the fixture list are separate reads, and a chip for the wrong match would be worse than none.
   */
  protected readonly fanExpectation = computed<{
    readonly expectation: FanExpectation;
    readonly frozen: boolean;
  } | null>(() => {
    const next = this.nextFixture();
    const expected = this.fanStore.nextFixture();

    return next !== null && expected !== null && expected.fixture.id === next.id
      ? { expectation: expected.expectation, frozen: expected.frozen }
      : null;
  });

  /** The club the manager holds, which carries the colours and crest it wears. */
  protected readonly tenure = computed(() => this.onboarding.state()?.tenure ?? null);

  /** The fixtures still ahead of publication, in round order. */
  protected readonly upcoming = computed(() =>
    (this.fixtures()?.fixtures ?? []).filter((fixture) => isUpcoming(fixture.status)),
  );

  /** The upcoming fixtures after the one called out at the top, which would otherwise be listed twice. */
  protected readonly afterNext = computed(() => {
    const next = this.nextFixture();

    return next === null ? this.upcoming() : this.upcoming().filter((f) => f.id !== next.id);
  });

  /** The fixtures that have published a result, the latest round first. */
  protected readonly results = computed(() =>
    (this.fixtures()?.fixtures ?? [])
      .filter((fixture) => fixture.status === 'published')
      .sort((a, b) => b.roundNumber - a.roundNumber),
  );

  /** The tabs, in the order they are shown, each with how many fixtures it holds. */
  protected readonly tabs = computed<
    readonly { readonly key: FixturesTab; readonly label: string; readonly count: number }[]
  >(() => [
    { key: 'upcoming', label: 'Upcoming', count: this.upcoming().length },
    { key: 'results', label: 'Results', count: this.results().length },
  ]);

  /** The tab currently open. */
  protected readonly activeTab = signal<FixturesTab>('upcoming');

  /** How many of the results listed are still held back, for the "show all" shortcut. */
  protected readonly hiddenCount = computed(
    () => this.results().filter((fixture) => this.gate.isHidden(fixture.matchId)).length,
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly nameLinkClass = NAME_LINK;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    this.store.loadFixtures();
    this.fanStore.load();

    this.timer = setInterval(() => this.nowSignal.set(new Date()), 30_000);
  }

  /** Stops the countdown's interval so no timer outlives the screen. */
  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  /** Opens a tab. */
  protected selectTab(tab: FixturesTab): void {
    this.activeTab.set(tab);
  }

  /** Moves between tabs with the arrow keys, Home and End, as the tabs pattern expects. */
  protected onTabKeydown(event: KeyboardEvent, index: number): void {
    const count = this.tabs().length;
    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % count
        : event.key === 'ArrowLeft'
          ? (index + count - 1) % count
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? count - 1
              : -1;

    if (target < 0) {
      return;
    }

    event.preventDefault();
    const key = this.tabs()[target].key;
    this.selectTab(key);
    (document.getElementById(`fixtures-tab-${key}`) as HTMLElement | null)?.focus();
  }

  /** The tint of a result's outcome badge. The word is always shown beside it, so colour is never alone. */
  protected outcomeClass(fixture: ClubFixture): string {
    switch (fixture.outcome) {
      case 'win':
        return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200';
      case 'loss':
        return 'border-red-500/40 bg-red-500/10 text-red-200';
      default:
        return 'border-amber-500/40 bg-amber-500/10 text-amber-200';
    }
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
