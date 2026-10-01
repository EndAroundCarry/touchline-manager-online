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
        // The player-facing information pages (master plan §16 Stage 15, F-55, ADR-0050). Unguarded and
        // shell-wrapped, so a signed-out visitor — and the register consent — can reach them.
        path: 'rules',
        data: { document: 'rules' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Game rules — Touchline Manager',
      },
      {
        path: 'privacy',
        data: { document: 'privacy' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Privacy — Touchline Manager',
      },
      {
        path: 'terms',
        data: { document: 'terms' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Terms — Touchline Manager',
      },
      {
        path: 'support',
        data: { document: 'support' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Support — Touchline Manager',
      },
      {
        path: 'status',
        loadComponent: () => import('./features/status/status').then((m) => m.Status),
        title: 'Service status — Touchline Manager',
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
        path: 'finances',
        loadComponent: () => import('./features/finances/finances').then((m) => m.Finances),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Finances — Touchline Manager',
      },
      {
        path: 'scouting',
        loadComponent: () => import('./features/scouting/scouting').then((m) => m.Scouting),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Scouting — Touchline Manager',
      },
      {
        path: 'transfers',
        loadComponent: () => import('./features/transfers/transfers').then((m) => m.Transfers),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Transfers — Touchline Manager',
      },
      {
        // One division's table by identity, for a shared or bookmarked link (§11.1).
        path: 'competitions/:divisionId/table',
        loadComponent: () =>
          import('./features/competitions/table').then((m) => m.CompetitionTable),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Table — Touchline Manager',
      },
      {
        // One division's player statistics by identity, reached from its table screen (§11.1).
        path: 'competitions/:divisionId/statistics',
        loadComponent: () =>
          import('./features/competitions/statistics').then((m) => m.CompetitionStatistics),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Statistics — Touchline Manager',
      },
      {
        // One division's competition rules and its stored tie-break draw (TBL-11).
        path: 'competitions/:divisionId/rules',
        loadComponent: () =>
          import('./features/competitions/rules').then((m) => m.CompetitionRules),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Competition rules — Touchline Manager',
      },
      {
        // One division's cards and outstanding suspensions, reached from its table (DIS-2, DIS-4, DIS-5).
        path: 'competitions/:divisionId/discipline',
        loadComponent: () =>
          import('./features/competitions/discipline').then((m) => m.CompetitionDiscipline),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Discipline — Touchline Manager',
      },
      {
        // The manager's own division's table. The division is named by their club, so the navigation
        // destination needs no parameter.
        path: 'competitions',
        loadComponent: () =>
          import('./features/competitions/table').then((m) => m.CompetitionTable),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Table — Touchline Manager',
      },
      {
        // The manager's own club's season history, resolved from their fixture list like the table.
        path: 'history',
        loadComponent: () => import('./features/history/history').then((m) => m.SeasonHistory),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Seasons — Touchline Manager',
      },
      {
        path: 'fixtures',
        loadComponent: () => import('./features/fixtures/fixtures').then((m) => m.Fixtures),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Fixtures — Touchline Manager',
      },
      {
        path: 'inbox',
        loadComponent: () => import('./features/inbox/inbox').then((m) => m.Inbox),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Inbox — Touchline Manager',
      },
      {
        // The public division news feed (`COM-1`), beside the inbox it shares its message shape with.
        path: 'news',
        loadComponent: () => import('./features/news/news').then((m) => m.News),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'News — Touchline Manager',
      },
      {
        // How the game works: the reference half of the guided help (`F-53`).
        path: 'help',
        loadComponent: () => import('./features/help/help').then((m) => m.Help),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Help — Touchline Manager',
      },
      {
        // A detail route for one fixture, where a manager prepares a side (`SQ-4`).
        path: 'fixtures/:fixtureId/prepare',
        loadComponent: () => import('./features/prepare/prepare').then((m) => m.Prepare),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Prepare — Touchline Manager',
      },
      {
        // A detail route rather than a navigation destination: a match is reached from its fixture, not
        // from the sidebar.
        path: 'matches/:matchId',
        loadComponent: () =>
          import('./features/match-viewer/match-viewer').then((m) => m.MatchViewer),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Match — Touchline Manager',
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
