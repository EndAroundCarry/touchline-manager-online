# ADR-0027: The inactivity ladder is one scheduled pass over real time, and a login is the return

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 11
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0009](0009-time-identity-and-concurrency.md), [ADR-0018](0018-ai-club-policy.md), game rules `OCC-1`, `OCC-2`, `OCC-3`, `OCC-5`, `OCC-8`, master plan §7.2, §16 Stage 11

## Context

`ClubTenure` has carried `LastActiveAt`, `RecordActivity`, `MarkInactive`, `Resume`, and `Close` since Stage 3,
and `WorldRuleSet` has carried the three thresholds (`InactivityWarningAfter` 10 days,
`InactivityAiAssistanceAfter` 14, `InactivityCloseAfter` 21) since the occupancy rules were specified. Nothing
called any of them: a tenure could be opened and closed by a manager, but time never closed one, so a club
whose manager had left the game stayed held forever and the country never freed a place.

`OCC-1`–`OCC-3` describe a ladder — warn, then hand routine decisions to the AI without closing, then close and
return the club to the AI — and `OCC-5` adds that closing a tenure never rewinds club state. Four decisions
were open: what drives the ladder, when a manager is considered back, which accounts are aged, and how the
warning is sent once.

## Decision

**1. One scheduled pass, over the whole membership, in real time.** `EvaluateInactivity` lists every open
tenure (active *and* inactive, because an inactive tenure still occupies its club, `OCC-8`) with its club,
manager, and account, and advances each one to whichever rung the elapsed real time puts it on. It runs from
`world.evaluate-inactivity`, materialised once per UTC day by `InactivityScheduler` — a worker-only
`BackgroundService` following the pattern ADR-0003 and ADR-0018 established. The row's business key is the UTC
day, so a worker that was down at the boundary still runs the day's ladder late rather than skipping it.

**2. The thresholds are the rule set's, read against `IClock.UtcNow` and `LastActiveAt`.** The ladder is a fact
about elapsed time, not about how often the job happens to run, so a worker that missed a week catches up on
its next pass rather than skipping a rung. The branches are exclusive, highest first: close at 21 days, mark
inactive at 14 days (only from `Active`), warn at 10 days when no warning is outstanding.

**3. A login is the manager returning.** `Login` finds the manager's open tenure after a successful login and,
in the same unit of work as the session, calls `Resume(now)` when it is inactive (restoring control and
clearing the warning) or `RecordActivity(now)` otherwise (`OCC-2`, `OCC-5`). That is why an away manager
resumes full control by signing in and nothing else.

**4. A blocked account is not an absence.** A tenure whose account cannot authenticate (suspended, deleting)
is skipped by the ladder. It keeps its club (`OCC-5`), but the manager did not choose to be away and ending a
tenure the game itself blocked would punish the wrong party. Administrative suspension is Stage 14's to
resolve.

**5. The warning is sent once per lapse.** `ClubTenure.InactivityWarningAt` records it and `RecordActivity` /
`Resume` clear it, so a manager who returns and lapses again is warned again. The warning writes an inbox
message and, unless the manager has turned it off, an outbox email (`COM-4`, ADR-0029).

**6. The AI fills the tenure's gaps.** `IAiClubRepository.LoadAiClubsAsync` now includes clubs whose open
tenure is *inactive* (it previously excluded any open tenure), so `EvaluateAiClubs` supplies the tactics and
training an away manager lacks — and, because that evaluation only fills gaps, it never overwrites a choice
the manager already made (`OCC-2`, `INS-12`). The market is deliberately left alone: it commits money, which
is not a safe decision to make on someone's behalf.

## Consequences

**Positive**

- A country frees a place when its manager leaves, which is what lets the pyramid's occupancy rules mean
  anything; a closure re-runs `CapacityEvaluator` so the lowest tier grows the same way a claim that fills it
  does (`PYR-1`).
- The ladder is idempotent on the day and advances only from committed state, so at-least-once delivery is
  safe and a repeated pass on the same day writes nothing.
- The warning, the AI assistance, and the closure are all one transaction, so a manager never sees a club
  handed to the AI without the message explaining it.

**Negative**

- A pass loads the whole membership at once. It is bounded by the number of clubs in the world and runs once a
  day, so the cost is acceptable, but a much larger world would want paging.
- The ladder's daily cadence means a tenure that crosses a threshold between two passes is actioned at the
  next pass rather than exactly on the day. The elapsed-time comparison keeps the *rung* correct; only the
  timing of the write is coarse.
- An inactive tenure's club is set up by the AI but its market is not run, so a long absence leaves the squad
  legal but untraded. That is a deliberate asymmetry, not an oversight.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Evaluate per-request (lazily, when a club or country is read) | The ladder must run even when nobody is looking, and a read that wrote would make a GET a command; the plan routes it through the worker for exactly this reason (`ADR-0001`, `ADR-0008`). |
| A per-tenure scheduled job, one deadline each | A tenure is not a deadline; it moves whenever the manager acts. One membership pass derives the rung from `LastActiveAt` and cannot drift from a stale per-tenure row. |
| Age a suspended account as an absence | It would end a tenure for a reason the manager did not cause. `OCC-5` keeps the club; administrative suspension is a different concern with a different owner. |
| Record activity from the session rather than the login | Sessions are refreshed silently and often; a manager who never opens the game but keeps a refresh token alive would look present. The login is the honest signal. |
| Give the AI the market too, on an inactive tenure | The market commits money and transfers players. The safe half of `OCC-2` — tactics and training — is the one that cannot overwrite a present manager's plan; the market waits for the manager. |
