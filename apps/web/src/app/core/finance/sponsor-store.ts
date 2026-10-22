import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../api/api-error';
import { FinanceApi } from './finance-api';
import { SignSponsorPayload, SponsorPage } from './sponsor.models';

/**
 * The sponsor page's view state (plan toasty-scott section 3; `SPN-1`…`SPN-6`).
 *
 * A feature-scoped store following `StructuresStore`. Nothing is optimistic: the offers, the bonuses and the deal
 * are the server's, and a signing is made against the version the server last returned. A stale one is not a
 * failure to hide: the signing is refused, the page is reloaded, and the manager is told nothing was signed so
 * they decide again against what is really there (`§11.2`).
 */
@Injectable({ providedIn: 'root' })
export class SponsorStore {
  private readonly api = inject(FinanceApi);

  private readonly pageSignal = signal<SponsorPage | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly signingSignal = signal(false);
  private readonly signErrorSignal = signal<string | null>(null);
  private readonly signedMessageSignal = signal<string | null>(null);

  /** The club's offers, deal and deadline. */
  readonly page = this.pageSignal.asReadonly();

  /** Whether the first read is in flight. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Whether a signing is in flight. */
  readonly signing = this.signingSignal.asReadonly();

  /** Why the last signing was refused. */
  readonly signError = this.signErrorSignal.asReadonly();

  /** What the last successful signing agreed. */
  readonly signedMessage = this.signedMessageSignal.asReadonly();

  /** Whether a read has produced the page. */
  readonly loaded = computed(() => this.pageSignal() !== null);

  /**
   * Reads the page.
   *
   * @param showProgress Whether the screen should swap to its loading state. A reload after a refused signing
   *   keeps the offers on screen instead, so the manager sees them change rather than vanish.
   */
  load(showProgress = true): void {
    this.loadingSignal.set(showProgress);
    this.errorSignal.set(null);

    this.api.getSponsor().subscribe({
      next: (page) => {
        this.pageSignal.set(page);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'Your sponsor offers could not be loaded.',
        );
      },
    });
  }

  /** Signs an offer and an objective, against the version the server last returned for the deal. */
  sign(choice: SignSponsorPayload): void {
    const page = this.pageSignal();

    if (page === null || this.signingSignal()) {
      return;
    }

    this.signingSignal.set(true);
    this.signErrorSignal.set(null);
    this.signedMessageSignal.set(null);

    this.api.signSponsor(choice, page.version).subscribe({
      next: (signed) => {
        this.pageSignal.set(signed);
        this.signingSignal.set(false);
        this.signedMessageSignal.set(
          `Signed with ${signed.deal?.sponsorName ?? 'your sponsor'}. The first payment arrives with the next matchday.`,
        );
      },
      error: (error: unknown) => this.onSignError(error),
    });
  }

  /** Clears the signing messages, so a stale confirmation does not outlive the visit it belonged to. */
  dismissMessages(): void {
    this.signErrorSignal.set(null);
    this.signedMessageSignal.set(null);
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.pageSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
    this.signingSignal.set(false);
    this.signErrorSignal.set(null);
    this.signedMessageSignal.set(null);
  }

  private onSignError(error: unknown): void {
    this.signingSignal.set(false);

    if (error instanceof ApiError && error.isPreconditionFailed) {
      this.load(false);
      this.signErrorSignal.set(
        'Your deal changed since you last looked. The page has been reloaded and nothing was signed. Check it and choose again.',
      );

      return;
    }

    this.signErrorSignal.set(
      error instanceof ApiError ? error.detail : 'The deal could not be signed.',
    );
  }
}
