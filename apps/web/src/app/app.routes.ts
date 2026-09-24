import { Routes } from '@angular/router';
import { requireAnonymous, requireAuthentication } from './core/auth/auth.guards';

/**
 * Route table.
 *
 * Screens are lazy-loaded per route so the initial bundle stays small, which matters on the mobile
 * connections this game targets (ADR-0007).
 *
 * `requireAnonymous` keeps a signed-in manager off the sign-in and registration forms; showing those
 * to somebody already signed in invites them to replace a working session by accident.
 * `requireAuthentication` guards the manager area, which the bootstrap initializer has already decided
 * by the time any guard runs.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'welcome' },

  {
    path: '',
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: 'welcome',
        loadComponent: () => import('./features/welcome/welcome').then((m) => m.Welcome),
        title: 'Welcome — Touchline Manager',
      },
      {
        path: 'register',
        loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
        canActivate: [requireAnonymous],
        title: 'Create your account — Touchline Manager',
      },
      {
        path: 'login',
        loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
        canActivate: [requireAnonymous],
        title: 'Sign in — Touchline Manager',
      },
      {
        // Reachable while signed out and while unverified, because the link comes from an email.
        path: 'verify-email',
        loadComponent: () =>
          import('./features/auth/verify-email/verify-email').then((m) => m.VerifyEmail),
        title: 'Confirm your email — Touchline Manager',
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
        canActivate: [requireAnonymous],
        title: 'Reset your password — Touchline Manager',
      },
      {
        path: 'reset-password',
        loadComponent: () =>
          import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
        title: 'Choose a new password — Touchline Manager',
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
        canActivate: [requireAuthentication],
        title: 'Settings — Touchline Manager',
      },
    ],
  },

  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
    title: 'Not found — Touchline Manager',
  },
];
