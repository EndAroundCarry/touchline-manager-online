import { Injectable, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

/**
 * Surfaces a deployed update to the manager (`F-45`, ADR-0007 §11.4).
 *
 * A service worker that activates a new version under a running client can leave the shell on one
 * version and a lazy route on another, so the update is offered rather than forced: the manager
 * chooses when to reload. `SwUpdate` only reports anything while a service worker is enabled, so the
 * store is a no-op in development and in tests, where the worker is not registered.
 */
@Injectable({ providedIn: 'root' })
export class UpdateStore {
  // Optional so the store can be injected where no service worker is provided (development, tests)
  // instead of failing to construct.
  private readonly swUpdate = inject(SwUpdate, { optional: true });

  private readonly updateReadySignal = signal(false);
  private readonly unrecoverableSignal = signal(false);

  /** Whether a new version has been downloaded and is waiting to be activated. */
  readonly updateReady = this.updateReadySignal.asReadonly();

  /** Whether the running version is in a state the worker cannot recover without a reload. */
  readonly unrecoverable = this.unrecoverableSignal.asReadonly();

  constructor() {
    if (this.swUpdate?.isEnabled !== true) {
      return;
    }

    // Only a ready version is actionable; a detection or a failed installation is not shown to the
    // manager, but a failed installation still has to be observed so the stream is not an error.
    this.swUpdate.versionUpdates.subscribe((event) => {
      if (event.type === 'VERSION_READY') {
        this.updateReadySignal.set(true);
      }
    });

    // An unrecoverable state requires a reload to fix; the banner treats it the same as a ready update.
    this.swUpdate.unrecoverable.subscribe(() => this.unrecoverableSignal.set(true));
  }

  /** Activates the downloaded version and reloads the page onto it. */
  reload(): void {
    this.swUpdate?.activateUpdate().then(
      () => globalThis.location?.reload(),
      () => globalThis.location?.reload(),
    );
  }

  /** Defers the update, hiding the prompt until the version is reported ready again. */
  dismiss(): void {
    this.updateReadySignal.set(false);
  }
}
