# ADR-0015: A compressed test clock is chosen at composition and refused in Production

- **Status:** Accepted
- **Date:** 2026-09-25
- **Stage:** 6
- **Related:** [ADR-0009](0009-time-identity-and-concurrency.md), [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0012](0012-daily-progression-materialised-job.md), master plan §15.5, §16 Stage 6, game rules `TIME-2`, `TIME-4`, `TIME-6`

## Context

Stage 6's exit criteria are already met by tests, but they are met by a test that moves a `FakeClock` past a
fixture's kickoff inside a process the test owns (`MatchdayWorkerTests`). That proves the workflow; it does
not let anyone *watch* it. Three real-time gaps make a running season impossible to observe without a clock
that can be accelerated:

- Matchdays are two to three days apart and a season is thirty-four of them, so the game's own cadence is
  roughly eleven weeks.
- A team sheet locks thirty minutes before kickoff, and the queue only becomes due when the wall clock
  reaches a stored instant — so nothing happens until real time arrives.
- The daily progression and the calendar's materialiser both re-derive their work from `IClock` on every
  pass, which means a clock that runs fast would genuinely drive them.

ADR-0009 already permits the mechanism — "compressed test clocks exist only in non-production
environments" — but nothing implemented it. Without it, the only lever is editing `World:FirstSeasonStartDate`
and re-seeding, which costs a fresh database (`WORLD-1` makes the seeder idempotent), and the end-to-end
journey and the multi-season staging runs §15.5 and Stage 12 ask for have no way to reach a rollover in
reasonable time.

The decisions that matter are: how game time is derived from real time, where the choice of clock is made,
and how a production deployment is kept away from it.

## Decision

**1. Game time is an affine map of real time, expressed as an anchor pair and a rate.**

`virtual = VirtualAnchorUtc + (realNow − RealAnchorUtc) × Rate`, implemented by `CompressedClock`, configured
under `Clock`. Both anchors are configuration and neither is process state. That is the point: the API
computes "server now" and the worker computes "is this job due" from the same real instant and the same
configuration, so they can never disagree about whether a deadline has passed. A per-process anchor ("the
moment this host started") would disagree by startup skew × rate, which at a season-compressing rate is
hours.

`VirtualAnchorUtc` is optional and defaults to `RealAnchorUtc`. Left unset, game time equals real time at
the anchor and then runs ahead of it — a pure speed-up. Set ahead of the real anchor, it also offsets the
world's clock, so a season can be pinned to a chosen date (typically just before its first kickoff) while
real time carries on from today.

**2. The clock is chosen at composition, and Production refuses a compressed one.**

`AddGameClock(services, configuration, environment)` replaces the default `SystemClock` only when the
configuration selects `Compressed` *and* the environment is not Production. A production host that asks for
compression does not silently fall back to real time — it fails to start, by name. A quiet fallback would
leave an operator believing a season was accelerated when it was not, which is the exact accident the rule
exists to prevent (`TIME-6`). Composition roots are where the environment is known, so this is the one place
the decision lives; it is not repeated in each root.

**3. Every process reads one clock.**

The API, the worker, and the world seeder all call `AddGameClock`. The queue already reads `IClock` for every
enqueue, claim, completion, and retry delay, and the schedulers read it for their horizons. There is no
component that reads the wall clock directly (`TIME-2`), so nothing needs to be taught about compression —
the clock is the only thing that changes.

**4. There is no HTTP control surface.**

Enabling compression is a configuration change and a restart. There is deliberately no endpoint that moves
time: a runtime-writable "now" is a deadline-critical write surface master plan §10 does not define, and
§17.12 keeps unreachable features out of the API rather than half-built. An operator who wants a different
picture restarts with a different anchor.

**5. The configuration is validated at startup.**

`Rate` must be between 2 and 100,000 when compressed, and `RealAnchorUtc` must be set. Both are checked in
`AddInfrastructure`, so a half-written compressed configuration fails immediately in every host rather than
at the first job. The arithmetic is clamped to a thousand years of game time from the anchor so a very
long-running compressed environment degrades rather than overflowing.

## Consequences

**Positive**

- A season that takes eleven weeks can be watched in minutes, which is what the end-to-end journey (§15.5
  journey 7) and the compressed multi-season staging runs (Stage 12) need.
- The safety property is structural rather than procedural: compression is opt-in, off by default, and a
  production host cannot be built with it, so it cannot reach a live world by a configuration accident.
- Determinism is preserved. Nothing about a played match depends on the clock: seeds come from the world
  secret and the frozen snapshot, and a compressed clock only changes *when* a deadline is reached, never
  what happens when it is (`MAT-9`).
- The mechanism is one class and one guard. No scheduler, job, or use case knows it exists.

**Negative**

- **A browser's countdowns are real time and therefore disagree with a compressed server clock.** The
  server's `IsLocked` answer is authoritative and the client is refused after the lock, so the control state
  is correct; what misleads is the countdown phrase. Closing that gap means the fixture and team-sheet reads
  carrying a server instant for the client to measure against — the `TIME-5` work those reads do not yet do —
  and it is deferred rather than half-built here.
- A running compressed world cannot be re-anchored without a restart. Accepted: it is a test tool whose
  natural lifetime is a test run or a staging session.
- The rate and anchors are process-wide, so all countries share one accelerated cadence. That matches the
  MVP, where all countries already share one season cadence (§3.4).
- A compressed clock makes every compiled-in real-time assumption visible at once, which is a feature for
  finding them and a nuisance for reading logs whose timestamps span years in a single run.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| An offset-only clock ("pretend it is now T") | Cannot compress the cadence, only jump to one deadline; every matchday needs another edit and the jumps are not reproducible across processes. |
| Anchor compression at each process's start time | The API and the worker would disagree by startup skew × rate — hours at a season-compressing rate — so a deadline could be locked for one and open for the other. |
| An operator endpoint that advances time | A public, deadline-critical write surface with no rule behind it; §10 defines no such command, and a mutable "now" is exactly the kind of state a deterministic system avoids. |
| Seed a near matchday for each test run | The seeder is idempotent by design (`WORLD-1`); a second date needs a fresh database, which staging cannot do per journey and a running world cannot do at all. |
| Only a test-harness `FakeClock` | Already exists, and is what the unit and integration tests use. It cannot accelerate a running worker, which is the entire purpose. |
| An `ops.feature_flags` row | The flag table does not exist yet, and this is a deployment-shape decision rather than a product toggle; configuration is where the rest of the host's shape lives (`RULE-1`, ADR-0012). |
