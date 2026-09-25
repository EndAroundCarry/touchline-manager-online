import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { ClubFixture } from '../../core/competition/competition.models';
import {
  lockCountdown,
  roundLabel,
  venueLabel,
} from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
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

/**
 * The club dashboard (master plan §11.1).
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

  protected readonly state = this.store.state;
  protected readonly club = signal<ClubDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly resigning = signal(false);
  protected readonly resignError = signal<string | null>(null);

  /** The club's fixture list, read once a club is held so the next fixture can be shown. */
  protected readonly fixtures = this.competition.fixtures;

  /** The next fixture still to be played, or null once the season is done (§11.1). */
  protected readonly nextFixture = computed<ClubFixture | null>(() => {
    const list = this.fixtures();

    if (list === null || list.nextFixtureId === null) {
      return null;
    }

    return list.fixtures.find((fixture) => fixture.id === list.nextFixtureId) ?? null;
  });

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

            // The next fixture is a second read because it is a different resource; a failure here leaves
            // the rest of the dashboard intact rather than blanking the screen.
            this.competition.loadFixtures();
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

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
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
