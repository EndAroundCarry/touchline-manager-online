# ADR-0037: PWA update UX, offline mutation gating, and the stale-read indicator

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 13
- **Related:** [ADR-0007](0007-pwa-first-delivery.md), [ADR-0002](0002-auth-and-session-model.md), master plan §11.2, §11.4, §16 Stage 13, `F-44`, `F-45`, test-strategy Layers 7–8.

## Context

ADR-0007 fixed the client's shape — a responsive, installable Angular PWA with a deliberately narrow
offline boundary — and the shell was built to it in Stage 1. But Stage 13 found that only the *scaffolding*
of that boundary existed:

- The service worker registered and cached the shell (`ngsw-config.json`, `manifest.webmanifest`), and the
  shell showed an offline banner — whose text promised that *"changes are disabled"* while no mutation
  control was disabled at all. Offline, every button stayed enabled and simply failed at the network.
- Nothing offered a deployed update. A service worker that activates a new version under a running client
  can leave the shell on one version and a lazy route on another; there was no prompt, no Reload, and no way
  for a manager to know a new build existed.
- Nothing distinguished *stale* data. A failed `/sync` was swallowed (correctly — the next tick retries), but
  the screen went on looking current.
- The poll only fired on its 60-second tick, so returning to a backgrounded tab or reconnecting left the
  unread badge — and any read it gated — up to a minute out of date.

## Decision

**1. A deployed version is offered, never forced.** `UpdateStore` subscribes to `SwUpdate.versionUpdates`
and raises `updateReady` on `VERSION_READY`, and to `SwUpdate.unrecoverable`. The shell shows a banner with
**Reload** (`activateUpdate()` then reload) and **Later** (dismiss). The store injects `SwUpdate` optionally
and is inert when a worker is disabled, so development and the unit suite need no worker to construct it.

**2. The three notices live in one component, not in the shell.** `SystemNotices` injects only
`ConnectivityStore`, `SyncStore`, and `UpdateStore` and owns the wording and ordering of the offline, stale,
and update banners. `AppShell` injects thirteen stores; keeping the notices there made their states
effectively untestable, and off the shell they have a focused spec.

**3. Staleness is defined narrowly and honestly.** `SyncStore` exposes `lastRefreshedAt` and `refreshFailed`;
`isStale = !isOnline || refreshFailed`. Because offline has its own banner, the **stale** banner appears only
while *online but the last `/sync` failed*. We cannot observe a service-worker cache hit from the
application, so we do not claim to: the label describes the read we can see.

**4. The poll re-reads when freshness can be regained.** `SyncStore.start()` also listens for
`visibilitychange` (→ visible) and `online`, and reads immediately. `stop()` removes them with the interval,
so no listener outlives the shell.

**5. Offline gating is explicit in the templates, not a directive.** Each mutation component exposes
`protected readonly canMutate = this.connectivity.isOnline;` and composes `|| !canMutate()` into its
controls' `disabled` (and the `<fieldset>`s that wrap them). A directive that set `disabled` would fight the
existing `[disabled]` bindings (last writer wins), and one that only set `aria-disabled` could not stop a form
submit. The explicit form is conflict-free, is visible in each screen's own contract, and matches how the rest
of the client is written. It is applied across onboarding (profile, club claim), tactics, training, prepare,
transfers, the player contract actions, and settings.

**6. Cached reads stay inside the ADR-0007 list.** `ngsw-config.json` gains a `reference-data` data group for
the public world summary and country list only (`/api/v1/world`, `/api/v1/countries`), alongside the existing
published match presentations. Volatile reads — capacity, `/sync`, anything mutable — are deliberately
**not** cached. The dead `/index.csr.html` entry (never emitted; there is no SSR/prerender) is removed, and
`index.html` gains an `apple-touch-icon`.

**7. Offline mutations are never queued.** This restates ADR-0007 with the implementation's specifics: a bid
replayed after its auction closed is worse than a bid the manager knows was never sent. There is no queue, no
service-worker background sync, and no optimistic update for money, bids, claims, renewals, or team sheets.

**8. The PWA is end-to-end tested against the production build.** The service worker is enabled only outside
development, so the existing journeys (which run `ng serve`) cannot register one. A separate Playwright stack
builds the production bundle, serves it from a small static origin that proxies `/api`, and drives install,
offline reads, and offline mutation blocking there.

## Consequences

**Positive**

- The offline banner is now true: mutation controls are actually disabled, and the reason is stated.
- A manager is told when a new version is ready and chooses when to take it, which is what keeps a lazy route
  from being loaded from the wrong build.
- A read that could not reach the server is labelled rather than passed off as current.
- Coming back to a tab or reconnecting refreshes at once instead of waiting out the tick.
- The boundary is pinned by a browser test on the only stack where the worker is real.

**Negative**

- Gating is repeated at each mutation site rather than enforced by one mechanism, so a new screen must
  remember to compose `canMutate`. The shell banner states the rule, and the PWA journey asserts it for the
  surfaces it visits, but it is a convention rather than a framework guarantee.
- The stale label cannot see a service-worker cache hit; it reports a failed refresh, which is the observable
  proxy. A read served stale by the worker while the server is reachable is not labelled.
- The PWA stack adds a production build and a static server to the end-to-end run. It is a fourth config, and
  it is slower than the dev-server suites, which is why it is its own step.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A directive that sets `disabled` when offline | It would fight the per-screen `[disabled]` bindings (Angular applies both; the winner is not a contract), and a captured click alone cannot stop Enter submitting a form. |
| A directive that only sets `aria-disabled` | Screen readers would say "disabled" while the control still submitted — worse than not marking it. |
| Force the update (`activateUpdate` on `VERSION_READY`) | Reloading a manager mid-action loses what they were doing, and a lazy route loaded across builds can break; ADR-0007 asks the worker to prompt, not force. |
| Treat any cached response as stale | The application cannot see service-worker cache hits, so it would have to guess; guessing wrong either cries wolf or hides a real failure. |
| Cache more API reads for offline | Volatile reads (capacity, sync, mutable state) served from cache would show a manager a stale world as if it were current; only the reference reads and immutable presentations are cached. |
| Queue mutations offline and replay them | Contradicts server authority on deadlines (ADR-0007) and lies about game state. |
| Test the PWA on the `ng serve` stack | The worker is disabled in development by design, so the install and offline assertions would test nothing. |
