import { Component, OnDestroy, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  STATUS_MESSAGE,
} from '../../../shared/forms/control-styles';

/**
 * The country step of onboarding (master plan §10.2, `WORLD-8`).
 *
 * The screen shows every country with how many of its clubs are free, including the ones with none. A
 * manager choosing where to play needs to see that a country is nearly full — hiding the crowded ones
 * would make a league with two places left look the same as an empty one.
 */
@Component({
  selector: 'app-country-choice',
  templateUrl: './country.html',
})
export class CountryChoice implements OnDestroy {
  private readonly store = inject(OnboardingStore);
  private readonly router = inject(Router);

  protected readonly countries = this.store.countries;
  protected readonly capacities = this.store.capacities;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    // A country's generation finishing is a change to a country on this screen, so the capacities are
    // read again and a waiting country's state clears without a click (`PYR-10`).
    effect(() => {
      if (this.store.provisioningSettled() > 0) {
        this.reloadCapacities();
      }
    });

    this.reloadCapacities();
  }

  /** Stops any provisioning poll when the screen is left. */
  ngOnDestroy(): void {
    this.store.stopProvisioningPoll();
  }

  /** Reads every country's capacity, then polls the one whose next tier is being generated. */
  private reloadCapacities(): void {
    this.loading.set(true);

    this.store.loadCapacities().subscribe({
      next: () => {
        this.loading.set(false);
        this.pollGeneratingCountry();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'The countries could not be loaded.',
        );
      },
    });
  }

  /** Begins polling the country the manager is waiting on, if there is one. */
  private pollGeneratingCountry(): void {
    const generating = this.capacities().find((capacity) => capacity.provisioning !== null);

    if (generating !== undefined) {
      this.store.startProvisioningPoll(generating.countryId);
    }
  }

  /** The display name of the country a capacity row belongs to. */
  protected countryName(countryId: string): string | null {
    return this.countries().find((country) => country.id === countryId)?.displayName ?? null;
  }

  /** Opens the club list of a country that has room. */
  protected choose(countryId: string): void {
    void this.router.navigate(['/onboarding/club'], { queryParams: { countryId } });
  }
}
