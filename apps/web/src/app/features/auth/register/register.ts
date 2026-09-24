import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { AuthApi } from '../../../core/auth/auth-api';
import { RegistrationAccepted } from '../../../core/auth/auth.models';
import {
  CHECKBOX_INPUT,
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

/** Requires the terms checkbox to be ticked, so consent is an explicit act. */
function mustBeAccepted(control: AbstractControl): ValidationErrors | null {
  return control.value === true ? null : { mustBeTrue: true };
}

/**
 * Account creation.
 *
 * On success the form is replaced by a confirmation panel rather than a redirect, because the only
 * useful next action is reading an email — and the panel can offer to send it again.
 */
@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
})
export class Register {
  private readonly authApi = inject(AuthApi);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    displayName: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(32),
        Validators.pattern("^[A-Za-z0-9][A-Za-z0-9 _'-]*$"),
      ],
    ],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
    acceptTerms: [false, [mustBeAccepted]],
  });

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverErrors = signal<ReadonlyMap<string, string[]>>(new Map());
  protected readonly registration = signal<RegistrationAccepted | null>(null);
  protected readonly resent = signal(false);

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly checkboxClass = CHECKBOX_INPUT;
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

  /** The element id a field's error message will carry. */
  protected idFor(controlName: string): string {
    return errorId(controlName);
  }

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

    this.authApi.register(this.form.getRawValue()).subscribe({
      next: (accepted) => this.registration.set(accepted),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.report(error);
      },
      complete: () => this.submitting.set(false),
    });
  }

  /** Sends the verification link again. */
  protected resend(): void {
    const accepted = this.registration();

    if (accepted === null) {
      return;
    }

    this.resent.set(false);

    this.authApi.resendVerification(accepted.email).subscribe({
      next: () => this.resent.set(true),
      error: () => this.formError.set('The link could not be sent. Try again shortly.'),
    });
  }

  private report(error: unknown): void {
    if (error instanceof ApiError) {
      if (error.fieldErrors.size > 0) {
        this.serverErrors.set(error.fieldErrors);

        return;
      }

      this.formError.set(error.detail);

      return;
    }

    this.formError.set('Registration failed. Check your connection and try again.');
  }
}
