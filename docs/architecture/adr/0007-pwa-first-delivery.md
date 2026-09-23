# ADR-0007: PWA-First Delivery with No Offline Mutations

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §2.3, §11, §14.4, plan §16 Stages 13/20/21

## Context

The first public client must reach desktop, tablet, and mobile from one codebase, be installable, and ship before any native work. Deadline-sensitive commands (lineups, bids, claims) must never execute against stale local state or be silently queued while offline.

## Decision

- Ship a **responsive installable Angular PWA** as the only public client for MVP; Capacitor Android/iOS follow after launch (Stages 20–21).
- Service worker caches only: app shell, fonts/icons, static country/rule reference data, and recently viewed **published** match presentations.
- **Offline reads** show cached data with an explicit stale/offline badge. **Offline writes are blocked and explained** — no queued bids, tactics, lineups, claims, or contract actions (plan §2.3 "Offline mutations" non-goal).
- Access token lives in memory; refresh cookie bootstraps the session on load.
- Data strategy: feature-scoped Signal stores, ETags/`If-Match` for optimistic-concurrency commands, `/sync` polling every 60 s visible (suspended when hidden/offline), never optimistic-update money, bids, claims, renewals, or locks.
- CSP-compatible strict build; WCAG 2.2 AA baseline; `user-select: none` restricted to drag handles and canvas controls.
- Rollout safety: current and one previous API-compatible client version must coexist during deploys.

## Consequences

- One codebase, fast iteration, installability without store review.
- Offline capability is read-only by design, which keeps competitive integrity (server time and server state always win).
- Store presence, push notifications, and native secure storage wait for the Capacitor stages.

## Alternatives considered

- **Native-first (React Native/Capacitor immediately):** rejected — doubles delivery surface before the game loop exists; plan defers native to Stages 20–21.
- **Offline command queue with sync:** rejected explicitly by plan — deadline-sensitive writes must never be silently queued.
- **Server-rendered MVC:** rejected — tactics board, canvas viewer, and dense management screens need a rich client.
