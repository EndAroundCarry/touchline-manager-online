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
        title: 'Welcome — Tactical11 Manager',
      },
      {
        // The player-facing information pages (master plan §16 Stage 15, F-55, ADR-0050). Unguarded and
        // shell-wrapped, so a signed-out visitor — and the register consent — can reach them.
        path: 'rules',
        data: { document: 'rules' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Game rules — Tactical11 Manager',
      },
      {
        path: 'privacy',
        data: { document: 'privacy' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Privacy — Tactical11 Manager',
      },
      {
        path: 'terms',
        data: { document: 'terms' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Terms — Tactical11 Manager',
      },
      {
        path: 'support',
        data: { document: 'support' },
        loadComponent: () => import('./features/info/info').then((m) => m.Info),
        title: 'Support — Tactical11 Manager',
      },
      {
        path: 'status',
        loadComponent: () => import('./features/status/status').then((m) => m.Status),
        title: 'Service status — Tactical11 Manager',
      },
      {
        path: 'register',
        loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
        canActivate: [requireAnonymous],
        title: 'Create your account — Tactical11 Manager',
      },
      {
        path: 'login',
        loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
        canActivate: [requireAnonymous],
        title: 'Sign in — Tactical11 Manager',
      },
      {
        // Reachable while signed out and while unverified, because the link comes from an email.
        path: 'verify-email',
        loadComponent: () =>
          import('./features/auth/verify-email/verify-email').then((m) => m.VerifyEmail),
        title: 'Confirm your email — Tactical11 Manager',
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
        canActivate: [requireAnonymous],
        title: 'Reset your password — Tactical11 Manager',
      },
      {
        path: 'reset-password',
        loadComponent: () =>
          import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
        title: 'Choose a new password — Tactical11 Manager',
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
        canActivate: [requireAuthentication],
        title: 'Settings — Tactical11 Manager',
      },
      {
        path: 'onboarding/manager',
        loadComponent: () =>
          import('./features/onboarding/manager/manager').then((m) => m.ManagerProfile),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Your manager profile — Tactical11 Manager',
      },
      {
        path: 'onboarding/country',
        loadComponent: () =>
          import('./features/onboarding/country/country').then((m) => m.CountryChoice),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Choose your country — Tactical11 Manager',
      },
      {
        path: 'onboarding/club',
        loadComponent: () => import('./features/onboarding/club/club').then((m) => m.ClubChoice),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Choose your club — Tactical11 Manager',
      },
      {
        path: 'onboarding/colours',
        loadComponent: () =>
          import('./features/onboarding/colours/colours').then((m) => m.ClubColoursChoice),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Choose your colours and crest — Tactical11 Manager',
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Office — Tactical11 Manager',
      },
      {
        path: 'squad',
        loadComponent: () => import('./features/squad/squad').then((m) => m.Squad),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Squad — Tactical11 Manager',
      },
      {
        path: 'tactics',
        loadComponent: () => import('./features/tactics/tactics').then((m) => m.Tactics),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Tactics — Tactical11 Manager',
      },
      {
        path: 'training',
        loadComponent: () => import('./features/training/training').then((m) => m.Training),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Training — Tactical11 Manager',
      },
      {
        path: 'finances',
        loadComponent: () => import('./features/finances/finances').then((m) => m.Finances),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Finances — Tactical11 Manager',
      },
      {
        path: 'finances/sponsor',
        loadComponent: () => import('./features/finances/sponsor').then((m) => m.SponsorScreen),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Sponsor — Tactical11 Manager',
      },
      {
        path: 'stadium',
        loadComponent: () => import('./features/stadium/stadium').then((m) => m.StadiumScreen),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Stadium — Tactical11 Manager',
      },
      {
        path: 'structures',
        loadComponent: () =>
          import('./features/structures/structures').then((m) => m.StructuresScreen),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Structures — Tactical11 Manager',
      },
      {
        path: 'fans',
        loadComponent: () => import('./features/fan-base/fan-base').then((m) => m.FanBasePage),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Fan base — Tactical11 Manager',
      },
      {
        path: 'scouting',
        loadComponent: () => import('./features/scouting/scouting').then((m) => m.Scouting),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Scouting — Tactical11 Manager',
      },
      {
        path: 'transfers',
        loadComponent: () => import('./features/transfers/transfers').then((m) => m.Transfers),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Transfers — Tactical11 Manager',
      },
      {
        // One division's table by identity, for a shared or bookmarked link (§11.1).
        path: 'competitions/:divisionId/table',
        loadComponent: () =>
          import('./features/competitions/table').then((m) => m.CompetitionTable),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Table — Tactical11 Manager',
      },
      {
        // One division's player statistics by identity, reached from its table screen (§11.1).
        path: 'competitions/:divisionId/statistics',
        loadComponent: () =>
          import('./features/competitions/statistics').then((m) => m.CompetitionStatistics),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Statistics — Tactical11 Manager',
      },
      {
        // One division's competition rules and its stored tie-break draw (TBL-11).
        path: 'competitions/:divisionId/rules',
        loadComponent: () =>
          import('./features/competitions/rules').then((m) => m.CompetitionRules),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Competition rules — Tactical11 Manager',
      },
      {
        // One division's cards and outstanding suspensions, reached from its table (DIS-2, DIS-4, DIS-5).
        path: 'competitions/:divisionId/discipline',
        loadComponent: () =>
          import('./features/competitions/discipline').then((m) => m.CompetitionDiscipline),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Discipline — Tactical11 Manager',
      },
      {
        // The manager's own division's table. The division is named by their club, so the navigation
        // destination needs no parameter.
        path: 'competitions',
        loadComponent: () =>
          import('./features/competitions/table').then((m) => m.CompetitionTable),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Table — Tactical11 Manager',
      },
      {
        // The manager's own club's season history, resolved from their fixture list like the table.
        path: 'history',
        loadComponent: () => import('./features/history/history').then((m) => m.SeasonHistory),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Seasons — Tactical11 Manager',
      },
      {
        path: 'fixtures',
        loadComponent: () => import('./features/fixtures/fixtures').then((m) => m.Fixtures),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Fixtures — Tactical11 Manager',
      },
      {
        // What is happening at the club on each day: matches, finances, injuries and whatever sources join them.
        path: 'calendar',
        loadComponent: () => import('./features/calendar/calendar').then((m) => m.Calendar),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Calendar — Tactical11 Manager',
      },
      {
        // Two sections under one address: what the game told the manager, and what other managers wrote
        // (`COM-10`). `managers/new` precedes the thread so "new" is never read as a conversation identity.
        path: 'inbox',
        canActivate: [requireAuthentication, requireVerifiedEmail],
        children: [
          {
            path: '',
            loadComponent: () => import('./features/inbox/inbox').then((m) => m.Inbox),
            title: 'Inbox — Tactical11 Manager',
          },
          {
            path: 'managers',
            loadComponent: () =>
              import('./features/inbox/managers/conversation-list').then((m) => m.ConversationList),
            title: 'Messages from managers — Tactical11 Manager',
          },
          {
            path: 'managers/new',
            loadComponent: () =>
              import('./features/inbox/managers/compose').then((m) => m.ComposeMessage),
            title: 'New message — Tactical11 Manager',
          },
          {
            path: 'managers/:conversationId',
            loadComponent: () =>
              import('./features/inbox/managers/thread').then((m) => m.ConversationThread),
            title: 'Conversation — Tactical11 Manager',
          },
        ],
      },
      {
        // The public division news feed (`COM-1`), beside the inbox it shares its message shape with.
        path: 'news',
        loadComponent: () => import('./features/news/news').then((m) => m.News),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'News — Tactical11 Manager',
      },
      {
        // How the game works: the reference half of the guided help (`F-53`).
        path: 'help',
        loadComponent: () => import('./features/help/help').then((m) => m.Help),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Help — Tactical11 Manager',
      },
      {
        // The old prepare screen is gone; links saved or sent before then land on the tactics screen.
        path: 'fixtures/:fixtureId/prepare',
        redirectTo: 'tactics',
      },
      {
        // A detail route rather than a navigation destination: a match is reached from its fixture, not
        // from the sidebar.
        path: 'matches/:matchId',
        loadComponent: () =>
          import('./features/match-viewer/match-viewer').then((m) => m.MatchViewer),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Match — Tactical11 Manager',
      },
      {
        // A detail route rather than a navigation destination, so it is not in `nav-items.ts`.
        path: 'players/:id',
        loadComponent: () => import('./features/player/player').then((m) => m.PlayerProfile),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Player — Tactical11 Manager',
      },
      {
        // Any club's public page: its squad and its season, for studying a rival. A detail route like the
        // player's, reached from a club's name wherever one is shown.
        path: 'clubs/:id',
        loadComponent: () => import('./features/club/club').then((m) => m.ClubPage),
        canActivate: [requireAuthentication, requireVerifiedEmail],
        title: 'Club — Tactical11 Manager',
      },
    ],
  },

  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
    title: 'Not found — Tactical11 Manager',
  },
];
