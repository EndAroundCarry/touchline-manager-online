# ADR-0014: A matchday is locked, resolved, and published by three jobs, and its snapshot is a stored document

- **Status:** Accepted
- **Date:** 2026-09-25
- **Stage:** 6
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0004](0004-deterministic-match-engine.md), [ADR-0009](0009-time-identity-and-concurrency.md), master plan §6.6, §7.2–§7.4, game rules `CAL-3`, `CAL-10`, `MAT-1`, `MAT-7`, `MAT-9`, `MAT-10`, `DIS-6`, `TBL-13`

## Context

Stage 6's last milestone turns the calendar and the engine into a season: a round locks its team sheets,
simulates its nine fixtures from frozen inputs, and publishes the results and the table together. Five
decisions in that work are expensive to reverse once a season is running.

**1. What the unit of work is.** The engine simulates one fixture; the rules publish nine at a time.
Between those two sizes sit the deadlines — the sheet lock is a property of the round
(`CAL-3`, `CAL-10`) — and the recovery question: a worker killed between two of the nine must leave a state
that a retry can finish, and one killed before publication must leave a state nobody can see.

**2. What a snapshot is on disk.** `MAT-1` requires the simulation to read only an immutable snapshot, and
`MAT-9` requires the result to be re-derivable from it. The engine's input is a versioned record with a
canonical text form, but it has nowhere to put the selection repairs `DIS-6` and `DIS-7` require, and it is
a record — not something a row can simply hold.

**3. Where the seed comes from.** Master plan §8.2 says the seed is derived with HMAC from a world secret.
No secret existed before this milestone, and a secret that ships as a default in a repository is not a
secret in the sense that matters.

**4. What happens when a club cannot field a side.** A club with no available goalkeeper has no legal
eleven: the engine refuses a side without a recognised goalkeeper, and it has no mechanism for a makeshift
one (Stage 5 recorded that as the correct shape rather than a gap). Something has to decide what the round
does about it.

**5. What "applied on publication" covers.** The stage's task list is broad — "table, stats, cards,
injuries, fatigue, morale, gate receipts, inbox events" — and the engine's output does not carry all of it.

## Decision

**1. One round is three jobs, each with the round's business key, and the lock and the resolution serialise
on a matchday-scoped advisory lock.**

`competition.lock-matchday` freezes every fixture's input at the sheet deadline; `competition.resolve-matchday`
simulates and stages at kickoff; `competition.publish-matchday` publishes once the round is staged. The
materialiser (`MatchdayScheduleScheduler`, worker-only, the `EnsureScheduleJobs` of §7.2) ensures the first
two for every open round inside its horizon and re-ensures the third for a round that is staged, which is
the recovery path for a round whose publication job was somehow lost. Business keys are derived from the
matchday's identity, so every re-derivation of the calendar is idempotent.

The lock and the resolution take `pg_advisory_xact_lock` on `matchday:{id}` for their critical sections.
In normal operation they are thirty minutes apart and never meet; they meet when the worker was down across
both deadlines, and there the lock is what stops two writers freezing the same fixture — which the snapshot
table would refuse as a uniqueness violation and the queue would retry. ADR-0003 names one
division-matchday publication as a genuine singleton; this is its key.

**2. The snapshot is a versioned document holding the engine's input and the repairs, with the engine's
input hash stored beside it and verified on every read.**

`MatchSnapshotDocument` writes `{ schema: "match-snapshot-v1", input, repairs }`, and the row carries the
content hash the seed was derived from, the seed, the commitment, and the input hash. `ReadVerified` reads
the document back, checks that the seed and commitment agree with the row, re-hashes the input, and refuses
a mismatch. The seed is therefore derived from a snapshot that is known to still be what it was, rather than
from whatever the document happens to say now.

Repairs are part of the document and carry their club, because a snapshot holds two sides and "slot 5 was
repaired" names two different players without it — and the manager a repair is reported to is the one whose
side it was.

**3. The world secret is configuration, validated at startup, with a development default.**

`World:MatchSeedSecret` joins `WorldOptions` with a minimum length. The default exists so a clone and the
test suite can freeze and simulate without configuration; a production deployment must set it, and rotating
it does not invalidate a played match, whose commitment was published from the seed it actually used. A
per-world secret belongs with multi-shard support, which is post-MVP.

**4. A club that cannot field a legal side refuses the round by name, and the round does not happen.**

Locking is all-or-nothing for the round: a club with no available goalkeeper dead-letters the job with the
club's identity in the message. There is no forfeit, no invented eleven, and no partial round — §7.4's
"do not invent forfeits" and the absence of any in-match keeper mechanism both point the same way, and an
operator repairing the club is the only honest path.

**5. Publication applies the table, and only the table.**

`PublishMatchday` publishes the nine fixtures and rebuilds the division's standings from its published
results in one serializable transaction, in that order, because the rebuild reads what the publication just
wrote. Player and club season statistics, discipline records and suspensions, injuries, condition, morale,
and gate receipts are deferred, and the reasons are specific rather than budgetary:

- The engine's player line has no assists and no rating, so a player-statistics projection would publish
  columns that could never be filled. Stage 7's match center and Stage 8's competition depth complete that
  contract, and the projection follows it.
- Injuries, condition, and morale need the engine to return state deltas, or a domain rule that decides
  them; `TRN-11` and `DIS-1` describe effects whose formulas live in the engine's specification, and the
  engine deliberately writes nothing back. Inventing a second definition here is the mistake ADR-0012
  refused for training injuries.
- Cards are counted from the match's events into the standings — the two columns `TBL-8` and `TBL-9` need —
  while accumulation into suspensions stays with Stage 8's discipline workflow.
- Gate receipts are Stage 9's finance module, which owns the ledger.

## Consequences

**Positive**

- A round is resumable at fixture granularity and invisible until it is complete: a crash after six of nine
  leaves six staged results and four to do, and no scoreline is public (`MAT-7`).
- A re-run of any of the three jobs is a no-op rather than an error, which is what at-least-once delivery
  requires; the tests assert it by running each one twice and counting rows.
- The stored snapshot reproduces its own hash before anything is simulated, so a result's provenance is
  checked rather than asserted.
- The table is rebuildable from published fixtures by construction (`TBL-13`), and the repair path is the
  same code as the live path.
- A world with no manager input at all still plays: a club with no plan fields the default formation with
  the neutral instructions, and its whole side is repaired deterministically and recorded.

**Negative**

- The advisory lock serialises the lock and the resolution of one round. Both are short and per-round, and
  the alternative is a race the snapshot table would refuse; the cost is one blocking wait in a recovery
  scenario.
- Publication rebuilds all 306 results of a division rather than incrementing the table. It is a handful of
  milliseconds three times a week, and incrementing is what lets a projection drift (`TBL-13`).
- A club with no available goalkeeper blocks its whole round until an operator acts. The alternative — a
  forfeit — decides a competitive outcome by a rule the game does not have.
- The staged state is a new failure surface: a round can sit staged with its publication job dead-lettered
  and nobody able to see its results. The materialiser re-ensures publication for staged rounds precisely to
  bound that window, and the job's own retry covers the rest.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| One job per fixture for lock and simulation | Loses the round as the unit: a partial round could publish, and the sheet-lock deadline is a property of the round (`CAL-10`, `MAT-7`). |
| One job for the whole round: lock, simulate, and publish | A crash mid-way leaves no resumable boundary, and the retry would re-simulate fixtures that were already staged (`MAT-9`'s idempotency would cover it, but at the cost of doing the expensive work again). |
| Simulate during the lock | The lock happens thirty minutes before kickoff; simulating then would move the result to a moment before the sheets it froze were due, and would publish a scoreline for a match that had not been played. |
| Store the snapshot as a canonical text blob only | The engine's canonical form is hashed, not parsed; without the document the workflow would have to re-derive the input from live tables, which is exactly what `MAT-1` forbids. |
| Let the resolver simulate from live tables when no snapshot exists | §7.3/§7.4: a delayed lock waits for a valid snapshot. The resolver therefore takes the snapshot itself rather than simulating something else. |
| Increment the standings on publication | Makes the projection a second source of truth that can drift from the fixtures; `TBL-13` requires rebuildability, which is also the repair path. |
| A forfeit for a club with no available goalkeeper | Decides a competitive outcome by a rule the game does not have, and hides a club that genuinely cannot play from the operator who could fix it. |
| Derive the seed from the fixture and the world seed already in configuration | `World:GenerationSeed` is a reproducibility input for world generation, not a secret, and it is visible in configuration; §8.2 asks for a secret specifically so nobody can influence a draw. |
