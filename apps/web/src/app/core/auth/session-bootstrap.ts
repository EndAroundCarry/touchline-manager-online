import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { SessionStore } from './session-store';

/**
 * Restores the session before the first route renders.
 *
 * A single initializer rather than per-feature bootstrap code, so a guarded route can rely on
 * {@link SessionStore.status} already being decided instead of guessing while a refresh is in flight
 * (ADR-0002). It never rejects, so an API outage still leaves the public screens usable.
 */
export function provideSessionBootstrap(): EnvironmentProviders {
  return provideAppInitializer(() => inject(SessionStore).restore());
}
