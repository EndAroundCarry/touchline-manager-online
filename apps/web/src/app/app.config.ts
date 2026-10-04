import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import Aura from '@primeuix/themes/aura';
import { definePreset } from '@primeuix/themes';
import { providePrimeNG } from 'primeng/config';
import { problemDetailsInterceptor } from './core/api/problem-details.interceptor';
import { authInterceptor } from './core/auth/auth.interceptor';
import { provideSessionBootstrap } from './core/auth/session-bootstrap';
import { routes } from './app.routes';

/**
 * The Aura preset retuned to the game's workspace, dark by default with a light scheme (ADR-0059).
 *
 * The primary ramp is the accent periwinkle, centred on 400 because Aura's dark scheme reads its primary
 * colour from step 400. The surface ramp is the same near-black-to-chalk ramp the Tailwind tokens in
 * `styles.css` use, so a PrimeNG table and a hand-written panel are the same colours: surface 950 is the
 * ground, 900 the panel, 800 the raised well, 700 the line.
 */
const TouchlinePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#f1f5ff',
      100: '#e2ebff',
      200: '#c8d9ff',
      300: '#a3bdff',
      400: '#7aa2ff',
      500: '#5a87f5',
      600: '#4268dc',
      700: '#3552b5',
      800: '#2c4490',
      900: '#263a73',
      950: '#172346',
    },
    colorScheme: {
      // The light scheme: the same accent, at a step dark enough for text on white, over a cool paper surface ramp.
      light: {
        surface: {
          0: '#ffffff',
          50: '#f6f8fb',
          100: '#eef2f7',
          200: '#d5dce6',
          300: '#aab5c4',
          400: '#8794a6',
          500: '#566275',
          600: '#364354',
          700: '#2a3441',
          800: '#1e2631',
          900: '#131a24',
          950: '#0b0f14',
        },
        primary: {
          color: '{primary.600}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.700}',
          activeColor: '{primary.800}',
        },
      },
      dark: {
        surface: {
          0: '#e8ecf2',
          50: '#e8ecf2',
          100: '#c7d0dc',
          200: '#aab5c4',
          300: '#98a4b5',
          400: '#7c8899',
          500: '#5d6978',
          600: '#3d495a',
          700: '#2a3441',
          800: '#1e2631',
          900: '#171d25',
          950: '#0f1318',
        },
        primary: {
          color: '{primary.400}',
          contrastColor: '#0b1020',
          hoverColor: '{primary.300}',
          activeColor: '{primary.200}',
        },
      },
    },
  },
});

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
      license:
        'eyJpZCI6IjU4ZDEzMmE4LTUzMzgtNDAzOS05ZjJmLTczYmU0NmE3YmQyOCIsInByb2R1Y3QiOiJwcmltZXVpIiwidGllciI6ImNvbW11bml0eSIsInR5cGUiOiJkZXYiLCJpYXQiOjE3OTA3NjU5NDQsImV4cCI6MTgyMjMwMTk0NH0.10IQx7ZBMNM0xeJerPld2Q4JkfrCtzVAdCVI-QIUGOcsyXBY2j-GSfbZxFONwqbLSmQZJwrYqXbNyLZg5KchCQ',
      theme: {
        preset: TouchlinePreset,
        options: {
          darkModeSelector: '.app-dark',

          // The `order` must match the @layer statement in src/styles.css exactly. PrimeNG
          // registering itself between Tailwind's base and utilities is what lets a utility class
          // override a component style without !important.
          cssLayer: {
            name: 'primeng',
            order: 'tailwind-base, primeng, app, tailwind-utilities',
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
