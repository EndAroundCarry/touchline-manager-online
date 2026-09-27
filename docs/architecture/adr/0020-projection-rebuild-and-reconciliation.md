# ADR-0020: Projections are reconciled and rebuilt by recomputing them from published results

- **Status:** Accepted
- **Date:** 2026-09-27
- **Stage:** 8
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0017](0017-match-load-at-publication.md), [ADR-0019](0019-engine-v2-and-season-statistics.md), master plan §5, §5.2, §7.2, §7.4, game rules `TBL-13`, `STA-1`, `STA-6`, `DIS-3`

## Context

Master plan §5 says a projection "is treated as a cache of a derivable fact", and §16 Stage 8 asks for
"projection rebuild and reconciliation tools". Two projections are in that position now: a division's table
and its players' season statistics. Both are written by the matchday publication inside its serializable
transaction, and both are derived from facts the world keeps — the published fixtures, and the match
snapshots, results, and events they were played from.

Before this decision nothing could recompute them. `Standing` could already be rebuilt from a whole line and
the publication did exactly that, but `PlayerSeasonStat` offered only `Open` and an incremental `Accumulate`,
so a stored line had no way back to a recomputed value. There was no read that reported what had drifted,
and no job that could repair it.

Four questions follow.

**1. What is the unit of work, and where does it execute.** A projection is written by the worker today
(`MAT-2`); a repair could be an operator command, a job, or an admin endpoint.

**2. What "reconcile" means beside "rebuild".** An operator wants to see what differs before authorising a
change, so the tool needs a read-only mode that is not a second, drifting definition of the expected value.

**3. Whether the read and the write are one transaction.** The publication guards itself by reading a whole
division and writing its projections inside one serializable transaction. A repair recomputes a whole
division too.

**4. What happens to a stored line that no published result supports**, and whether the discipline
accumulation is in scope.

## Decision

**1. One use case, two modes, running the same recomputation.** `RebuildDivisionProjections.ExecuteAsync`
takes a `divisionSeasonId` and an `apply` flag. It recomputes the expected table with
`StandingsCalculator.Rank` over the division's published outcomes, and the expected statistics with
`SeasonStatisticsCalculator` folded by `SeasonStatisticsCalculator.Aggregate` over its published results.
`apply: false` reports what differs, is missing, or has no source and writes nothing; `apply: true` writes
the corrections. Both modes run the same recomputation, so "what the projections should be" has exactly one
definition — the one the live publication already uses.

**2. A rebuild writes whole rows, not columns.** `PlayerSeasonStat.Create` opens a line already holding a
season's totals and `PlayerSeasonStat.Rebuild` replaces a stored line's totals wholesale, mirroring
`Standing.Rebuild`. The domain validates the recomputed line the way the database's checks and the aggregate
do: an appearance is required, a start is a subset of an appearance, shots on target a subset of shots, and a
season's rating total is at most ten thousand basis points per rated appearance.

**3. The apply reads and writes inside one serializable transaction.** The recomputation that decides the
corrections happens after `BeginTransactionAsync`, so a peer publication cannot commit a round between the
read and the write and leave the repair writing a stale answer. The dry run takes no transaction, because it
writes nothing.

**4. A stored line that no published result supports is removed.** `player_season_stats` has a unique key and
no other delete path, so a line written for a result that a later repair voided would otherwise persist. A
projection is a cache, so the rebuild removes a line whose player and club no published result names, leaving
the projection equal to its source rather than merely close to it.

**5. The discipline accumulation is not rebuilt.** Its card counts are derivable from the same events, but
the record's purpose is a suspension already served against specific fixtures. Replaying the accumulation
without replaying the service would leave a player's bookings and their absences disagreeing. That
reconciliation belongs with the rollover that owns the accumulation's reset (`DIS-3`), and it is deferred
rather than half-built (`§17.12`).

**6. It is a durable, worker-only job.** `competition.rebuild-division-projections` is keyed on the
division-season, so a repeated enqueue is a no-op and a retried delivery is idempotent. It has no HTTP
command, exactly as no other projection has one; the admin surface that would give it a button, a reason, and
an audit entry is Stage 14's.

## Consequences

**Positive**

- The table and the season statistics can be proven equal to what their results compute, which is the exit
  criterion Stage 8 names, and a drift can be corrected without editing a column.
- The repair uses the same arithmetic as the live path, so it cannot invent an answer the publication would
  not have produced, and the two definitions cannot diverge.
- The read-inside-the-transaction shape means a repair is safe against a concurrent publication, and the
  business key makes it safe against the queue's at-least-once delivery.
- Removing an unsupported line keeps a projection equal to its source rather than merely plausible.

**Negative**

- A repair that removes a line is destructive to a cache; it is justified only because the row is derived and
  the derivation is the authority (master plan §5), and it is bounded to lines no published result names.
- The discipline accumulation is left out, so a drifted card count is not repaired by this tool; that work is
  carried by Stage 12.
- There is no operator-facing entry point yet, so the job is reachable only by enqueuing it directly until the
  admin module lands.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A separate "check" read distinct from the rebuild | Two definitions of the expected value that can disagree — the very drift the tool exists to find. |
| A command an operator calls over HTTP now | Would need an operator role, a reason, and an audit entry that do not exist until Stage 14; a half-built admin surface is what `§17.12` keeps out. |
| Rebuild by re-simulating each published match | Defeats the stored, hashed result and would re-simulate under whatever engine build has since shipped (ADR-0014, ADR-0019). |
| Read the projections before the transaction and write inside it | A publication committing between the two would leave the repair writing a stale recomputation. |
| Keep an unsupported line and only correct the rest | Leaves the projection unequal to the live projection, which is the property the tool exists to provide. |
| Rebuild `discipline_records` in the same tool | Its consequence is a suspension already served; a replay that does not replay the service would create a new inconsistency to fix. |
| Give the rebuild a scheduler | A repair is not a deadline; there is no calendar for it to keep. It is enqueued when it is needed. |
