import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import Aura from '@primeuix/themes/aura';
import { providePrimeNG } from 'primeng/config';
import { problemDetailsInterceptor } from './core/api/problem-details.interceptor';
import { authInterceptor } from './core/auth/auth.interceptor';
import { provideSessionBootstrap } from './core/auth/session-bootstrap';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    provideRouter(routes, withComponentInputBinding()),

    // Order is a contract. The auth interceptor is outermost so that it sees the `ApiError` the
    // problem-details interceptor produces — without that, it could not tell an expired session (401)
    // from any other failure, and would have nothing to retry on.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, problemDetailsInterceptor])),

    // Restores the session before the first route activates, so a guard never has to guess whether the
    // client is signed in while a refresh is still in flight.
    provideSessionBootstrap(),

    providePrimeNG({
      theme: {
        preset: Aura,
        options: {
          darkModeSelector: '.app-dark',

          // The `order` must match the @layer statement in src/styles.css exactly. PrimeNG
          // registering itself between Tailwind's base and utilities is what lets a utility class
          // override a component style without !important.
          cssLayer: {
            name: 'primeng',
            order: 'tailwind-base, primeng, tailwind-utilities',
          },
        },
      },
    }),

    // The service worker is disabled in development: a cached shell during development produces
    // stale-code bugs that look like application bugs.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
