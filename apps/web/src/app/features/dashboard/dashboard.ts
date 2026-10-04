import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ClubFixture, DivisionTableRow } from '../../core/competition/competition.models';
import {
  lockCountdown,
  roundLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { FinanceStore } from '../../core/finance/finance-store';
import { InboxStore } from '../../core/inbox/inbox-store';
import { SquadStore } from '../../core/squad/squad-store';
import { TransfersStore } from '../../core/transfers/transfers-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import {
  formatDeadline,
  formatFunds,
  formatInstant,
  preferredLocale,
  preferredTimeZone,
} from '../../core/world/presentation';
import { ClubDashboard } from '../../core/world/world.models';
import {
  DESTRUCTIVE_BUTTON,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';
import {
  CalendarEventKind,
  calendarEvents,
  squadStatus,
  statLeaders,
  tableWindow,
} from './dashboard-presentation';

/**
 * The club dashboard (master plan §11.1).
 *
 * With a club it is a grid of widgets, each answering one question a manager asks on arriving: the next match and the
 * calendar around it, the league table, the club's record, the inbox, the squad's status, the market, and the
 * division's leaders. Each widget reads its own store and fails on its own, so one slow or failed read leaves the rest
 * of the screen intact.
 *
 * It is also the router for onboarding: with no manager profile it offers the profile step, with a profile
 * but no club it offers the country step, and with a club it shows what the manager inherited. Deciding
 * that here rather than in a chain of guards keeps one screen responsible for "what should this person do
 * next", which is a question only the current state can answer.
 *
 * What the dashboard shows is what exists at this stage of the build. Squad, fixtures, results, and inbox
 * arrive with the stages that create them, rather than appearing as permanently-empty cards.
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly store = inject(OnboardingStore);
  private readonly competition = inject(CompetitionStore);
  private readonly finance = inject(FinanceStore);
  private readonly squadStore = inject(SquadStore);
  private readonly transfers = inject(TransfersStore);
  private readonly inbox = inject(InboxStore);

  protected readonly state = this.store.state;
  protected readonly club = signal<ClubDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly resigning = signal(false);
  protected readonly resignError = signal<string | null>(null);

  /** Whether the first-steps guidance has been dismissed. Session-scoped: nothing is persisted. */
  protected readonly firstStepsDismissed = signal(false);

  /** The club heading, focused when the guidance is dismissed so focus is not dropped to the body. */
  private readonly clubHeading = viewChild<ElementRef<HTMLHeadingElement>>('clubHeading');

  /** The club's fixture list, read once a club is held so the next fixture can be shown. */
  protected readonly fixtures = this.competition.fixtures;

  /** The risks the club is running: expiring contracts, payroll risk, a squad below the minimum (`FIN-16`). */
  protected readonly warnings = computed(() => this.finance.summary()?.warnings ?? []);

  /** The next fixture still to be played, or null once the season is done (§11.1). */
  protected readonly nextFixture = computed<ClubFixture | null>(() => {
    const list = this.fixtures();

    if (list === null || list.nextFixtureId === null) {
      return null;
    }

    return list.fixtures.find((fixture) => fixture.id === list.nextFixtureId) ?? null;
  });

  /** The finance summary, for the club widget's funds and the warnings. */
  protected readonly financeSummary = this.finance.summary;

  /** The rows of the division table around the manager's club, for the league-table widget. */
  protected readonly tableRows = computed(() =>
    tableWindow(this.competition.divisionTable()?.rows ?? [], this.club()?.club.id ?? null, 12),
  );

  /** Whether the division table has been read, so the widget can tell "still loading" from "empty". */
  protected readonly hasTable = computed(() => this.competition.divisionTable() !== null);

  /** The manager's own row of the table: their record, position, goals and cards. */
  protected readonly ownRow = computed<DivisionTableRow | null>(() => {
    const clubId = this.club()?.club.id;

    return this.competition.divisionTable()?.rows.find((row) => row.clubId === clubId) ?? null;
  });

  /** Why the squad could not be read, or null. */
  protected readonly squadError = signal<string | null>(null);

  /** The squad's concerns, grouped, for the squad-status widget. */
  protected readonly squadGroups = computed(() =>
    squadStatus(this.squadStore.squad()?.players ?? []),
  );

  /** The squad itself, once read. */
  protected readonly squadRead = this.squadStore.squad;

  /** The latest inbox messages, for the inbox widget. */
  protected readonly latestMessages = computed(() => this.inbox.messages().slice(0, 4));
  protected readonly unreadCount = this.inbox.unreadCount;
  protected readonly inboxLoading = this.inbox.loading;
  protected readonly inboxError = this.inbox.error;

  /** The club's own market activity, for the pending-transfers widget. */
  protected readonly myBids = this.transfers.myBids;
  protected readonly myListings = this.transfers.myListings;
  protected readonly marketLoading = this.transfers.loading;
  protected readonly marketError = this.transfers.error;

  /** The division's leaders, for the player-stats widget. */
  protected readonly leaders = computed(() =>
    statLeaders(this.competition.divisionStatistics()?.rows ?? []),
  );
  protected readonly hasStatistics = computed(() => this.competition.divisionStatistics() !== null);

  /** What is next on the calendar: matches, the team-sheet deadline, and auctions closing. */
  protected readonly calendar = computed(() =>
    calendarEvents(this.fixtures(), this.myBids(), this.myListings(), new Date()),
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly destructiveButtonClass = DESTRUCTIVE_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  constructor() {
    this.load();
  }

  /** Reads the onboarding state, then the club dashboard when there is a club. */
  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.store.loadState().subscribe({
      next: (state) => {
        if (state.tenure === null) {
          this.club.set(null);
          this.loading.set(false);

          return;
        }

        this.store.dashboard(state.tenure.clubId).subscribe({
          next: (dashboard) => {
            this.club.set(dashboard);
            this.loading.set(false);

            // The next fixture and the finance warnings are separate reads because they are different
            // resources; a failure in either leaves the rest of the dashboard intact rather than blanking
            // the screen.
            this.competition.loadFixtures();
            this.finance.loadSummary();

            // The widgets' own reads. They are independent of one another, and of the club that has just loaded,
            // so none of them waits for another.
            this.competition.loadMyDivisionTable();
            this.competition.loadDivisionStatistics(dashboard.division.id);
            this.inbox.load();
            this.transfers.load();
            this.squadError.set(null);
            this.squadStore.loadSquad(dashboard.club.id).subscribe({
              error: (error: unknown) =>
                this.squadError.set(
                  error instanceof ApiError ? error.detail : 'Your squad could not be read.',
                ),
            });
          },
          error: (error: unknown) => {
            this.loading.set(false);
            this.loadError.set(
              error instanceof ApiError ? error.detail : 'Your club could not be loaded.',
            );
          },
        });
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your manager profile could not be loaded.',
        );
      },
    });
  }

  /** The initials of a club, for the monogram badge: its short name, or the first letters of its name. */
  protected badge(shortName: string): string {
    return shortName.slice(0, 3).toUpperCase();
  }

  /** The icon for a calendar entry, so the kind is not told by colour alone. */
  protected calendarIcon(kind: CalendarEventKind): string {
    return kind === 'match' ? 'pi pi-flag' : kind === 'deadline' ? 'pi pi-clock' : 'pi pi-tag';
  }

  /** Formats a calendar entry's moment compactly: weekday, day, month and time. */
  protected when(value: string): string {
    return formatCalendarMoment(value);
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** Formats a team-sheet deadline in the viewer's local time, naming the zone (`VOI-4`, `CAL-4`). */
  protected deadline(value: string): string {
    return formatDeadline(value);
  }

  /** Names the manager's side of a fixture: home or away. */
  protected venue(value: string): string {
    return venueLabel(value);
  }

  /** A round's label. */
  protected round(roundNumber: number): string {
    return roundLabel(roundNumber);
  }

  /** How long until a fixture's team sheets lock (`CAL-3`). */
  protected countdown(instant: string): string {
    return lockCountdown(instant, new Date());
  }

  /**
   * Hides the first-steps guidance.
   *
   * The card holds the control that was just activated, so removing it would drop focus onto the document
   * body. Focus is moved to the club heading instead, which leaves the reading position where it was.
   */
  protected dismissFirstSteps(): void {
    this.firstStepsDismissed.set(true);
    this.clubHeading()?.nativeElement.focus();
  }

  /** Resigns from the club and returns the manager to the country step. */
  protected resign(): void {
    if (this.resigning()) {
      return;
    }

    this.resignError.set(null);
    this.resigning.set(true);

    this.store.resign().subscribe({
      next: () => {
        this.resigning.set(false);
        this.club.set(null);
      },
      error: (error: unknown) => {
        this.resigning.set(false);
        this.resignError.set(
          error instanceof ApiError ? error.detail : 'You could not resign. Try again.',
        );
      },
    });
  }
}

/** Weekday, day, month and time in the manager's locale and zone, which is how a calendar reads. */
function formatCalendarMoment(value: string): string {
  return new Intl.DateTimeFormat(preferredLocale(), {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: preferredTimeZone(),
  }).format(new Date(value));
}
