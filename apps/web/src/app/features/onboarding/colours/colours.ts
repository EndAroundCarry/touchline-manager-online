import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { MaintenanceStore } from '../../../core/maintenance/maintenance-store';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
} from '../../../shared/forms/control-styles';
import { KitColourPicker } from '../../../shared/ui/kit-colour-picker/kit-colour-picker';

/**
 * The colours step of onboarding, shown once a club has been taken over.
 *
 * The club arrives wearing the colours it was generated with, so they are what the picker starts on and a
 * manager who likes them confirms in one click. Choosing is never a gate: leaving without saving keeps the
 * generated colours, and the same picker is in Settings for as long as the club is theirs.
 */
@Component({
  selector: 'app-club-colours-choice',
  imports: [KitColourPicker, RouterLink],
  templateUrl: './colours.html',
})
export class ClubColoursChoice {
  private readonly store = inject(OnboardingStore);
  private readonly router = inject(Router);
  private readonly maintenance = inject(MaintenanceStore);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  /** The club being dressed, or null when the manager holds none. */
  protected readonly clubName = signal<string | null>(null);

  protected readonly primary = signal('#1f4e79');
  protected readonly secondary = signal('#d6e4f0');

  /** Whether a write is allowed; offline or read-only the colours cannot be saved. */
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    this.store.loadState().subscribe({
      next: (state) => {
        this.loading.set(false);

        if (state.tenure === null) {
          return;
        }

        this.clubName.set(state.tenure.clubName);
        this.primary.set(state.tenure.primaryColour);
        this.secondary.set(state.tenure.secondaryColour);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your club could not be loaded.',
        );
      },
    });
  }

  /** Saves the pair, then opens the club's dashboard. */
  protected confirm(picker: KitColourPicker): void {
    if (this.saving() || !picker.valid()) {
      return;
    }

    this.saveError.set(null);
    this.saving.set(true);

    this.store.changeClubColours(this.primary(), this.secondary()).subscribe({
      next: () => void this.router.navigateByUrl('/dashboard'),
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(
          error instanceof ApiError
            ? error.detail
            : 'The colours could not be saved. Try again, or choose them later in Settings.',
        );
      },
    });
  }
}
