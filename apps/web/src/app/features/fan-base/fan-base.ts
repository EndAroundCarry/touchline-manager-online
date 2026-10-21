import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClubFixture } from '../../core/competition/competition.models';
import {
  outcomeLabel,
  roundLabel,
  scoreLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { FanEvent, FanHistoryScope, FanRecentMatch } from '../../core/fans/fan.models';
import { FanStore } from '../../core/fans/fan-store';
import {
  CALM_FROM,
  CALM_TO,
  MOOD_BANDS,
  chancesLabel,
  eventDate,
  eventKindLabel,
  eventNote,
  expectationToneClass,
  expectedPoints,
  fanCountPlot,
  fanCountSummary,
  gapLabel,
  groupedNumber,
  holdBackHistory,
  latestSeasonEvents,
  moodChange,
  moodPoints,
  moodWordFor,
  ordinalPlace,
  predictedScore,
  satisfactionPlot,
  satisfactionSummary,
  showsCap,
  signedNumber,
} from '../../core/fans/fan-presentation';
import { ResultGate } from '../../core/match/result-gate';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  NAME_LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';
import { ClubCrest } from '../../shared/ui/club-crest/club-crest';
import { FanExpectationChip } from '../../shared/ui/fan-expectation-chip/fan-expectation-chip';
import { LineChart } from '../../shared/ui/line-chart/line-chart';
import { ResultHidden } from '../../shared/ui/result-hidden/result-hidden';

/** The Fan Base page's history choices, in the order they are shown. */
const SCOPES: readonly { readonly key: FanHistoryScope; readonly label: string }[] = [
  { key: 'tenure', label: 'Since you took over' },
  { key: 'club', label: 'Whole club history' },
];

/**
 * The Fan Base page (`FAN-1`…`FAN-10`).
 *
 * It shows the club's fans as they stand, what they expect from the next match, how the last ones went, and two
 * charts: how satisfied the fans are this season, and how many there are over every season.
 *
 * A fan's reaction is a result in disguise: the mood and the count that follow a match move by what happened in it.
 * So the page honours the result gate like the table does. While one of the manager's own results is unseen, the
 * history is cut before that match and every number that comes from it is held back, with the same way out the rest
 * of the game gives: watch the match, or show the result.
 */
@Component({
  selector: 'app-fan-base',
  imports: [RouterLink, ClubCrest, FanExpectationChip, LineChart, ResultHidden],
  templateUrl: './fan-base.html',
})
export class FanBasePage {
  private readonly store = inject(FanStore);
  private readonly gate = inject(ResultGate);
  private readonly onboarding = inject(OnboardingStore);

  protected readonly fans = this.store.fans;
  protected readonly loading = this.store.loading;
  protected readonly error = this.store.error;

  /** Whether the manager's fixture list has been read, so the gate can say which results are unseen. */
  protected readonly settled = this.gate.settled;

  /** The club the manager holds, which carries the colours and crest it wears. */
  protected readonly tenure = computed(() => this.onboarding.state()?.tenure ?? null);

  protected readonly scopes = SCOPES;
  protected readonly scope = signal<FanHistoryScope>('tenure');

  /** The history with the part that would give a result away cut off. */
  private readonly held = computed(() => {
    const hidden = new Set(this.gate.unseen().map((fixture) => fixture.id));

    return holdBackHistory(this.fans()?.events ?? [], hidden);
  });

  /** The history the page may show. */
  protected readonly events = computed(() => this.held().events);

  /** Whether the page is holding something back. */
  protected readonly heldBack = computed(() => this.held().heldBack);

  /** The last point the page may show: the fans and the mood as the manager is allowed to know them. */
  private readonly latest = computed<FanEvent | null>(() => {
    const events = this.events();

    return events.length === 0 ? null : events[events.length - 1];
  });

  /** The fans to show. */
  protected readonly fanCount = computed(() =>
    this.heldBack() ? (this.latest()?.fansAfter ?? null) : (this.fans()?.fans ?? null),
  );

  /** The satisfaction to show, 0–100. */
  protected readonly satisfaction = computed(() => {
    const fans = this.fans();

    if (fans === null) {
      return null;
    }

    return moodPoints(this.heldBack() ? (this.latest()?.moodAfterBp ?? fans.moodBp) : fans.moodBp);
  });

  /** The mood word to show. */
  protected readonly moodPhrase = computed(() => {
    const fans = this.fans();

    if (fans === null) {
      return '';
    }

    return this.heldBack()
      ? moodWordFor(this.latest()?.moodAfterBp ?? fans.moodBp).phrase
      : fans.moodPhrase;
  });

  /** The events of the season the satisfaction chart draws. */
  protected readonly seasonEvents = computed(() => latestSeasonEvents(this.events()));

  protected readonly satisfactionPlot = computed(() => satisfactionPlot(this.seasonEvents()));
  protected readonly satisfactionLabel = computed(() => satisfactionSummary(this.seasonEvents()));

  protected readonly fanCountPlot = computed(() => {
    const fans = this.fans();

    return fans === null ? null : fanCountPlot(this.events(), fans.floorFans, fans.softCapFans);
  });
  protected readonly fanCountLabel = computed(() => fanCountSummary(this.events()));

  /** Whether the soft cap is on the chart, so the page can say where it is when it is not. */
  protected readonly capDrawn = computed(() => {
    const fans = this.fans();

    return fans !== null && showsCap(this.events(), fans.softCapFans);
  });

  /** The fans' target for the season in words, or null before it is set. */
  protected readonly targetLabel = computed(() => {
    const rank = this.fans()?.seasonTargetRank ?? null;

    return rank === null ? null : ordinalPlace(rank);
  });

  /** Where the club stood at the last review, or null: the review follows results, so it is held back with them. */
  protected readonly lastGapLabel = computed(() =>
    this.heldBack() ? null : gapLabel(this.fans()?.lastWeekGap ?? null),
  );

  protected readonly moodBands = MOOD_BANDS;
  protected readonly calmFrom = CALM_FROM;
  protected readonly calmTo = CALM_TO;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly nameLinkClass = NAME_LINK;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;

  constructor() {
    this.gate.refresh();
    this.store.load(this.scope());
  }

  /** Switches how much history the charts draw. The page stays on screen while the new history is read. */
  protected selectScope(scope: FanHistoryScope): void {
    if (scope === this.scope()) {
      return;
    }

    this.scope.set(scope);
    this.store.load(scope, false);
  }

  /** Reads the fans again after a failed read. */
  protected retry(): void {
    this.store.load(this.scope());
  }

  /** Whether a match's result is still to be seen. */
  protected hidden(match: FanRecentMatch): boolean {
    return this.gate.isHidden(match.fixture.matchId);
  }

  protected group(value: number): string {
    return groupedNumber(value);
  }

  protected signed(value: number): string {
    return signedNumber(value);
  }

  protected instant(value: string): string {
    return formatInstant(value);
  }

  protected date(value: string): string {
    return eventDate(value);
  }

  protected round(fixture: ClubFixture): string {
    return roundLabel(fixture.roundNumber);
  }

  protected venue(fixture: ClubFixture): string {
    return venueLabel(fixture.venue);
  }

  protected score(fixture: ClubFixture): string {
    return scoreLabel(fixture.homeScore, fixture.awayScore);
  }

  protected outcome(fixture: ClubFixture): string {
    return outcomeLabel(fixture.outcome);
  }

  /** The tint of a result's badge: the word is always shown beside it. */
  protected outcomeClass(fixture: ClubFixture): string {
    switch (fixture.outcome) {
      case 'win':
        return expectationToneClass('good');
      case 'loss':
        return expectationToneClass('bad');
      default:
        return expectationToneClass('even');
    }
  }

  protected chances(match: FanRecentMatch): string {
    return match.expectation === null ? '' : chancesLabel(match.expectation);
  }

  protected predicted(match: FanRecentMatch): string {
    return match.expectation === null ? '' : predictedScore(match.expectation);
  }

  /** The change in satisfaction a match made, in points, or an empty string when none was recorded. */
  protected moodDelta(match: FanRecentMatch): string {
    return match.moodBeforeBp === null || match.moodAfterBp === null
      ? ''
      : moodChange(match.moodBeforeBp, match.moodAfterBp);
  }

  /** The change in fans a match made, or an empty string when none was recorded. */
  protected fansDelta(match: FanRecentMatch): string {
    return match.fansBefore === null || match.fansAfter === null
      ? ''
      : signedNumber(match.fansAfter - match.fansBefore);
  }

  protected kind(event: FanEvent): string {
    return eventKindLabel(event.kind);
  }

  protected note(event: FanEvent): string {
    return eventNote(event);
  }

  protected word(event: FanEvent): string {
    return moodWordFor(event.moodAfterBp).phrase;
  }

  protected points(event: FanEvent): string {
    return moodPoints(event.moodAfterBp).toFixed(1);
  }

  protected expected(centis: number): string {
    return expectedPoints(centis);
  }
}
