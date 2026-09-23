import { Routes } from '@angular/router';

/**
 * Route table.
 *
 * Screens are lazy-loaded per route so the initial bundle stays small, which matters on the mobile
 * connections this game targets (ADR-0007). Authentication and onboarding routes arrive in Stage 2;
 * they slot into this table rather than replacing it.
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
    ],
  },

  {
    path: '**',
    loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound),
    title: 'Not found — Touchline Manager',
  },
];
