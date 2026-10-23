# ADR-0086: Matchday publications take turns, and a job that collides is run again at once

- **Status:** Accepted
- **Date:** 2026-10-23
- **Stage:** Fair squads plan (`create-a-plan-that-cheeky-dongarra.md`), found in Milestone 2
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0010](0010-club-takeover-serialisation.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0049](0049-non-production-stepped-game-clock.md), game rules `CONC-2`, `MAT-7`, `WORLD-1`

## Context

A new world now seeds tiers 1 and 2 in every country, so twelve divisions publish a round at the same kickoff instead of six (eighteen once Belgium, Portugal and the Netherlands are added). Publication is one serializable transaction (`CONC-2`, ADR-0014). The stepped-season test, which runs the worker at its production concurrency of four, started dead-lettering a `competition.publish-matchday` job on `40001: could not serialize access due to read/write dependencies among transactions`.

Two divisions share no table row. A probe recorded the predicate locks one publication leaves, and the tables it writes:

- It reads whole relations: `fans.club_fan_bases`, the `player_season_stats`, `discipline_records` and open-`player_unavailability` indexes. The table is small enough to be scanned, or the index is read by a range. Every publication writes to all four.
- It reads single pages of `finance.club_accounts`, `competition.standings`, `squad.player_state`, `squad.players` and `finance.sponsor_deals`. A world's few hundred clubs fit on one or a few pages of each, so every division's rows share them, and every publication updates them.

So the collisions are real at the granularity PostgreSQL checks, and they are not a missing index: a table of a few hundred rows is scanned, or held on one page, however it is indexed. They grow with the number of divisions.

Two things made them fatal rather than slow. The queue retries a transient failure with a backoff of 30 seconds or more, so in production a publication that lost a collision went public late. On a stepped clock it retries at once (ADR-0049), so the colliding jobs met again on every attempt and spent the budget of eight in seconds.

## Decision

**1. Publications take turns.** `PublishMatchday` holds `AdvisoryLockKey.MatchdayPublication()` before it reads anything or begins its transaction. `IAdvisoryLock.HoldAsync` takes a session-level `pg_advisory_lock` on the unit of work's own connection, which it opens for the purpose, and releases it when the handle is disposed. The transaction stays serializable, so `CONC-2` holds exactly as before.

- The lock is taken **before** `BEGIN` because a serializable transaction takes its snapshot at its first statement. A lock taken inside it would be waited for on a snapshot from before the wait (ADR-0010). Taken first, each publication reads what the previous one committed.
- A redelivered job for the same round waits behind the first and then finds it published (`AlreadyPublished`). Before, the two raced.
- One key for the deployment, since a deployment holds one world (`WORLD-1`), and the turn is taken before the matchday is read.
- A session lock outlives its transaction, which ADR-0005 avoided. This one is bounded: the handle unlocks in its dispose (not cancellable), a pooled connection is reset with `DISCARD ALL` before it is reused, and a crashed process's connection closes and releases it.

**2. A job that collides is run again at once.** `JobQueueWorker` reruns a handler that failed with a serialization failure (`40001`) or a deadlock (`40P01`), found anywhere in the exception chain by `SerializationConflict.IsConflict`. It reruns up to five times inside the same attempt, after a random pause of up to 400 ms times the rerun number, each time in a fresh scope. The failed attempt's unit of work holds entities from a rolled-back transaction and must not be reused. Handlers are already idempotent because the queue delivers at least once, so a rerun is what a queue retry would have done, without the backoff or the attempt. Only when the reruns are spent does the failure go to the queue as transient. This covers the other serializable work (`ResolveListing`, `RebuildDivisionProjections`) and anything that deadlocks.

The rerun alone was tried first and was not enough: four publications kept rolling each other back on every rerun, and round 1 did not finish publishing in 45 seconds. Taking turns removes the cause; the rerun handles the collisions that remain with other jobs.

## Consequences

- A round's results go public one division after another, not all in the same instant. A publication took well under a second in the tests, so eighteen divisions publish within seconds of each other, against a 30-second backoff for each loser before.
- Other jobs keep running in parallel. A publication waiting its turn occupies one of the worker's four slots.
- `ConcurrentPublicationTests` publishes four divisions' round 1 at once through the use case, with no worker to rerun a collision, and a redelivered round twice. Both fail with `40001` without the turn. `JobQueueWorkerTests` covers a job that collides twice and then completes within its first attempt, and one that keeps colliding and goes back to the queue as transient.
- The stepped-season test runs at the production concurrency of four again.
