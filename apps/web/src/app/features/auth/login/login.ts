import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { SessionStore } from '../../../core/auth/session-store';
import {
  FORM_CARD,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';
import { controlError, errorId } from '../../../shared/forms/form-support';

/**
 * Sign-in.
 *
 * The failure message never distinguishes "no such account" from "wrong password", because the server
 * deliberately does not either — showing the difference here would give the enumeration defence away.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
})
export class Login {
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverErrors = signal<ReadonlyMap<string, string[]>>(new Map());

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly formCardClass = FORM_CARD;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
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

    this.store.login(this.form.getRawValue()).subscribe({
      next: () => {
        void this.router.navigateByUrl(this.returnUrl());
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.report(error);
      },
      complete: () => this.submitting.set(false),
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

    this.formError.set('Sign-in failed. Check your connection and try again.');
  }

  /**
   * Where to go after signing in.
   *
   * Only a same-origin absolute path is honoured. A `returnUrl` is attacker-influenced input, and
   * navigating to an arbitrary value would turn the sign-in page into an open redirect.
   */
  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');

    if (requested !== null && requested.startsWith('/') && !requested.startsWith('//')) {
      return requested;
    }

    return '/settings';
  }
}
