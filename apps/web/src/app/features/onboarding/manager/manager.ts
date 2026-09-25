import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import { preferredLocale, preferredTimeZone } from '../../../core/world/presentation';
import { ManagerProfile as ManagerProfileModel } from '../../../core/world/world.models';
import {
  FIELD_ERROR,
  FORM_CARD,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';
import { controlError, errorId } from '../../../shared/forms/form-support';

/** The locale shape the server accepts: a language and a region. */
const LOCALE_PATTERN = /^[a-z]{2,3}-[A-Z]{2}$/;

/**
 * The manager profile step of onboarding (master plan §10.2).
 *
 * Two preferences are asked for and no name: the name a league table shows is the account's display name,
 * and asking for a second one here would raise the question of which one is real. Both defaults come from
 * the browser so the common case is one click.
 */
@Component({
  selector: 'app-manager-profile',
  imports: [ReactiveFormsModule],
  templateUrl: './manager.html',
})
export class ManagerProfile {
  private readonly store = inject(OnboardingStore);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverErrors = signal<ReadonlyMap<string, string[]>>(new Map());

  /** The profile the account already has, when this screen is a confirmation rather than a form. */
  protected readonly profile = signal<ManagerProfileModel | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    locale: [preferredLocale(), [Validators.required, Validators.pattern(LOCALE_PATTERN)]],
    timeZone: [preferredTimeZone(), [Validators.required, Validators.maxLength(64)]],
  });

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formCardClass = FORM_CARD;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly fieldErrorClass = FIELD_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  constructor() {
    this.store.loadState().subscribe({
      next: (state) => {
        this.profile.set(state.manager);

        if (state.manager !== null) {
          this.form.patchValue({ locale: state.manager.locale, timeZone: state.manager.timeZone });
        }

        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your manager profile could not be loaded.',
        );
      },
    });
  }

  /** Resolves the message under a field. */
  protected errorFor(controlName: string, serverField: string): string | null {
    return controlError(this.form.get(controlName), this.serverErrors(), serverField);
  }

  /** The element id a field's error message will carry. */
  protected idFor(controlName: string): string {
    return errorId(controlName);
  }

  /** Creates the profile, then moves on to choosing a country. */
  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    this.formError.set(null);
    this.serverErrors.set(new Map());

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);

    const { locale, timeZone } = this.form.getRawValue();

    this.store.createManagerProfile(locale, timeZone).subscribe({
      next: () => this.continueToCountry(),
      error: (error: unknown) => {
        this.submitting.set(false);

        if (error instanceof ApiError && error.fieldErrors.size > 0) {
          this.serverErrors.set(error.fieldErrors);

          return;
        }

        this.formError.set(
          error instanceof ApiError
            ? error.detail
            : 'Your profile could not be created. Try again.',
        );
      },
    });
  }

  /** Moves to the country step, for a profile that already existed. */
  protected continueToCountry(): void {
    void this.router.navigateByUrl('/onboarding/country');
  }
}
