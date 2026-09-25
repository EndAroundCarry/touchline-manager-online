# Feature folders

Each folder here is one bounded screen area from master plan §11.1. A folder appears when its stage
lands, and its route is added to `app.routes.ts` at the same time, so no unreachable screen exists.

| Folder | Screens | Stage |
|---|---|---|
| `auth/` | `/register`, `/verify-email`, `/login`, `/forgot-password`, `/reset-password` | 2 |
| `onboarding/` | `/onboarding/manager`, `/onboarding/country`, `/onboarding/club` | 3 |
| `dashboard/` | `/dashboard` | 6 |
| `squad/` | `/squad` | 4 |
| `player/` | `/players/:id` | 4 |
| `tactics/` | `/tactics` | 4 |
| `training/` | `/training` | 4 |
| `competitions/` | `/competitions/:divisionId/table`, `/competitions/:divisionId/fixtures` | 6 |
| `fixtures/` | `/fixtures/:fixtureId/prepare` | 6 |
| `match-viewer/` | `/matches/:matchId` plus the framework-neutral Canvas renderer | 7 |
| `scouting/` | `/scouting` | 10 |
| `transfers/` | `/transfers` | 10 |
| `finances/` | `/finances` | 9 |
| `inbox/` | `/inbox` | 11 |
| `settings/` | `/settings` | 13 |
| `admin/` | `/admin`, lazy-loaded and role protected | 14 |

Present today: `welcome/`, `not-found/`, the `auth/` screens, `settings/`, the `onboarding/` screens,
`dashboard/`, and — as of Stage 4 — `squad/` and `player/`. A folder and its route land together, so the
table above doubles as the record of what is reachable.

## Conventions

- **State:** feature-scoped Signal stores or facades. There is no single global mutable store.
- **Transport:** call the API through `core/api/api-client.ts` so the base URL, concurrency headers,
  and `ApiError` mapping are applied once.
- **Loading and failure:** every command screen handles pending, success, validation, conflict,
  timeout, and retry. A `412` must show the changed server state and let the manager reapply
  deliberately rather than overwriting it.
- **Accessibility:** an attribute, status, or team is never distinguished by colour alone.
- **Class naming:** the Angular CLI convention — `squad.ts`, not `squad.component.ts`.
