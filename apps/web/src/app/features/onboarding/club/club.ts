import { NgTemplateOutlet } from '@angular/common';
import { Component, OnDestroy, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { MaintenanceStore } from '../../../core/maintenance/maintenance-store';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import {
  AvailableClub,
  ProvisioningStatus,
  TakeoverOption,
  TakeoverPreview,
  TakeoverStart,
} from '../../../core/world/world.models';
import { Money } from '../../../shared/ui/money/money';
import {
  FORM_ERROR,
  LINK_ACTION,
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
 * Taking a club over is a choice between two starts (`TKO-1`): keep it exactly as it is, with its squad, debts and
 * ground, or a clean start, which releases the old players to the free agents and begins with the tier's default squad,
 * ground and cash, and no debt. Both are shown side by side before the claim, as the server would leave the club.
 *
 * The claim carries an idempotency key held by the store, so a double click or a retry after a dropped
 * connection cannot produce two tenures.
 */
@Component({
  selector: 'app-club-choice',
  imports: [NgTemplateOutlet, RouterLink, Money],
  templateUrl: './club.html',
})
export class ClubChoice implements OnDestroy {
  private readonly store = inject(OnboardingStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly maintenance = inject(MaintenanceStore);

  /** The country whose tier the manager is choosing from. */
  private readonly countryId = this.route.snapshot.queryParamMap.get('countryId');

  protected readonly listing = this.store.availableClubs;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly claimingClubId = signal<string | null>(null);
  protected readonly claimError = signal<string | null>(null);
  protected readonly provisioning = signal<ProvisioningStatus | null>(null);
  protected readonly cooldownUntil = signal<string | null>(null);

  /** The club whose start is being chosen, or null while the manager is still looking at the list. */
  protected readonly choosing = signal<AvailableClub | null>(null);
  protected readonly preview = signal<TakeoverPreview | null>(null);
  protected readonly previewLoading = signal(false);
  protected readonly previewError = signal<string | null>(null);
  protected readonly claimingStart = signal<TakeoverStart | null>(null);

  /** Whether a write is allowed; offline or read-only a club cannot be taken over. */
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    const countryId = this.countryId;

    if (countryId === null) {
      this.loading.set(false);
      this.loadError.set('Choose a country first.');

      return;
    }

    // When the country's next tier finishes generating, the clubs that just became claimable are read
    // again, so the screen moves on by itself rather than waiting for another click (`PYR-10`).
    effect(() => {
      if (this.store.provisioningSettled() > 0) {
        this.refresh();
      }
    });

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

  /** Stops any provisioning poll when the screen is left. */
  ngOnDestroy(): void {
    this.store.stopProvisioningPoll();
  }

  /** Opens the choice of start for a club: reads both ways of taking it over and shows them side by side. */
  protected choose(club: AvailableClub): void {
    if (this.claimingClubId() !== null) {
      return;
    }

    this.claimError.set(null);
    this.provisioning.set(null);
    this.cooldownUntil.set(null);
    this.choosing.set(club);
    this.preview.set(null);
    this.previewError.set(null);
    this.previewLoading.set(true);

    this.store.loadTakeoverPreview(club.id).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.previewLoading.set(false);
      },
      error: (error: unknown) => {
        this.previewLoading.set(false);
        this.previewError.set(
          error instanceof ApiError
            ? error.detail
            : 'The two ways of taking the club could not be read.',
        );
      },
    });
  }

  /** Goes back from the choice of start to the list of clubs. */
  protected back(): void {
    if (this.claimingClubId() !== null) {
      return;
    }

    this.choosing.set(null);
    this.preview.set(null);
    this.previewError.set(null);
    this.claimError.set(null);
  }

  /** Claims the club in the chosen way, then asks for the colours it will play in before opening the dashboard. */
  protected claim(club: AvailableClub, start: TakeoverStart): void {
    if (this.claimingClubId() !== null) {
      return;
    }

    this.claimError.set(null);
    this.provisioning.set(null);
    this.cooldownUntil.set(null);
    this.claimingClubId.set(club.id);
    this.claimingStart.set(start);

    this.store.claimClub(club.id, start).subscribe({
      next: () => {
        void this.router.navigate(['/onboarding/colours']);
      },
      error: (error: unknown) => {
        this.claimingClubId.set(null);
        this.claimingStart.set(null);

        if (!(error instanceof ApiError)) {
          this.claimError.set('The club could not be taken over. Try again.');

          return;
        }

        // The server names the reason; the screen only decides what else to show with it.
        this.claimError.set(error.detail);

        const pending = error.extension<ProvisioningStatus>('provisioning');

        this.provisioning.set(pending);
        this.cooldownUntil.set(error.extension<string>('cooldownUntil'));

        // The country is full and a tier is coming, so poll until it arrives rather than making the
        // manager guess when to look again (`PYR-10`).
        if (pending !== null && this.countryId !== null) {
          this.store.startProvisioningPoll(this.countryId);
        }
      },
    });
  }

  /** Re-reads the club list, after a claim that failed because somebody else took the club. */
  protected refresh(): void {
    const countryId = this.countryId;

    if (countryId === null) {
      return;
    }

    this.claimError.set(null);
    this.provisioning.set(null);
    this.cooldownUntil.set(null);
    this.choosing.set(null);
    this.preview.set(null);
    this.loading.set(true);

    this.store.loadAvailableClubs(countryId).subscribe({
      next: () => this.loading.set(false),
      error: () => this.loading.set(false),
    });
  }

  /** The figures of one start, in the order they are compared. */
  protected figures(option: TakeoverOption): readonly { label: string; value: string }[] {
    return [
      { label: 'Players', value: String(option.players) },
      { label: 'Squad strength', value: option.squadStrength.toFixed(1) },
      {
        label: 'Ground',
        value: `${option.groundPlaces.toLocaleString()} places (level ${option.groundLevel})`,
      },
      {
        label: 'Snack bar and shop',
        value: `levels ${option.snackBarLevel} and ${option.shopLevel}`,
      },
    ];
  }

  /** Names a financial state. */
  protected stateWord(state: string): string {
    switch (state) {
      case 'healthy':
        return 'Healthy';
      case 'in_the_red':
        return 'In the red';
      case 'administration':
        return 'In administration';
      default:
        return state;
    }
  }

  /** Whether the start leaves the club with a debt. */
  protected inDebt(option: TakeoverOption): boolean {
    return option.debtMinor > 0;
  }
}
