import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthApi } from '../../../core/auth/auth-api';
import {
  FORM_CARD,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';
import { controlError, errorId } from '../../../shared/forms/form-support';

/**
 * Starts a password reset.
 *
 * The answer is always the same, whether or not the address has an account. Saying "no such account"
 * here would hand out an account-existence oracle, which is exactly what the server refuses to do.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
})
export class ForgotPassword {
  private readonly authApi = inject(AuthApi);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
  });

  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formCardClass = FORM_CARD;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  /** Resolves the message under the email field. */
  protected errorFor(): string | null {
    return controlError(this.form.get('email'), new Map(), 'Email');
  }

  /** The element id the error message will carry. */
  protected idFor(): string {
    return errorId('email');
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    this.formError.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);

    this.authApi.forgotPassword(this.form.getRawValue().email).subscribe({
      next: () => this.submitted.set(true),
      error: () =>
        this.formError.set('The request could not be sent. Check your connection and try again.'),
      complete: () => this.submitting.set(false),
    });
  }
}
