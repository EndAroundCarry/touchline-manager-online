import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { SessionStore } from '../../core/auth/session-store';
import {
  DESTRUCTIVE_BUTTON,
  FIELD_ERROR,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';
import { controlError, errorId } from '../../shared/forms/form-support';

/** Requires the deletion confirmation to be ticked. */
function mustBeAccepted(control: AbstractControl): ValidationErrors | null {
  return control.value === true ? null : { mustBeTrue: true };
}

/**
 * Account settings.
 *
 * Covers what Stage 2 introduces: the profile, the display name under an optimistic concurrency check,
 * signing out (here or everywhere), and closing the account. Timezone, reduced motion, score hiding,
 * and session listing arrive with the settings work in a later stage.
 */
@Component({
  selector: 'app-settings',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './settings.html',
})
export class Settings {
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);

  /** The signed-in account. */
  protected readonly user = this.store.user;

  /** Whether the address is confirmed. */
  protected readonly isVerified = this.store.isVerified;

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly nameForm = inject(FormBuilder).nonNullable.group({
    displayName: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(32),
        Validators.pattern("^[A-Za-z0-9][A-Za-z0-9 _'-]*$"),
      ],
    ],
  });

  protected readonly nameSubmitting = signal(false);
  protected readonly nameSaved = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly nameConflict = signal(false);

  protected readonly deleteForm = inject(FormBuilder).nonNullable.group({
    password: ['', [Validators.required, Validators.maxLength(128)]],
    confirm: [false, [mustBeAccepted]],
  });

  protected readonly deleteSubmitting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  protected readonly signingOut = signal(false);

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly destructiveButtonClass = DESTRUCTIVE_BUTTON;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly fieldErrorClass = FIELD_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  /** The element id the display-name error will carry. */
  protected readonly displayNameErrorId = errorId('displayName');

  /** The element id the delete-password error will carry. */
  protected readonly deletePasswordErrorId = errorId('deletePassword');

  constructor() {
    this.reload();
  }

  /** Reloads the profile and the entity tag the next write must carry. */
  protected reload(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.store.loadProfile().subscribe({
      next: (profile) => {
        this.nameForm.patchValue({ displayName: profile.displayName });
        this.applyVerificationToNameForm(profile.emailVerified);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your profile could not be loaded.',
        );
      },
    });
  }

  /**
   * Enables the manager name only for a confirmed address.
   *
   * The control's own disabled state is used rather than a `disabled` binding on the input. With a
   * reactive form the control owns that property — `FormControlName` re-applies it on every change —
   * so an attribute binding is silently ignored and the field stays editable while the screen claims
   * otherwise.
   */
  private applyVerificationToNameForm(verified: boolean): void {
    const control = this.nameForm.controls.displayName;

    if (verified) {
      control.enable({ emitEvent: false });
    } else {
      control.disable({ emitEvent: false });
    }
  }

  /** Resolves the message under the display-name field. */
  protected displayNameError(): string | null {
    return controlError(this.nameForm.get('displayName'), new Map(), 'DisplayName');
  }

  /** Saves the display name. */
  protected saveDisplayName(): void {
    if (this.nameSubmitting()) {
      return;
    }

    this.nameSaved.set(false);
    this.nameError.set(null);
    this.nameConflict.set(false);

    if (this.nameForm.invalid) {
      this.nameForm.markAllAsTouched();

      return;
    }

    this.nameSubmitting.set(true);

    this.store.updateDisplayName(this.nameForm.getRawValue().displayName).subscribe({
      next: () => this.nameSaved.set(true),
      error: (error: unknown) => {
        this.nameSubmitting.set(false);

        if (error instanceof ApiError) {
          if (error.isPreconditionFailed) {
            // Somebody changed the profile underneath this screen. Show the new state and let the
            // manager decide, rather than overwriting the other change.
            this.nameConflict.set(true);
            this.reload();

            return;
          }

          this.nameError.set(error.detail);

          return;
        }

        this.nameError.set('The change could not be saved. Try again.');
      },
      complete: () => this.nameSubmitting.set(false),
    });
  }

  /** Signs out of this device. */
  protected signOut(): void {
    this.signingOut.set(true);
    this.store.logout().subscribe(() => {
      void this.router.navigateByUrl('/login');
    });
  }

  /** Signs out of every device. */
  protected signOutEverywhere(): void {
    this.signingOut.set(true);
    this.store.logoutAll().subscribe(() => {
      void this.router.navigateByUrl('/login');
    });
  }

  /** Closes the account. */
  protected deleteAccount(): void {
    if (this.deleteSubmitting()) {
      return;
    }

    this.deleteError.set(null);

    if (this.deleteForm.invalid) {
      this.deleteForm.markAllAsTouched();

      return;
    }

    this.deleteSubmitting.set(true);

    this.store.deleteAccount(this.deleteForm.getRawValue().password).subscribe({
      next: () => {
        void this.router.navigateByUrl('/welcome');
      },
      error: (error: unknown) => {
        this.deleteSubmitting.set(false);
        this.deleteError.set(
          error instanceof ApiError ? error.detail : 'The account could not be closed. Try again.',
        );
      },
    });
  }

  /** The element id a field's error message will carry. */
  protected idFor(controlName: string): string {
    return errorId(controlName);
  }
}
