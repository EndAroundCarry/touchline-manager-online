import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { AuthApi } from '../../../core/auth/auth-api';
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

/** Requires the confirmation field to match the new password. */
function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const password = control.get('newPassword')?.value as string | undefined;
  const confirmation = control.get('confirmPassword')?.value as string | undefined;

  return password === confirmation ? null : { mismatch: true };
}

/**
 * Completes a password reset.
 *
 * The token is read from the link and never shown. A successful reset signs out every existing session
 * server-side, so the screen says so rather than leaving the manager to discover it on another device.
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly authApi = inject(AuthApi);
  private readonly userId = inject(ActivatedRoute).snapshot.queryParamMap.get('userId');
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');

  /** Whether the link carried both the account and the token. */
  protected readonly hasLink = this.userId !== null && this.token !== null;

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatch] },
  );

  protected readonly submitting = signal(false);
  protected readonly completed = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverErrors = signal<ReadonlyMap<string, string[]>>(new Map());

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formCardClass = FORM_CARD;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly fieldErrorClass = FIELD_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  /** Resolves the message under a field. */
  protected errorFor(controlName: string, serverField: string): string | null {
    return controlError(this.form.get(controlName), this.serverErrors(), serverField);
  }

  /** Whether the confirmation field disagrees with the new password. */
  protected get mismatch(): boolean {
    return this.form.hasError('mismatch') && this.form.controls.confirmPassword.touched;
  }

  /** The element id the mismatch message will carry. */
  protected readonly mismatchId = errorId('confirmPassword');

  protected submit(): void {
    if (this.submitting() || this.userId === null || this.token === null) {
      return;
    }

    this.formError.set(null);
    this.serverErrors.set(new Map());

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);

    this.authApi
      .resetPassword({
        userId: this.userId,
        token: this.token,
        newPassword: this.form.getRawValue().newPassword,
      })
      .subscribe({
        next: () => this.completed.set(true),
        error: (error: unknown) => {
          this.submitting.set(false);

          if (error instanceof ApiError) {
            if (error.fieldErrors.size > 0) {
              this.serverErrors.set(error.fieldErrors);

              return;
            }

            this.formError.set(error.detail);

            return;
          }

          this.formError.set('The password could not be changed. Try again.');
        },
        complete: () => this.submitting.set(false),
      });
  }
}
