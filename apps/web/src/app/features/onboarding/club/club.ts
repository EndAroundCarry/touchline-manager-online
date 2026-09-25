import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import { AvailableClub, ProvisioningStatus } from '../../../core/world/world.models';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../../shared/forms/control-styles';

/**
 * The club step of onboarding (master plan §10.2, `WORLD-8`).
 *
 * Only the lowest active tier is offered, and taken clubs are shown as taken rather than removed: a
 * manager who can see that eleven of eighteen clubs are gone understands why the remaining ones matter.
 *
 * The claim carries an idempotency key held by the store, so a double click or a retry after a dropped
 * connection cannot produce two tenures.
 */
@Component({
  selector: 'app-club-choice',
  imports: [RouterLink],
  templateUrl: './club.html',
})
export class ClubChoice {
  private readonly store = inject(OnboardingStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly listing = this.store.availableClubs;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly claimingClubId = signal<string | null>(null);
  protected readonly claimError = signal<string | null>(null);
  protected readonly provisioning = signal<ProvisioningStatus | null>(null);
  protected readonly cooldownUntil = signal<string | null>(null);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  constructor() {
    const countryId = this.route.snapshot.queryParamMap.get('countryId');

    if (countryId === null) {
      this.loading.set(false);
      this.loadError.set('Choose a country first.');

      return;
    }

    this.store.loadAvailableClubs(countryId).subscribe({
      next: () => this.loading.set(false),
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'The clubs could not be loaded.',
        );
      },
    });
  }

  /** Claims a club, then opens the dashboard it arrived with. */
  protected claim(club: AvailableClub): void {
    if (this.claimingClubId() !== null) {
      return;
    }

    this.claimError.set(null);
    this.provisioning.set(null);
    this.cooldownUntil.set(null);
    this.claimingClubId.set(club.id);

    this.store.claimClub(club.id).subscribe({
      next: () => {
        void this.router.navigate(['/dashboard']);
      },
      error: (error: unknown) => {
        this.claimingClubId.set(null);

        if (!(error instanceof ApiError)) {
          this.claimError.set('The club could not be taken over. Try again.');

          return;
        }

        // The server names the reason; the screen only decides what else to show with it.
        this.claimError.set(error.detail);
        this.provisioning.set(error.extension<ProvisioningStatus>('provisioning'));
        this.cooldownUntil.set(error.extension<string>('cooldownUntil'));
      },
    });
  }

  /** Re-reads the club list, after a claim that failed because somebody else took the club. */
  protected refresh(): void {
    const countryId = this.route.snapshot.queryParamMap.get('countryId');

    if (countryId === null) {
      return;
    }

    this.claimError.set(null);
    this.provisioning.set(null);
    this.cooldownUntil.set(null);
    this.loading.set(true);

    this.store.loadAvailableClubs(countryId).subscribe({
      next: () => this.loading.set(false),
      error: () => this.loading.set(false),
    });
  }
}
