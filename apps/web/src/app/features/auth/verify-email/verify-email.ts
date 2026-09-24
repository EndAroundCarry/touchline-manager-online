import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { errorId } from '../../../shared/forms/form-support';

/** Where the confirmation stands. */
type ConfirmationState = 'idle' | 'confirming' | 'confirmed' | 'failed';

/**
 * Confirms an email address.
 *
 * The confirmation is a button press, not an automatic request on load. Corporate mail scanners and
 * link previews fetch URLs before the recipient opens them, and this token is single-use: confirming
 * on a GET would let a scanner burn the link and leave the manager permanently unconfirmed.
 */
@Component({
  selector: 'app-verify-email',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './verify-email.html',
})
export class VerifyEmail {
  private readonly authApi = inject(AuthApi);
  private readonly userId = inject(ActivatedRoute).snapshot.queryParamMap.get('userId');
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');

  /** Whether the link carried both the account and the token. */
  protected readonly hasLink = this.userId !== null && this.token !== null;

  protected readonly state = signal<ConfirmationState>('idle');
  protected readonly message = signal<string | null>(null);
  protected readonly resent = signal(false);
  protected readonly resendSubmitting = signal(false);
  protected readonly resendError = signal<string | null>(null);

  protected readonly resendForm = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
  });

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formCardClass = FORM_CARD;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly fieldErrorClass = FIELD_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;

  /** The element id the resend field's error message will carry. */
  protected readonly resendErrorId = errorId('resendEmail');

  protected confirm(): void {
    if (this.state() !== 'idle' || this.userId === null || this.token === null) {
      return;
    }

    this.state.set('confirming');

    this.authApi.verifyEmail(this.userId, this.token).subscribe({
      next: (accepted) => {
        this.message.set(accepted.message);
        this.state.set('confirmed');
      },
      error: (error: unknown) => {
        this.message.set(
          error instanceof ApiError
            ? error.detail
            : 'The confirmation could not be completed. Try again.',
        );
        this.state.set('failed');
      },
    });
  }

  protected resend(): void {
    if (this.resendSubmitting()) {
      return;
    }

    this.resendError.set(null);
    this.resent.set(false);

    if (this.resendForm.invalid) {
      this.resendForm.markAllAsTouched();

      return;
    }

    this.resendSubmitting.set(true);

    this.authApi.resendVerification(this.resendForm.getRawValue().email).subscribe({
      next: () => this.resent.set(true),
      error: () => this.resendError.set('The link could not be sent. Try again shortly.'),
      complete: () => this.resendSubmitting.set(false),
    });
  }
}
