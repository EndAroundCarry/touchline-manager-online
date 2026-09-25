import { Routes } from '@angular/router';
import {
  requireAnonymous,
  requireAuthentication,
  requireVerifiedEmail,
} from './core/auth/auth.guards';

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
      {
        path: 'onboarding/manager',
        loadComponent: () =>
          import('./features/onboarding/manager/manager').then((m) => m.ManagerProfile),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Your manager profile — Touchline Manager',
      },
      {
        path: 'onboarding/country',
        loadComponent: () =>
          import('./features/onboarding/country/country').then((m) => m.CountryChoice),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Choose your country — Touchline Manager',
      },
      {
        path: 'onboarding/club',
        loadComponent: () => import('./features/onboarding/club/club').then((m) => m.ClubChoice),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Choose your club — Touchline Manager',
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Dashboard — Touchline Manager',
      },
      {
        path: 'squad',
        loadComponent: () => import('./features/squad/squad').then((m) => m.Squad),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Squad — Touchline Manager',
      },
      {
        path: 'tactics',
        loadComponent: () => import('./features/tactics/tactics').then((m) => m.Tactics),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Tactics — Touchline Manager',
      },
      {
        path: 'training',
        loadComponent: () => import('./features/training/training').then((m) => m.Training),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Training — Touchline Manager',
      },
      {
        // A detail route rather than a navigation destination, so it is not in `nav-items.ts`.
        path: 'players/:id',
        loadComponent: () => import('./features/player/player').then((m) => m.PlayerProfile),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Player — Touchline Manager',
      },
    ],
  },

  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
    title: 'Not found — Touchline Manager',
  },
];
