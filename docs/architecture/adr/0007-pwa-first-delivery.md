# ADR-0007: PWA-first client delivery

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (shell in Stage 1, hardening in Stage 13)
- **Related:** [ADR-0002](0002-auth-and-session-model.md), [ADR-0008](0008-deployment-topology.md)

## Context

The game is asynchronous: managers prepare squads and tactics, and the server resolves
matches on a schedule. There is no live in-match interaction in the MVP (`no live tactical
changes`, no WebSockets, no synchronous PvP). That removes the only strong technical argument
for a native client at launch.

At the same time, managers will use the product on phones as well as desktops, and the
product must eventually ship to Android and iOS app stores.

## Decision

**The first public client is a responsive, installable Angular PWA.**

- Angular standalone components with Signals, strict TypeScript, RxJS only at I/O boundaries.
- Service worker caches the app shell, fonts/icons, static reference data (countries, rules),
  and recently viewed **published** match presentations.
- Installable: manifest, icons, theme colours, update prompt, and documented service-worker
  rollback guidance.
- Native Android/iOS packaging is a later Capacitor wrap of the same application, not a
  separate codebase. `npx cap sync` is not an acceptance criterion; CI must build and sign a
  real `.aab` for the intended track.

**Offline boundary is explicit and deliberately narrow.**

- Cached data is displayed with a clear stale/offline indicator.
- **Mutations are disabled while offline and explained in the UI.** Bids, listings, tactics
  submissions, team sheets, club claims, and contract actions are never queued for later
  replay. Deadline-sensitive writes must never be silently queued — a bid replayed after its
  auction closed is worse than a bid the user knows was never sent.
- No optimistic updates for money, bids, club claims, contract renewals, or fixture locks.

**Synchronization model.** REST/JSON with ETags. The client polls `/sync` every 60 seconds
while visible, slows or suspends in hidden/background tabs, and refetches after visibility or
network changes. Polling frequency may increase near a match or an auction close only where
justified by measured value.

**Version compatibility.** A newly deployed client and one previous API-compatible client
version must coexist during rollout. The API therefore avoids removing fields within a
release window, and the service worker prompts rather than force-updating mid-session.

## Consequences

**Positive**

- One codebase for desktop, mobile web, Android, and iOS.
- Distribution without app-store review for the first release; faster iteration on the core
  loop, which is where the balance risk actually is.
- Installable and offline-readable, which suits an asynchronous game where reading the table
  and the last result while offline is genuinely useful.

**Negative**

- Browser differences in service-worker update behaviour must be handled explicitly.
- Canvas rendering and installability vary across devices, so a device matrix and performance
  budgets are required rather than optional.
- The "no offline mutations" rule needs careful UX: users must understand *why* a button is
  disabled instead of assuming the app is broken.
- App-store expectations (notifications, background behaviour) will require real work later
  in the Capacitor stage rather than at wrap time.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Native-first (Flutter/React Native) | Doubles the client surface and delays the core-loop learning that actually determines whether the game is good. |
| Desktop-only SPA, no PWA | Loses phone usage, which is the majority of casual management-game sessions. |
| Offline-first with queued mutations | Contradicts server authority on deadlines; a queued bid is a lie about the game state. |
| Server-sent events / WebSockets from day one | No live in-match interaction in MVP; adds connection management, scale, and reconnection complexity for information that changes three times a week. |
