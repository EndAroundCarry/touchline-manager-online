# Changelog

Notable changes by stage. The stage numbering follows
[`docs/product/master-plan.md`](docs/product/master-plan.md) §16.

## Stage 6 — Season schedule, fixtures, and the matchday worker

A season you can see the shape of. Seeding the world now generates each division's full fixture list: the
nine matches of round one through the nine of round thirty-four, played on Tuesdays, Thursdays, and
Sundays, with the team-sheet lock derived from each kickoff. The first milestone of the stage is the
calendar and the two tables it lives in; the lock-and-snapshot workflow, the durable matchday worker, the
staged simulation, and atomic publication follow.

### Added

- **`competition.matchdays` and `competition.fixtures`.** A matchday is one round of one division's season
  — the nine fixtures that lock, resolve, and publish as a unit (`CAL-10`, `MAT-7`) — and a fixture is one
  match with the score it publishes. The database carries the rules rather than a convention: the round
  number is bounded to 34 (`CAL-1`), the lock must precede the kickoff it is derived from (`CAL-3`), a club
  cannot play itself, a score together with its match exists *exactly* in the `staged` and `published`
  states so a half-resolved fixture is impossible, and `published_at` agrees with the published status. A
  club appears once per matchday as host and once as visitor through two matchday-scoped unique indexes,
  and the round number is unique per division-season.
- **The `Matchday` and `Fixture` aggregates with their lifecycles as behaviour.** Five fixture states —
  scheduled, locked, simulating, staged, published — plus `void` for an operator removal (`MAT-10`). Every
  transition guards itself and every repeat of a transition that has already happened is a no-op, because
  the workflow that drives them runs from a durable job that may be retried at any boundary (§7.4,
  ADR-0003). A matchday marks itself staged once every fixture has, and publishes only from staged.
- **`RoundRobinSchedule`**: the fixture list generator (`CAL-8`). The circle (Berger) method builds one
  single round-robin — every club meets every other exactly once, and plays exactly once a round — and the
  second half replays it with the venues swapped, which makes "each pair meets once home and once away" and
  a club's seventeen home and seventeen away fixtures true by construction rather than by a correction
  pass. The clubs are shuffled by a `Pcg32` stream seeded from the division-season's stored schedule seed,
  so the same seed reproduces the same list. Home and away are decided round by round, alternating a club's
  venue wherever the pairing allows; the second half is emitted in *reverse* round order, so the last
  first-half round and the first second-half round are the same pairing with opposite venues and a run
  never crosses the halfway point.
- **`ScheduleValidator`**: the `CAL-9` properties checked by name before a schedule is written — round
  count, one fixture per club per round, each ordered pairing once, each pair reciprocated, equal home and
  away counts, and a bounded run of games at one venue. The generator is built so they hold; this is what
  turns "by construction" into evidence, and a malformed schedule fails generation instead of becoming a
  season that quietly cannot be played correctly.
- **The seeder generates the schedule.** Running `npm run seed` now creates 34 matchdays and 306 fixtures
  per division — 1,836 matches across the six countries — with each matchday's lock thirty minutes before
  its kickoff and every fixture still `scheduled`. The clubs are passed in identity-generation order, the
  only stable order there is: ids are UUIDv7 and differ per run, so the fixture list is keyed on that order
  plus the recorded seed.
- **`WorldRuleSet.MaxConsecutiveHomeOrAway`** (`CAL-9`), and the rule set is now `world-rules-v4`, as a
  stage's constants arrive with that stage (`RULE-1`). A world stamped with an earlier version keeps being
  read against it.
- 35 new domain tests (296 total): the schedule's properties across every plausible division size and
  fifty seeds, its determinism and seed-sensitivity, its refusals, and every fixture and matchday
  transition including the idempotent repeats; and 6 new infrastructure tests over real PostgreSQL,
  including two that force a malformed row and assert the check constraint by name (`MIG-7`).

### Notes

- **The run of games at one venue is four, not three, and the constant is fitted to the schedule rather
  than the schedule to the constant.** The first attempt alternated venues by round and position parity and
  left four-game runs; alternating on the previous round's venue and emitting the second half in reverse
  cut the maximum to three for small divisions but still produced four at 18 and 20 clubs. Four is a normal
  home stand in a real fixture list, so `CAL-9`'s "acceptable" is met at four, and the property tests
  demonstrate the bound holds rather than aspiring to it.
- **The second half is the first half reversed, and the reversal is doing real work.** Mirroring in the
  same order lets a run straddle the halfway point; mirroring in reverse order makes round 17 and round 18
  the same pairing at opposite venues, so the boundary always alternates and a run inside the first half
  maps to an identical run inside the second. The maximum run over the whole season is therefore the first
  half's, which is what makes the bound provable rather than measured.
- **A fixture's kickoff is denormalized from its matchday.** The fixture list and the countdown both read
  it, and a list query wants it on the fixture's own row. It is written once, from the matchday, and never
  moved — `CAL-11` forbids shifting a scheduled kickoff because simulation was late.
- **The `squad.fixture_team_sheets.fixture_id` foreign key is deliberately still absent.** Stage 4 shipped
  the shell without it, noting that "Stage 6 adds it", and Stage 6 does — but only once a write path
  produces team sheets for real fixtures, which is the prepare-match milestone. Adding it now would force
  an unrelated squad test to arrange a fixture in the same commit, which §17.15 forbids.
- **`WorldBootstrapGenerator.Version` records `world-gen-v3`.** The bootstrap now produces clubs, squads,
  *and* the fixture list, so the run that emits them names the version that covers all three (`FIC-8`,
  `PYR-14`).
- **The schedule generator versions itself separately** (`schedule-gen-v1`) and its version is folded into
  a world run's input hash, because changing how a fixture list is drawn is a reproducibility fact
  independent of how clubs or players are generated.
- **The database's share of `CAL-9` is smaller than the rule.** "One fixture per club per matchday" cannot
  be expressed across two columns, so the two matchday-scoped unique indexes catch a club hosting twice or
  visiting twice, and the full property — one match per club per round, each pair once each way — remains a
  generation-time check, exactly as master plan §6.4 allows ("generation validation plus database support
  where practical").
- **Deferred to the rest of Stage 6:** the fixture and matchday reads with the next-fixture dashboard and
  the prepare-match screen (both delivered in the next milestone, below), the durable job queue's full
  lock/snapshot workflow, the immutable snapshot and the seeded commitment hash, the staged simulation and
  atomic nine-fixture publication, the standings and statistics projections, and the compressed test clock.
  **Deferred beyond it:** rollover, lower-tier provisioning, and the match viewer.

### The fixture list and the prepare-match screen

A season you can read and a side you can prepare. `GET /fixtures/mine` answers the dashboard and the
fixtures screen with the club's thirty-four fixtures and the next one named, `GET /divisions/{id}/fixtures`
answers the division's whole calendar in thirty-four rounds, and the prepare-match screen reads one fixture
in full and saves a club's selection for it under the sheet's version. This is the second milestone of
Stage 6; the lock-and-snapshot workflow, the durable matchday worker, the staged simulation and atomic
publication, and the projections follow.

#### Added

- **The fixture reads** (master plan §10.5, §11.1): `GET /divisions/{divisionId}/fixtures` — a division's
  whole calendar, the 306 fixtures of a season grouped into the 34 matchdays that lock and publish as a unit
  (`CAL-10`), with its eighteen clubs carried once so the fixtures reference them by identity — plus
  `GET /fixtures/mine`, the manager's own club's season with `NextFixtureId` named, and
  `GET /fixtures/{fixtureId}`, one fixture in full with `ManagedClubId` and `ManagedSide` resolving the
  caller's end. Fixtures are public game data, so none of the three is gated on holding a club; what the
  tenure adds is which side is theirs.
- **A score is public only once its matchday has published** (`MAT-7`, `CAL-10`). A staged fixture's row
  already carries the score and the match — that is what staging means — but the mapping publishes both only
  from `published`, so a half-resolved round cannot leak one of its nine results through a read.
- **`IsLocked`, from the deadline and the status together** (`CAL-3`, `SQ-7`). A fixture's sheet is closed
  when its status has moved past `scheduled` *or* when its lock instant has passed, because the deadline is a
  rule and the status is bookkeeping: a delayed lock job must not leave a sheet editable a minute after it
  was due.
- **The fixture team sheet** (master plan §10.4): `GET /fixtures/{fixtureId}/team-sheet` — the opponent and
  the deadline, the plan the side is prepared from, all eighteen slots filled or empty, and the squad it may
  pick from, in one response — plus `PUT`, which replaces the whole selection. The club, the fixture, the
  plan version the sheet references, and the deadline all come from the server, and none of them is taken
  from the request (`INT-1`).
- **`FixtureTeamSheetValidator`** (`SQ-4`, `SQ-9`, `TRN-12`): one pure rule over the submitted selection and
  the club's selectable and unavailable players, refusing a slot number outside 1–18, a repeated slot, a
  repeated player, a player who is not selectable, an unavailable player, and a starting eleven that is not
  all picked. It is the same rule the Stage 8 snapshot builder will repair a locked sheet through
  (`INS-12`), and it is shared with the screen as a validation preview of stable codes rather than a field
  message.
- **The sheet's version is its strong entity tag** (`CONC-1`, ADR-0009). A replace requires `If-Match` —
  answered `428` without it — and a stale one is answered `412`, so a side prepared on one device cannot be
  silently overwritten on another. The first save for a fixture carries no tag, because there is nothing to
  be conditional against yet, and `squad.fixture_team_sheets.version` is now a concurrency token, so a raced
  save is refused by the database and not only by the use case's own comparison.
- **The two foreign keys Stage 4 shipped without** (ADR-0011): `squad.fixture_team_sheets.fixture_id` and
  `squad.player_unavailability.source_fixture_id` both now reference `competition.fixtures`, in one
  migration applied against real PostgreSQL 17. Stage 4 recorded that Stage 6 would add them once a write
  path produced a team sheet for a real fixture; the prepare-match screen is that write path. The second is
  nullable on purpose, because an absence can come from training rather than from a match (`TRN-12`).
- **The `/fixtures` screen and the prepare-match screen** (§11.1): the club's season split into what is still
  to play and what has been played, with the next fixture called out and a countdown to its deadline on a
  slow interval rather than a one-off render; and the prepare screen, where the eleven starting slots take
  their family and role from the club's plan and the bench is seven more places. Every slot is a labelled
  select rather than a drag, so the screen is reachable from a keyboard and a screen reader (§11.3), and a
  refused save lists the validator's issues against the slots they concern. When the fixture has locked the
  controls are disabled and the screen says so; when the club has no default plan it points at the tactics
  screen instead.
- **The dashboard's next-fixture card**, with the opponent, the kickoff, the deadline, and the countdown, and
  the `/fixtures` navigation destination flipped to available. A failure to read the fixtures leaves the rest
  of the dashboard intact rather than blanking it, because the card is a second read of a different resource.
- 9 new domain tests (305 total) for the team-sheet validator; 6 new API integration tests (68 total)
  covering the calendar and the club's list, the prepare screen's read, the create/replace lifecycle with its
  `428`/`412` refusals, the validation preview, and the other-club and no-club refusals; a new infrastructure
  test over real PostgreSQL for the foreign key by name (`MIG-7`), beside the team-sheet round trip, which
  now arranges a real fixture instead of a random one; 21 new frontend tests (145 total) over the
  presentation helpers and the store's concurrency contract; and 2 new Playwright journeys (20 total) —
  the prepare screen with its version conflict survived by reapplying, and the guard for a visitor with no
  session.

#### Notes

- **The idempotency of publication is enforced in the mapping, not in the query.** A staged row genuinely
  holds its score; what keeps it private is that `FixtureMapping.Visible` publishes the score, the match, and
  the outcome only from `published`. That is one place to read when the question is "can a manager see this
  yet", and it is the same predicate the Stage 7 viewer will need.
- **The reapply path had to read before it wrote, and the browser journey is what found it.** A `412`
  reloads the sheet asynchronously, and the first version of the store reapplied against whatever version it
  happened to hold — so a manager who clicked *Reapply my changes* the moment the conflict appeared re-sent
  the version that had just been refused and was refused again, with no way out but a page reload. Reapply
  now reads the sheet and conditions the save on what that read returns. The store's unit tests did not
  catch it because they ran the reload to completion first; the journey clicked when a person would.
- **A fixture's own row carries its kickoff as well as its matchday's.** It was written once from the
  matchday when the schedule was generated, and it is what the club's list reads; the matchday's own instant
  is still what a round-level read reports.
- **The season a read is measured against is the world's current one**, resolved once by
  `CurrentSeasonQuery`, so a read during the rollover window keeps answering for the season being played
  rather than for the next one whose rows already exist (`CAL-6`).
- **A replacement selection is a delete and an insert, not a set of edits.** Swapping two players between two
  slots cannot be expressed as single-slot updates without passing through a state that violates one of the
  sheet's unique indexes, so the use case removes the previous entries and adds the new ones and the two
  commit in one transaction. The API test that replaces a whole side with a different one is what proves the
  ordering is safe against real PostgreSQL.
- **The plan a sheet is described by is the sheet's own when one exists, and the club's default otherwise.**
  A stored selection is always described by the shape it was prepared against, and a fresh screen shows the
  shape a new save would use; a save rebases the sheet onto the current default's version, which is what
  `FixtureTeamSheet.Rebase` was added for in Stage 4.
- **The bench's size needs no rule of its own.** `SQ-4` asks for "up to seven substitutes" and nothing about
  which numbers they take, and slots 12–18 are exactly seven, so a set of distinct slot numbers cannot name
  more. The validator deliberately adds no contiguity rule: it would refuse a legal side for a reason the
  rules do not give.
- **`GET /divisions/{divisionId}/table` is not in this milestone.** §10.5 lists it, but the standings
  projection is the rest of Stage 6; a table read with nothing behind it would be an empty screen pretending
  to be a feature.
- **The match viewer, `GET /matches/{id}`, is likewise not here.** A fixture names its match once a result is
  staged, and nothing produces one yet: simulation and publication are the next milestone.
- **The fixtures screen polls nothing and the countdown is the only thing that moves.** A calendar changes
  once a matchday publishes, and that is a moment the manager is already on the screen for; a 30-second
  interval re-renders the countdown only.
- **Deferred to the rest of Stage 6:** the durable job queue's materialiser and lock/snapshot workflow, the
  immutable input snapshot with its seed commitment hash, the staged simulation and atomic nine-fixture
  publication, the standings and statistics projections, and the compressed test clock. **Deferred beyond
  it:** rollover, lower-tier provisioning, and the match viewer.

### The durable matchday worker, the frozen snapshot, and the table

A matchday that plays itself. The calendar's deadlines become durable jobs, each round freezes both clubs
into one immutable input before it kicks off, the engine simulates it from that input alone, and the nine
results become public together with the table they move. Three jobs, three business keys, and no way for a
client to influence any of it.

#### Added

- **The `match` module's four tables** (`MAT-1`, `MAT-9`, master plan §6.6): `match.input_snapshots` (one
  per fixture, immutable), `match.matches` (one per fixture, carrying both hashes and the versioned
  statistics document), `match.events` (the durable narrative, `unique (match_id, sequence)`), and
  `match.simulation_attempts` (every attempt, successful or not, `unique (fixture_id, attempt_number)`). The
  constraints carry the rules: a fixture is frozen once, simulated once, and an attempt number happens once,
  so at-least-once delivery cannot produce a second row of any of them.
- **`InputSnapshot`, `SimulatedMatch`, `SimulationAttempt`, and `MatchEvent`**: the match module's
  aggregates, each with a factory that validates and no mutator at all, because a frozen input and a played
  result are history (ADR-0014). `MatchEventType` mirrors the engine's event vocabulary by value, with the
  stable codes the domain needed and the engine did not, and two of them — "a second yellow is a sending-off,
  not a second yellow" and "a penalty goal is a goal" — are the definitions the table's card columns and the
  score reconciliation read.
- **`MatchSnapshotBuilder`**: a pure function from two prepared sides to one frozen input, with the
  deterministic repair `DIS-6` asks for. A club's sheet is honoured slot by slot; a slot it left empty, or
  filled with somebody who cannot play in it, is decided by position suitability, then condition, then
  ability, then the player's identity. The eleven must contain exactly one recognised goalkeeper, which is
  why a goalkeeper is never placed outfield and an outfield player is never placed in goal; the bench is
  filled to seven with at most one goalkeeper, so an untended club's match is a match. Every decision is
  recorded with its club, its slot, the player who was dropped, and why (`DIS-7`).
- **A club with no saved plan takes the field in the default formation with the neutral instructions.** The
  seed world still ships no tactical plans for its AI clubs, and this is what makes the game playable before
  a single manager has opened the tactics screen — and what `TeamInstructionSet.Neutral` exists for, since
  the enums' zero values are the extremes rather than the middle. The full AI lineup and tactic policy is
  Stage 8's, and it will replace this one selection rule without touching the workflow.
- **`MatchSnapshotDocument` and `MatchStatisticsDocument`** (`match-snapshot-v1`,
  `match-statistics-v1`): the stored documents, each with a schema discriminator that reading refuses to do
  without (§4.5). The snapshot document holds the engine's input *and* the repairs, because §6.6 requires
  both and the engine's contract has nowhere to put a repair; its attributes round-trip through the engine's
  own validating factory, so an edited document is refused rather than simulated.
- **`MatchSnapshotFactory`**: the one place a snapshot is built — content hash, seed derived by HMAC from
  `World:MatchSeedSecret`, commitment, document, input hash — and the one place it is read back.
  `ReadVerified` re-checks the seed, the commitment, and the input hash before returning the input, so a
  result can only be produced from a snapshot that is provably still what it was (`MAT-9`).
- **The three matchday jobs** (`competition.lock-matchday`, `competition.resolve-matchday`,
  `competition.publish-matchday`) with business keys derived from the matchday's identity, and
  `MatchdayScheduleScheduler`: the materialiser §7.2 calls `EnsureScheduleJobs`, which turns the calendar
  into work. It is worker-only, holds no deadline of its own, and re-derives everything from the clock on
  every pass, so a restart, a redeploy, and a scale-down all produce the same rows (ADR-0003).
- **`LockMatchday`**: one transaction and one matchday-scoped advisory lock for the whole round. It freezes
  each fixture's input, freezes the club's prepared sheet, and marks the fixture locked, so the round is
  either frozen or untouched — a half-locked round would leave some managers able to edit a side and others
  not, for no reason the rules give.
- **`ResolveMatchday`**: per-fixture transactions, so a worker killed after six of nine leaves six staged
  results that a retry finds and leaves alone. Each fixture is marked simulating before the engine is
  called, so a run that dies mid-simulation is visible as one that started. A fixture whose lock never ran
  has its snapshot taken here rather than being simulated from live tables (§7.3), and the fixture's lock
  state and its snapshot commit together. When all nine are staged, the round marks itself staged and
  enqueues publication in the same transaction.
- **`PublishMatchday`**: one serializable transaction that publishes the nine fixtures, rebuilds the
  division's table from its published results, and marks the round published. A round that is not fully
  staged publishes nothing and moves nothing (`MAT-7`), and the repair path for a table is the same code as
  the live one (`TBL-13`).
- **`StandingsCalculator` and `Standing`**: the ordering rules of `TBL-1`–`TBL-12` as one pure function
  over published results, plus the projection row the table is stored in. The last tie-break is the draw key
  derived from the seed the division recorded before the season (`TBL-10`, `TBL-11`), and a club identity is
  compared beyond it only so the order is total. Cards come from the match's events, which is why
  `TBL-8`/`TBL-9` need no second accumulator.
- **The seeded world opens with a table**: eighteen rows per division, at nil-nil, ordered by the same draw
  the season committed to, so a manager who signs in before the first ball is kicked sees a table rather
  than an empty screen — and sees the same order the first publication will keep.
- **`GET /divisions/{divisionId}/table`** (§10.5): the division's table for the season in progress, public
  game data like the calendar, in the order the projection stored, with the server's own instant so a client
  with a wrong clock cannot mislead a manager about when it was read (`TIME-5`).
- 24 new domain tests (329 total): the table's ordering rules with every criterion tested by a construction
  that leaves all the earlier ones level — including the head-to-head group rule and the case where goal
  difference and goals scored disagree — and the match aggregates' guards, codes, and immutability.
- 29 new application tests (52 total) for the snapshot builder and the documents: the repair order and each
  repair reason, the one-goalkeeper rule, the bench, determinism across builds, the refusal of a club with
  no goalkeeper, the round trip that reproduces the input hash, and the vocabulary mapping that keeps the
  two enums' values equal.
- 7 new infrastructure tests over a seeded world in their own container (106 total): the lock's freezing and
  its idempotency, the resolution's staging and re-run, a round interrupted mid-simulation resuming without
  a lost or duplicated result, a re-simulation from the stored snapshot reproducing the output hash, a
  partial round publishing nothing, and a published round moving the table to exactly the ranking the
  published results compute.
- 2 new API integration tests (70 total) for the table read, its public access, and its unknown-division
  refusal; and 1 new worker integration test (4 total) that runs a whole matchday through the real
  composition: the scheduler materialises it, the queue claims it, the handlers lock, simulate, and publish,
  and all three jobs reach a terminal state.

#### Notes

- **The lock and the resolution serialise on a matchday-scoped advisory lock, and the test that found it is
  the one that made both due at once.** They are thirty minutes apart in normal operation and never meet;
  they meet when the worker was down across both deadlines and comes back to two overdue jobs. The
  integration test arranged exactly that and the two raced: the resolver takes the snapshot itself when the
  lock job has not run, so both tried to freeze the same fixture and one died on the snapshot table's unique
  index. ADR-0003 names one division-matchday publication as a genuine singleton, so the fix was the lock
  the ADR already called for rather than a new mechanism.
- **The resolver's snapshot and the fixture's lock state commit together, and that is a fix to a bug the
  first version had.** A retried resolution that found an existing snapshot skipped the fixture's
  `Lock` transition, and a crash between the two writes would then have left a fixture that was still
  `scheduled` with a frozen input — which `Stage` refuses. The ensure step now locks the fixture whenever it
  is still scheduled, so the pair is written or neither is.
- **The bench's slot numbers were burning a shirt number per passed-over goalkeeper.** The first version
  iterated the free slot numbers from the same enumerator that decided whether to add a candidate, so
  skipping a second goalkeeper consumed a number without recording a repair — and the integration test's
  repair count came back as 34 or 35 per fixture instead of 36. The count is now asserted exactly, which is
  what caught it.
- **`MatchEventTypes.MaxCodeLength` was sixteen and `second_yellow_card` is eighteen.** The column was
  sized from the constant, so the first staging attempt failed with `value too long for type character
  varying(16)` — a reminder that a "longest code" constant is a claim about the data and belongs in a test
  that walks the enum, which `Every_stored_code_round_trips` does.
- **The standings are rebuilt rather than incremented, and the publication reads its own writes.** The
  transaction publishes the nine fixtures, commits, reads the division's published results back, and ranks
  them from scratch, so the projection cannot drift from the fixtures it summarises. It costs a few
  milliseconds three times a week.
- **A snapshot's repairs carry their club.** The first version did not, and a repair list for one fixture
  holding two sides is ambiguous at slot level: "slot 5 was repaired" names two different players. The
  document carries the club for the same reason, and so does the inbox item `DIS-7` will eventually send.
- **A club with no available goalkeeper refuses its round by name.** Locking is all-or-nothing, the job
  dead-letters with the club's identity, and the round does not happen until an operator repairs it. That is
  deliberate: a forfeit would decide a competitive outcome by a rule the game does not have, and Stage 5
  already recorded that the engine has no mechanism for a makeshift keeper.
- **Nothing here is reachable from a command.** There is no endpoint that locks, simulates, or publishes:
  the three use cases are driven by jobs, and the only public surface this milestone adds is a read
  (`MAT-2`).
- **Deferred to the rest of Stage 6:** the compressed test clock, and the web table screen that reads the
  new endpoint. **Deferred to other stages, with reasons:** player and club season statistics (the engine's
  player line has no assists and no rating, so the projection would publish columns that could never be
  filled — Stage 7 completes that contract), discipline records and suspensions, injuries, condition and
  morale (all need the engine to return state deltas or a rule that decides them, which is Stage 8's subject
  — ADR-0012 made the same call for training injuries), gate receipts (Stage 9 owns the ledger), the inbox
  (`DIS-7`'s report needs the inbox to exist), the highlight table (Stage 7's viewer is its first consumer),
  and rollover, lower-tier provisioning, and the match viewer.

## Stage 5 — The pure match engine

A match you can replay. `MatchSimulator.Simulate` takes one frozen snapshot and returns one result, and the
same snapshot always returns the same result down to the byte — not because the engine is careful, but
because there is nothing in it that could vary. It reads no clock, database, network, filesystem, culture,
or `Random.Shared`, and it names itself in the result so a scoreline can always be explained by the rules
that produced it. Version 1 is `engine-v1` / `engine-rules-v1`, and its formulas, constants, and measured
distributions are specified in [`docs/product/match-engine.md`](docs/product/match-engine.md).

### Added

- **The engine's own `Pcg32`** (`Randomness/Pcg32.cs`), a pinned XSH-RR implementation with its golden
  sequence asserted, plus `MatchSeed`, which derives the secret seed by HMAC-SHA256 over the world secret,
  the fixture, the snapshot's content hash, and the engine version (`MAT-9`, master plan §8.2). The content
  hash deliberately excludes the seed, because hashing a seed into its own derivation is circular; the
  full input hash — facts plus seed — is what gets stored. `CommitmentOf` produces the publishable
  commitment, so the raw seed can stay protected and still be verified later (`MAT-10`, `MAT-11`).
- **`EngineRulesV1`**: every tunable the engine has, as one validated, versioned, immutable record. Its
  canonical description is derived by reflection over its own properties rather than written out by hand,
  so a new constant cannot be silently omitted from the hash a result records — the failure mode of a
  hand-maintained list is that two genuinely different configurations claim the same provenance.
  `EngineConfiguration.HashOf` is what a snapshot is frozen against, and the engine refuses to simulate a
  snapshot whose configuration hash does not match the rules supplied.
- **The input and output contracts** (master plan §8.3): `MatchInputV1` with `MatchSideV1`,
  `MatchParticipantV1`, `MatchSlotV1`, and `MatchInstructionsV1`; `EngineEventV1` as a flat, ordered,
  fact-only event; `MatchResultV1` with statistics, player lines, and both hashes. `Validate` refuses a
  snapshot by name — a lineup of ten, a slot at an off-pitch or duplicated coordinate, a role that
  disagrees with its family, a side fielding two recognised goalkeepers, a participant from another club,
  and a dozen more — because a malformed snapshot that simulated anyway would produce a plausible result
  indistinguishable from a real one.
- **`CanonicalMatchSerializer`**, and with it the three digests: content (the facts, seed excluded), input
  (the facts and the seed), and output (the result, bound to its input hash). Collections are sorted,
  numbers are formatted invariantly, and every field is labelled on its own line, so a value cannot change
  position unnoticed and two adjacent numbers cannot be read as one.
- **The rating model** (master plan §8.4): `UnitRatingWeights` with a versioned table per unit that checks
  itself the first time it is read; `UnitRatingCalculator` over nine units; `TacticalModifiers`; and
  `LineupResolver`, which resolves each slot's occupant and their role familiarity once at kickoff.
  `INS-10`'s out-of-position penalty is a single definition, so a makeshift side is priced consistently by
  the ratings and by cohesion.
- **The simulation**: a possession-based model in a documented phase order — the defending side's foul,
  then progression out of build-up, then creation, then the chance. Goals come only out of resolved chances
  (`MAT-4`), the shooter is drawn by the attribute the chance asks for, and the penalty taker is the best
  finisher on the pitch rather than a draw. `DisciplineSimulator`, `InjurySimulator`, and
  `SubstitutionPlanner` cover the rest, and `MatchResultBuilder` derives every statistic from the event
  stream so `MAT-5`'s reconciliation holds by construction.
- **Commentary tokens** (`commentary-v1`): a template key, the facts, a variant key, and the English text.
  The key and parameters are the durable part, which is what makes the same match narratable in another
  language later without re-simulating it. Three variants per template are chosen by event sequence, so a
  long match does not read as one sentence repeated and a change to a sentence cannot change a result.
- **Semantic highlights** (`highlights-v1`): every goal and every penalty always shown, then the best
  chances by the goal probability they were resolved against. 22 player entities plus the ball, one track
  per entity holding normalized keyframes, a 5–8 second duration, and an accessible narration. Two caps —
  a count and a 750 KB payload budget — shed the lowest-quality highlights first and never a goal.
- **`docs/product/match-engine.md`**: the executable specification for version 1 — the two arithmetic
  scales, the draw-order contract, every formula, every constant with its value, the measured
  distributions, and a map of which test suite pins what.
- **ADR-0013**, on integer basis-point arithmetic and the bounded scoreline effect.
- **The simulation laboratory** (`tools/simulation-benchmarks`), which was a `Hello, World!` stub: one
  match with its hashes, a distribution table over any number of matches with target bands, and a timing
  run reporting p50/p95/p99, allocation per match, throughput, and the hardware it ran on.
- 521 engine tests, up from zero, covering the pinned PRNG sequence, all 10,935 instruction combinations,
  the golden output hash, twenty named input refusals, the statistical distributions, and the engine's
  purity by reflection.

### Notes

- **There is no floating-point number anywhere in the engine** (ADR-0013). A rating scale of 0–100 — one
  attribute point is five units — and a basis-point scale of 0–10,000 carry every formula, and two
  probabilities compose exactly as `a * b / 10_000` in integer arithmetic. Floating-point is reproducible
  on one binary, but .NET makes no cross-platform guarantee for the transcendental functions, and an
  engine whose value is that a result is re-derivable anywhere cannot rest on that.
- **The score distribution needed a mechanism, not a coefficient.** With independent goals the model is
  close to Poisson, and the measurements said so: a mean of 3.15 and **4.2%** of matches with seven or more
  goals, against football's roughly 2.5%. Lowering the mean to 2.84 fixed most of it; a bounded,
  score-derived creation modifier — a side three goals up stops chasing a fourth — fixed the rest, and the
  measured tail is now 2.59%. `GameStateModifier` is a pure function of the score that consumes no draw, so
  it cannot drift, and it is capped so a rout stays a rout.
- **The bounded modifier alone was not enough, and the first attempt at it barely moved anything.** It
  shifted creation between the sides without reducing the total, which is what the measurement showed the
  moment it was run. The honest summary is that the mean sets the tail and the modifier shapes it; both
  were needed and the proportions were measured rather than reasoned about.
- **A shot event carries the goal probability it was resolved against.** Without a quality signal a save
  from three yards and a save from thirty are identical in the event stream, and "notable saves above a
  configured threshold" is not expressible. It is a fact about a shot, derived from attributes the owning
  manager can already see — emphatically not a hidden player value — but it is also not something a
  player-facing response may carry (`MAT-11`), so the commentary tests hold an allowlist of parameter names
  and the contracts assembly keeps its data-classification guard.
- **The validation caught two real bugs while the engine was being built.** Home advantage was written as
  1,030 basis points rather than 10,300 — a 3% multiplier entered as a 0.103 one — and a subtractive
  cohesion penalty was filed among the multipliers. Both were refused at startup by the rules' own
  validator, which is the argument for validating a configuration rather than trusting it.
- **`FoulShareOfTurnoverBasisPoints` and `AggressiveTacklingCardMultiplierBasisPoints` were removed or
  wired up before the stage closed.** The first was superseded by rolling fouls independently of
  progression and nothing read it; the second existed but the booking chance was using the *foul*
  multiplier. Bookings now scale separately from fouls, because committing more fouls is not the same as
  committing worse ones, and the aggressive setting is a genuine disciplinary risk rather than merely a
  busier one.
- **The engine defines its own vocabulary rather than reusing the domain's enums.** The numeric values
  mirror `AttributeName`, `PlayerPosition`, `PlayerRole`, and the eight instructions deliberately, and the
  application layer will map between them by value — but the engine may not depend on `Domain` (`DEP-2`,
  ADR-0004), because a generated world's reproducibility and a played match's reproducibility are separate
  versioned contracts.
- **A side whose goalkeeper is sent off has no player in the Goalkeeping unit**, so every shot against them
  is close to a formality. That is the correct shape rather than a gap: `MAT-6` has no mechanism for naming
  a new goalkeeper mid-match, and a side that loses theirs is in trouble.
- **The most aggressive possible instructions are tested over every seed**, because the degraded paths are
  the ones nobody exercises: aggressive tackling maximises sendings-off, a high press maximises injuries,
  and a side can end up with a goalkeeper outfield and a rating built from an empty band.
- **Performance is not a problem yet, and the budget is recorded rather than targeted.** p95 is 3.5 ms per
  match against a 100 ms budget, at ~2.1 MB allocated and ~834 matches a second single-threaded, so a
  nine-fixture division matchday is about 11 ms of simulation. Throughput will come from running
  independent fixtures concurrently; one match is always single-threaded (ADR-0004).
- **Deferred to Stage 6:** the fixture calendar, the lock and snapshot workflow, atomic nine-fixture
  publication, and the matchday worker — everything that turns this library into a season. Nothing in
  Stage 6 should need an engine change, which is the point of having built it as a pure library first.

## Stage 4 — Squads, contracts, tactics, and training foundations

A world you inherit a squad from. Every seeded club now owns a legal twenty-two-player senior squad,
generated from the same world seed as its identity, so a manager who takes a club over inherits players
rather than a name. This is the first of the stage's milestones: the schema and the generation the rest
of the stage writes and reads.

### Added

- **The `squad` schema** (shipped first, ahead of the rest of the stage): `players`, `player_attributes`,
  `player_state`, `player_contracts`, `player_registrations`, `player_unavailability`, `tactical_plans`,
  `tactical_slots`, `training_plans`, `player_training_focus`, `fixture_team_sheets`, and
  `team_sheet_entries`, in one migration applied against real PostgreSQL 17 before being committed. The
  range checks (`TRN-4` on all twenty-eight attributes, `TRN-5`…`TRN-7` on state, `TAC-9` on slot
  coordinates), the partial unique indexes (`SQ-6` one active contract and one active registration per
  player, `INS-11` one default plan per club), and the slot and team-sheet uniqueness constraints are all
  in the database rather than in a convention.
- **The player aggregates with the rules as behaviour**: `Player` (identity, physique, positions, and
  the two hidden C2 values), `PlayerAttributes` over a twenty-eight-attribute set with a canonical order
  and a checksum that makes an out-of-band edit visible, `PlayerState` (condition, fatigue, morale,
  sharpness in basis points, and the carried development remainder), `PlayerContract` (a 1–3 season term,
  `CON-1`), `PlayerRegistration` (eligibility from a fixture boundary, `SQ-7`), `PlayerUnavailability`
  (measured in fixtures, not days, `TRN-12`), and the tactics and training rows the later milestones
  write into: `TacticalPlan` with the six presets and the eight instructions, `TacticalSlot`,
  `FixtureTeamSheet`, `TeamSheetEntry`, `TrainingPlan`, `PlayerTrainingFocus`, and `SquadLegality`.
- **Deterministic squad generation.** `PlayerGenerator` and versioned `PlayerNamePools` and
  `PlayerAttributeProfiles`: three goalkeepers, seven defenders, seven midfielders, and five attackers
  per club, an age spread, per-position attribute emphasis, state, a wage, and one active contract and
  registration each. Every value is a pure function of the seed, the club's ordinal within its country,
  its tier, and the season's game year — keyed on the ordinal rather than the club id, because ids are
  UUIDv7 and differ per run while the logical squad must not.
- **Names that cannot duplicate themselves inside a squad**, by construction rather than by retry: the
  given-name and surname pools are coprime and larger than a squad, so a club's consecutive ordinals
  visit distinct pairs — the same argument the club name pools already make. A name that collides with
  the fictional-data blocklist advances deterministically to the next candidate for the same seed
  (`FIC-6`), which today's pools do not trigger.
- **The seeder generates the squads.** Running `npm run seed` now creates 2,376 players for the 108
  clubs, and `world.generation_runs` records the count alongside the clubs and accounts (`SQ-1`).
- 59 new domain tests (234 total) covering generation determinism, the golden squad, squad legality and
  composition, the attribute, state, contract, position, and tactics invariants, and the name pools'
  blocklist and injectivity; and 14 new infrastructure tests over real PostgreSQL covering the squad
  constraints, the team-sheet round trip, and the seeded squads.
- **ADR-0011**, on hidden player values as server-only columns rather than a restricted table, and on
  contract/registration agreement as an application invariant rather than a database constraint.
- **The squad, player, and contract reads** (second milestone; master plan §10.3): `GET
  /clubs/{clubId}/squad`, `GET /players/{playerId}`, and `GET /contracts`, each answering only for the
  club the caller actually holds. The squad response carries the state and contract each row needs and a
  legality summary, so the screen can warn about a squad below the minimum without recomputing `SQ-2`.
- **The ownership refusal, by name** (master plan §10.9, §15.4): `CLUB_NOT_MANAGED` for another club and
  `NO_CLUB` for an account that holds none, because "your view is stale" and "you have nothing yet" send a
  manager to two different screens and one forbidden response would leave the client guessing.
- **The `/squad` and `/players/:id` screens.** The squad table is PrimeNG's, the app's first PrimeNG
  component, lazy-loaded on its route; sorting is the table's own and announces its direction through
  `aria-sort`, and filtering is client-side over a response already bounded to 25. The player profile
  renders all twenty-eight attributes grouped by family beside the state, contract, and registration.
- **Attribute display with non-colour indicators** (F-17, master plan §11.3): an `AttributeValue`
  component renders every attribute as its number *and* the word for its band, and state values are shown
  on the 0–100 scale the API converts to (`TRN-8`). A unit test asserts both halves render, so a band that
  only changed the colour would fail.
- **The C2 guard `data-classification.md` §2.1 asks for**, now that a player response exists: a
  reflection test over the contracts assembly that fails if a squad DTO ever grows a `Potential` or
  `Reputation`, or exposes a basis-point field, plus an API test that reads the player payload as raw JSON
  and asserts neither hidden value crossed the wire.
- 8 new API integration tests (48 total), 3 data-classification tests, 20 new frontend tests (77 total),
  and 2 Playwright journeys (14 total) covering the squad screens and their refusals.

### Notes

- **The squad constants went into `WorldRuleSet`, and its version is now `world-rules-v2`.** `RULE-1`
  asks for one versioned rule set, and the file's own documentation says a stage's constants arrive with
  that stage. A world already stamped `world-rules-v1` keeps being read against v1, which is the
  versioning model working rather than a migration.
- **A `GenerationRun` records `world-gen-v2`.** The bootstrap now produces clubs *and* squads, so the
  version it records is the bootstrap's, and the sub-generators' versions are folded into the input hash.
  The Stage 3 test that pinned the club generator's version moved with it.
- **Shortlists are not here.** `modules.md` gives `market.shortlists` to the market module, and search
  and shortlisting land in Stage 10, so the squad schema is twelve tables rather than thirteen.
- **The team-sheet tables ship before fixtures do.** `competition.fixtures` arrives in Stage 6, so
  `fixture_team_sheets.fixture_id` and `player_unavailability.source_fixture_id` carry no foreign key
  yet. Stage 3 set the precedent by shipping the competition and finance shells with their stage, and the
  fixture-independent lineup work needs somewhere to land. ADR-0011 records it.
- **`training_plans` is one current row per club**, updated in place with a bumped version. The
  data-model phrase "partial unique … latest plan wins" is read as "one row per club", matching the
  entity definition, which has no supersede column; plan history is a Stage 12 concern.
- **No club is given a tactical or training plan yet.** A default plan appears when a manager first sets
  one (the tactics milestone) or when the AI does (Stage 8); Stage 12 requires "a default tactic" only
  when it rolls seasons.
- **The player generator reuses `Pcg32` and `DeterministicDigest` rather than a third copy.** The FNV-1a
  seeding that was private to `ClubIdentityGenerator` moved onto `DeterministicDigest` as `SeedOf`, and
  the Stage 3 golden name tests are what prove the extraction changed nothing.
- **The C2 test landed with the squad reads, as ADR-0011 predicted.** It cannot pass vacuously — a third
  assertion fails if the squad contracts are renamed or made internal — and it is scoped to the squad
  namespace rather than the whole assembly, because a club's public reputation is a legitimate
  `Reputation` property on a world DTO and is C0.
- **The squad reads are own-club only.** The squad list carries condition and the contract list carries
  wages, both C1 ("readable where the viewer is authorized — for example own club"), and §10.9 asks
  resource policies to enforce club ownership. The public, unattached player profile is Stage 10's
  scouting surface (`SCT-1`), which is why `GET /players/{playerId}` is scoped to the owner here.
- **State comes back on the user-facing scale, not in basis points** (`TRN-8`: the API converts; the
  database is authoritative). The conversion lives once, in the application mapper, and the response has
  no basis-point field — a rule the data-classification test now enforces across every squad DTO.
- **A squad row does not carry the attribute grid.** The table is about readiness — who is available,
  tired, or expiring — and the grid is the player profile's job. Attribute-based sorting is the Stage 10
  search surface, which will need indexed queries anyway.
- **The squad table needed PrimeNG's template-reference API, not `pTemplate`.** PrimeNG 22 reads its
  slots through `contentChild('header')` and friends, so an `ng-template pTemplate="header"` compiles and
  then renders nothing — a silent empty table. The header, body, and empty-message templates are declared
  as `#header`, `#body`, and `#emptymessage`. Worth knowing before the next dense screen.
- **The initial bundle budget moved from 500 kB to 550 kB.** Using PrimeNG puts its table styling into the
  global stylesheet, which is part of the initial payload; the table's JavaScript stays in the lazy
  `/squad` chunk. The initial total is 524 kB (123 kB transferred), so the new threshold is a deliberate
  26 kB of headroom rather than a blanket relaxation, and `maximumError` is unchanged at 1 MB.
- **`GET /clubs/{clubId}` is deliberately not in this milestone.** §10.3 lists it, but no screen needs it:
  the dashboard already composes the club, country, division, season, and finances.
- **A world seeded by an earlier generator has no players, and the seeder will not add any.** It is
  idempotent, so it reports the world and stops. An environment carrying a `world-gen-v1` world needs a
  fresh database (or a reset) before the squad screens have anything to show; the end-to-end suite in this
  change was verified against a freshly seeded one.
- **Deferred to the rest of Stage 4:** the tactics presets, slots, validator, and ETag contract (delivered
  in the next milestone, below); the training endpoints and the deterministic daily progression job behind
  a feature flag; and the contract renewal quote. **Deferred beyond it:** fixtures, match effects, full
  finances, transfers, and the public scouting surface.

### Tactics

A club you can shape. A manager now saves a formation, a slot layout, the roles, the eight team
instructions, and the default eleven, on a plan whose `version` is the contract that stops two devices
overwriting each other. This is the second of the stage's milestones: the model, the validator, and the
API. The `/tactics` screen — the drag-and-drop board and its keyboard alternative — is the rest of it.

#### Added

- **The formation presets as data** (`TAC-1`…`TAC-6`): `FormationLayouts` holds each preset's eleven slots
  — family, role, and normalized coordinates — and checks its own table the first time it is used, so a
  preset that names not-eleven slots, repeats a number, puts a role in the wrong family, or strays off the
  pitch fails by name rather than deep inside a save. Slot 1 is the goalkeeper in every preset.
- **The tactics validator** (`TAC-7`…`TAC-10`, `INS-10`, `INS-12`): one pure function over plain slot
  values and two sets of facts — who is selectable and who is unavailable. It refuses the wrong slot
  count, duplicate slot numbers, coordinates off the pitch, two slots on the same point, a role that
  disagrees with its family, a repeated player, a player who is not a selectable member of the club, an
  unavailable player, and a half-filled lineup. An out-of-position player is deliberately **not** an
  issue: `INS-10` makes familiarity a penalty the engine applies, not a reason to refuse a side.
- **The tactics API** (master plan §10.4): `GET /tactics` — the club's plans, the squad they are picked
  from, and every preset's own arrangement — plus `POST /tactics`, `PUT /tactics/{planId}`, and
  `POST /tactics/{planId}/make-default`. The first plan a club saves becomes its default (`INS-11`), and
  making one default demotes the previous one in a single transaction with the demotion committed first,
  because the partial unique index is checked per statement.
- **The ETag contract** (`CONC-1`, ADR-0009): a plan's `version` is its strong entity tag. `GET` returns
  it in the body, both writes require it in `If-Match` (answered `428` without it), and a stale one is
  answered `412`. `squad.tactical_plans.version` is now a concurrency token, so a raced save is refused by
  the database and not only by the use case's own comparison — an empty migration records the token in the
  model snapshot.
- **The validation preview**: a refused plan answers `400 PLAN_VALIDATION_FAILED` with the issues as
  stable codes, each naming its slot and player, so the screen can draw them on the pitch rather than
  render a field message.
- 15 new domain tests (249 total) for the preset layouts and every validator rule; 2 new infrastructure
  tests (89 total), one of which is the only place the concurrency token itself can be observed; and 9 new
  API integration tests (57 total) covering the create/revise/default lifecycle, the `412`/`428`/`404`
  refusals, and the validation preview.

#### Notes

- **The preset layouts are the server's, not the client's.** `GET /tactics` returns every preset's default
  arrangement, so the screen renders a formation without reproducing eighteen coordinates, and the same
  numbers reach the renderer, the input snapshot hash, and — in Stage 5 — the engine (`TAC-9`).
- **Tactical zones are not modelled yet.** `TAC-7` speaks of dragging "within validated tactical zones";
  this milestone enforces the bounds and the unambiguous half of the overlap rule — two slots on one point
  — but there is no zone map, so a dragged slot is not constrained to its preset's region. Zone geometry
  arrives with the pitch model that gives it a consumer.
- **The lineup is all-or-nothing, and the domain decides it.** A request's lineup may name any number of
  slots; whether the eleven are full is `TAC-10`, and the domain answers it with `SELECTION_INCOMPLETE`, so
  the pitch's rules live in one place instead of being split between a field validator and the domain.
- **`TacticalSlot.Reshape` was added** so a formation change re-lays a plan's existing slots in place
  rather than deleting and reinserting them. Slot numbers stay stable, which is what keeps a team sheet
  prepared against a plan version referring to the same eleven positions (`data-model.md` §3.2).
- **`CurrentSeasonQuery` was extracted from the squad queries.** The tactics read resolves the same "season
  in progress", and a second copy would be where the two answers quietly diverged.
- **The migration is deliberately empty.** Marking the plan version a concurrency token changes the model,
  not the schema; the migration exists to record it in the model snapshot, and its `Up` says as much.
- **The API tests are tolerant of a re-used club.** The world is seeded once for the whole API collection
  and a released club keeps the plans its previous manager saved, so the create test reads whether a
  default already exists rather than assuming the club is fresh. The alternative was a test that passed
  only on its first run.
- **Deferred:** the `/tactics` screen (the board, the role and instruction pickers, the keyboard
  alternative, and the `412` reapply UX) closes this milestone; the training endpoints and the
  deterministic daily progression job follow; then the contract renewal quote.

### The tactics board

A side you can see and shape. The `/tactics` screen draws the eleven slots at their normalized
positions, lets a manager drag a player onto one, move a slot, pick a role and the eight team
instructions, and save the plan under the version that stops two devices overwriting each other. This
closes the tactics milestone of Stage 4: the model, the validator, the API, and now the screen.

#### Added

- **The board** (master plan §11.1, `TAC-9`): the eleven slots rendered at the coordinates the engine
  will hash — depth on the vertical axis, width across it — so what a manager sees is the stored layout
  rather than a second, display-only copy. A slot is a focusable button with an accessible name, so the
  pitch itself is keyboard- and screen-reader-reachable.
- **The accessible, non-drag alternative** (§11.3, `TAC-8`): an assignment table of eleven rows, each with
  a role picker constrained to its family's roles and a player picker grouped by position family. This is
  the path a keyboard, a screen reader, or a touch screen uses to set a side; dragging is a convenience on
  top of it, not the only way in.
- **Drag-and-drop**: a player chip dragged onto a slot assigns them, and a slot dragged onto the pitch
  moves it, with the drop position converted back to a normalized coordinate and clamped to the pitch
  (`TAC-7`, `TAC-9`).
- **Formation presets that keep the lineup** (`TAC-1`…`TAC-6`): choosing a formation re-lays the eleven
  slots from the server's own arrangement, and a player stays in their slot number — the right back picked
  in a 4-4-2 is still slot 2 in a 4-3-3.
- **The plan lifecycle**: the plan chooser (the default first), a rename, a new plan, and the eight
  instruction pickers (`INS-1`…`INS-8`). Creating the club's first plan makes it the default, and any
  plan can be promoted with `make-default` (`INS-11`).
- **The ETag save and its conflict UX** (`CONC-1`, ADR-0009, §11.2): every save sends the version the
  client last read in `If-Match`. A `412` keeps the manager's edits, pulls the server's state, and offers
  an explicit *Reapply my changes* or *Use the server's version* — never a silent overwrite.
- **The validation preview, drawn on the board** (§10.4): a refused save returns the validator's issues as
  stable codes; the screen lists them in words, names the slot or the player, and tints the slots they
  concern. The role picker offers only a slot's own family's roles, so a `ROLE_FAMILY_MISMATCH` is
  unpickable rather than merely reported.
- 29 new frontend tests (106 total) over the plan-draft transforms (a formation change keeps the picks, an
  ordered request, a partial lineup sent rather than dropped), the presentation helpers (labels, pitch
  geometry, the wording for every validator code), and the store (the conditional save, the `412` reapply,
  the default promotion); and 2 Playwright journeys (16 total) — the board, its accessible assignment, and
  a version conflict survived by reapplying, plus the guard for a visitor with no session.

#### Notes

- **The save always sends the whole layout, and the lineup only when somebody is picked.** Sending the
  slots means a manager's dragged positions are what is stored, not the preset they started from. Omitting
  the lineup when the sheet is empty is how a legal template is saved; sending a *partial* one is
  deliberate, because the server refuses it with `SELECTION_INCOMPLETE` (`SQ-4`) and omitting it would
  silently save a plan with nobody in it — the manager would believe their picks were kept.
- **Out-of-position is a warning, not a refusal** (`INS-10`): a makeshift side is a legitimate choice, so
  the board marks it and the engine penalises it, exactly as the validator intends.
- **A formation change moves the slot, not the player.** Slot numbers are stable across presets, which is
  what keeps a team sheet prepared against a plan version referring to the same eleven positions
  (`data-model.md` §3.2).
- **Dragging uses the browser's native drag-and-drop.** A mouse drives it; the assignment table is the
  touch and keyboard path, so a phone can set a side today. Pointer-driven dragging for touch is Stage 13's
  responsive work, where it is tested at mobile breakpoints.
- **Tactical zones are still not modelled**, so a dragged slot is bounded by the pitch but not confined to
  its preset's region — the same gap the API milestone recorded, waiting for the zone geometry that gives
  it a consumer.
- **The dirty check compares the request, not the object graph.** "There is something to save" is exactly
  "the request the save would send differs from the last one", so a re-built or re-ordered draft does not
  read as a change and a save is never sent needlessly.
- **`GET /tactics` is one read for the whole screen** — the plans, the squad, and every preset's
  arrangement — so the board renders a first, empty plan without reproducing eighteen coordinates in the
  client, and the same numbers reach the renderer and the snapshot hash.

### Training

A club you can develop. A manager sets the team's focus and intensity, points any individual at one
attribute family, and a deterministic daily job advances every player in the world by a day of training —
recovery, bounded development against their hidden potential, and a fraction carried forward so nobody
loses a day to rounding. This closes the last two deliverables of Stage 4 that were still open: the
training endpoints, and the daily progression. The `/training` screen follows as its own milestone.

#### Added

- **The deterministic progression calculator** (`TRN-1`, `TRN-2`, `TRN-9`, `TRN-10`): `DailyProgression`
  is a pure function of the player, the day, the plan in force, the age curve, and the hidden potential,
  seeded from the player identity and the day and versioned (`training-v1`). It reads no clock, database, or
  global random source, so a replay reproduces the same day; it produces recovery (condition, fatigue,
  sharpness, and a bounded morale drift) and development (a fraction per emphasised attribute, carried
  forward and spent one point at a time). Intensity buys development and costs condition and fatigue;
  recovery focus buys freshness and develops nobody. An individual focus steers growth to its own family
  rather than adding to the team's, which is what makes `TRN-2` a decision.
- **Two aggregate mutations**, the first that move a stored player value outside generation:
  `PlayerState.ApplyProgression` (basis points, the development remainder, and the day) and
  `PlayerAttributes.Apply` (re-validates the 1–20 scale and restamps the checksum). Training never lowers an
  attribute and never pushes one past the player's potential or the scale (`TRN-4`, `TRN-9`).
- **The training API** (master plan §10.4): `GET /training` — the plan, the squad it applies to, and the
  option lists so the client never reproduces the enumerations — plus `PUT /training` and
  `PUT /players/{playerId}/training-focus`. The plan's `version` is its strong entity tag: a change to a set
  plan requires `If-Match` (answered `428` without it, `412` when stale), and an individual focus that
  exists carries the same contract (`CONC-1`, ADR-0009).
- **The daily progression job** (`TRN-3`, master plan §7.2): `RunDailyProgression` loads every club's
  roster — human and AI alike, with the implicit `balanced`/`normal` default for a club that has set no
  plan — advances the players whose day it has not already done, and commits once.
  `DailyPlayerProgressionJobHandler` is a thin shell over it, and `DailyProgressionScheduler`, a worker-only
  hosted service, materialises the day's date-keyed row so the row is the deadline and a late run is late
  rather than lost (ADR-0012). The run is idempotent for a day, so at-least-once delivery cannot develop a
  player twice.
- **`WorldRuleSet.DailyProgressionUtc`** (02:00 UTC, `TRN-3`), and the rule set is now `world-rules-v3`, as
  a stage's constants arrive with that stage (`RULE-1`). A world stamped `world-rules-v2` keeps being read
  against it.
- **The training persistence**: `ITrainingRepository`/`TrainingRepository` (the plan and focus as tracked
  aggregates, and the whole-world roster in four flat queries rather than one graph) and
  `ITrainingQueries`/`TrainingQueries` (the plan, the club, and the squad with each player's focus in one
  round trip).
- **`Training:EnableDailyProgression`, off by default.** The switch is configuration rather than an
  `ops.feature_flags` row because that table does not exist yet, and the scheduler and handler both honour
  it (master plan §17.12). The player-facing endpoints are complete, so they are not gated.
- **ADR-0012**, on the materialised world job, the configuration-gated switch, the pure deterministic
  calculator, and the deliberate deferral of training injuries to Stage 8.
- 12 new domain tests (261 total) covering determinism, the scale and basis-point bounds over a year, that
  training never lowers an attribute, that a focus develops only its family, the relative cost of intensity,
  the age limit, and the potential ceiling; 3 new infrastructure tests (92 total), the important one being
  the run over a real seeded world — 108 clubs and 2,376 players advanced once, the repeat a no-op, the next
  day everyone again; and 5 new API integration tests (62 total) covering the read, the create/revise with
  `428`/`412`, setting and clearing a focus, and the other-club and no-club refusals.

#### Notes

- **The scheduler is `EnsureScheduleJobs`' first form.** Master plan §7.2 names a materialiser for future
  lock and matchday jobs, but its real subject — fixtures — arrives in Stage 6. Rather than a second
  mechanism later, the worker service that ensures the daily row is written so the fixture calendar can
  join it (ADR-0012).
- **Training injuries are not in this milestone.** `TRN-12` requires them, but the band-to-fixture mapping
  is the discipline rule `PlayerUnavailability`'s own documentation gives to Stage 8; a calculator that
  invented one would be a competing definition of injury severity. The development and recovery paths are
  complete without it.
- **The facilities baseline is a constant.** `TRN-9` lists it as an input, and facilities are a post-MVP
  feature (§2.3); it becomes a real input when there is a facility to read (ADR-0012).
- **Development can leave one attribute behind for a season.** Each day's whole points are spread in a draw
  order seeded from the player and the day, so over a long run the family develops as a group but a single
  attribute is not guaranteed a point every season. That is the intended shape — some attributes come on
  faster — and the tests assert the family-level contract rather than per-attribute growth, which would be
  a coin flip.
- **The individual focus overrides the team's family selection, not adds to it.** Team focus still governs
  load and recovery, so a `fitness` plan with a `technical` individual focus means the player trains hard
  and develops on the ball. That is the reading of `TRN-2` that makes the optional instruction matter.
- **The plan takes effect from the day it is saved.** A future-dated plan would need the job to honour
  several at once for no MVP benefit; the effective date is still stored, because the row records when the
  choice was made.
- **A player may be pulled off individual focus and returned to the team plan** with a null family, which
  deletes the row. The clear is idempotent, so a retried clear is not a conflict.
- **`Player.Potential` is read by the job, not by a DTO.** The development ceiling is class C2 and the
  progression repository is an application-layer port, not a manager-facing projection; the
  data-classification test that guards the contracts assembly is untouched and still passes.
- **Deferred to the rest of Stage 4:** the `/training` screen. **Deferred beyond it:** the contract renewal
  quote (whose `CON-3` inputs include playing time, which does not exist until Stage 6's fixtures),
  fixtures, match effects, full finances, transfers, and the public scouting surface.

### The training screen

A club you can develop, on screen. The `/training` screen sets the club's team focus and intensity, and
points any player at one attribute family — or returns them to the team plan — on the plan's version and
each focus's own. This closes the last open deliverable of Stage 4: the model, the API, the daily
progression job, and now the screen.

#### Added

- **The training screen** (master plan §11.1, F-20): the plan's two choices are labelled selects and the
  roster is a table, each row carrying the condition and fatigue a manager weighs when setting a load and a
  per-player focus picker (`TRN-1`, `TRN-2`). Every focus control is a labelled select rather than a drag,
  so the screen is reachable from a keyboard and a screen reader, and condition and fatigue carry their band
  word beside the number so colour is decoration rather than the message (§11.3).
- **The plan's ETag contract** (`CONC-1`, ADR-0009, §11.2): a first plan is sent without `If-Match`, because
  there is nothing to be conditional against, and a revise sends the version the client last read. A `412`
  keeps the manager's choices, pulls the server's state, and offers an explicit *Reapply my changes* or
  *Use the server's version* — never a silent overwrite.
- **The per-player focus contract**: setting a new focus carries no tag, while changing or clearing a set one
  sends the focus's own version. A stale one is refused rather than overwriting a change made elsewhere, and
  the roster is refreshed with the refusal explained.
- **The option lists come from the server.** `GET /training` already returns every focus, intensity, and
  attribute-family code, so the screen renders them without reproducing the enumerations, and the empty
  value of the focus select is the clear that returns a player to the team plan.
- 17 new frontend tests (123 total) over the presentation helpers (a label for every code the server can
  send, the team plan first, the effective-date formatting) and the store (the create-without-tag, the
  revise-under-version, the `412` reapply, the new/set/cleared focus, and the stale-focus refresh); and a
  new Playwright journey (18 total) — the plan, a version conflict survived by reapplying, an individual
  focus set and cleared, plus the guard for a visitor with no session.

#### Notes

- **The screen reads one response.** `TrainingResponse` carries the plan, the club, the option lists, and
  the squad with each player's focus, so the screen makes one round trip and the same numbers reach the
  controls that the progression job reads (`TRN-3`).
- **A plan equal to the implicit defaults is not dirty.** When a club has not set a plan it is already
  training `balanced`/`normal`, so the save button stays disabled until a manager actually changes a choice
  rather than creating a row that says what the job already assumes.
- **The roster's order is the server's** — goalkeepers first, then by name — the order a manager reads a
  squad in, so the client does not re-sort it.
- **The individual focus is written straight into the read model**, not drafted: each is a single value on a
  single player, and a document to stage around it would be the tactics draft's machinery without its
  reason.
- **Tactical zones, drag-and-drop, and pointer input are not here**, exactly as the tactics and training API
  milestones recorded: they belong to the pitch model and the Stage 13 responsive work.

## Stage 3 — World generation, six countries, clubs, and onboarding

A world you can onboard into. Six fictional national pyramids are generated from one seed, a manager
creates a profile, chooses a country, takes over an AI club in its lowest active tier, and inherits it
exactly as it stands. A country that fills with humans queues the generation of the next tier underneath
it.

### Added

- **The schema the rest of the stage writes into** (shipped first, in its own commit): the `world` schema
  (game worlds, countries, manager profiles, clubs, club tenures, division provisioning requests,
  generation runs), the `competition` shell (seasons, divisions, division-seasons, club season entries),
  and the `finance` shell (club accounts), in one migration applied against real PostgreSQL 17 before
  being committed. The versioned `WorldRuleSet` holds every constant from game rules §3, and its version is
  stamped onto the world and each season, so a historical season is interpreted against the rules that were
  actually in force when it was played.
- **The world aggregates with the rules as behaviour**: `GameWorld` (freeze/resume), `Country`, `Manager`
  (the resignation cooldown), `Club` (tier-scaled baselines and a slug derived from the name rather than
  supplied beside it), `ClubTenure` (the whole of the ownership model), `DivisionProvisioningRequest`,
  `GenerationRun`, `SeasonCalendar` (a pure function from a first matchday to a season's whole window),
  and a `CountryCapacity` value object that answers "does this country have room, and should it grow?" in
  one place instead of in an endpoint.
- **Mappings that put the plan's constraints in the database rather than in a convention**: partial unique
  indexes for one open tenure per club and per manager (`OCC-9`), unique `(country_id, target_tier)` for
  provisioning (`PYR-3`), unique per-world club name and slug, one club per season, and the `FIN-13`
  checks that neither balance can go negative and reservations cannot exceed the cash behind them.
- **Deterministic world generation.** `Pcg32`, a pinned, explicitly tested PRNG, plus versioned fictional
  name pools for all six locales (`england`, `spain`, `germany`, `italy`, `france`, `romania`), a curated
  blocklist of real football identities, and `ClubIdentityGenerator`. Every generated value is a pure
  function of the seed, the country's pool, and the club's ordinal within its country, so the same seed
  reproduces the same pyramid and different seeds produce visibly different ones.
- **Club names that cannot collide, without a retry loop.** Places and suffixes are woven by ordinal rather
  than multiplied, so a division alternates both instead of naming eighteen clubs after one place; the
  combination cycle is their least common multiple, which makes the pairing injective and the names unique
  by construction. Past the cycle the generator adds a qualifier and then a numeral, so a deep pyramid runs
  out of names only by running out of integers (`PYR-11`).
- **The world seeder**, as a use case and as `tools/world-seeder`. `--seed` and `--first-matchday` override
  the configuration; everything else comes from `World:*`. It is idempotent — running it against an
  existing world reports that world and writes nothing (`WORLD-1`) — and it records the seed, generator
  version, and a digest of the non-seed inputs in `world.generation_runs` (`PYR-14`).
- **The onboarding commands**: `CreateManagerProfile`, `ClaimClub`, and `ResignClub`. The takeover validates
  the club is an active, AI-controlled member of the country's lowest active tier, disables a manager who
  already holds a club or is serving the resignation cooldown, and returns the inherited club.
- **`CapacityEvaluator`**, which answers "should this country grow?" in one place. It runs after every
  successful takeover and after every resignation, and creates the next tier's
  `DivisionProvisioningRequest` exactly once (`PYR-1`, `PYR-2`).
- **Advisory locks**: `IAdvisoryLock` and `AdvisoryLockKey`, implemented over
  `pg_advisory_xact_lock`. A takeover takes the manager's lock and then the country's, always in that
  order, which is what keeps the pair deadlock-free (`PYR-3`).
- **Explicit transactions** on `IUnitOfWork` (`BeginTransactionAsync`, `IDatabaseTransaction`,
  `TransactionIsolation`), so a workflow that must hold a lock across more than one save can.
- **The onboarding reads**: world, countries, per-country capacity, available clubs, onboarding state, and
  the inherited-club dashboard, each one query shaped for one screen.
- **The API** from master plan §10.2, plus `GET /clubs/{clubId}/dashboard`. Claim refusals carry the stable
  codes §7.6 requires — `CLUB_ALREADY_CLAIMED`, `MANAGER_HAS_ACTIVE_CLUB`, `MANAGER_PROFILE_REQUIRED`,
  `MANAGER_IN_COOLDOWN`, and `CAPACITY_PROVISIONING` — and the last two carry the moment the cooldown
  lapses and the provisioning request's state and polling hint (`PYR-10`).
- **The Angular onboarding screens** (`/onboarding/manager`, `/onboarding/country`, `/onboarding/club`) and
  a dashboard that routes on state: no profile, no club, or a club. `OnboardingStore` holds the reference
  data and the claim's idempotency key; `requireVerifiedEmail` keeps a manager who cannot claim away from
  the screen that would ask them to.
- 109 new domain tests (175 total) covering the PRNG's pinned sequences, the generator's determinism,
  uniqueness, deep-pyramid behaviour, and the blocklist; 38 world infrastructure tests over real
  PostgreSQL, including the concurrent-takeover and fill-a-tier races; 5 API onboarding tests; 14 new web
  tests (57 total); and a Playwright onboarding journey.
- **ADR-0010**, on why a takeover serialises with an advisory lock rather than with `SERIALIZABLE`.

### Fixed

- **The worker's composition root could not resolve `IRequestContext`.** The API registered an HTTP-backed
  implementation and nothing registered a default, so any use case that writes an audit row would have
  failed the moment the worker ran one. `ServiceRequestContext` is now the default, and the API's own
  registration overrides it — which is the arrangement `IRequestContext`'s own documentation described and
  nothing implemented.

### Notes

- **The onboarding endpoints sit at the version root, not under `/api/v1/world`.** Master plan §10.2
  addresses them as resources (`/countries`, `/club-claims`, `/club-tenure`), and the committed path is the
  contract. It is the same reasoning that puts `/me` at the root.
- **A takeover holds an advisory lock in a read-committed transaction rather than a serializable one.**
  §7.6 asks for serializable, but a snapshot-isolation transaction fixes its snapshot at its first
  statement — which in this workflow is the lock request — so it would still be reading pre-lock state
  after the lock was granted, and the losing manager would get a constraint violation instead of the
  documented refusal. ADR-0010 records the decision; the partial unique indexes are untouched and remain
  the backstop.
- **The deterministic PRNG in `Domain` will not be the match engine's.** The engine ships its own
  `Pcg32` in Stage 5, and that is deliberate: a generated world's reproducibility and a played match's
  reproducibility are separate versioned contracts, and the engine's output hashes are pinned per engine
  version (ADR-0004). The dependency rules settle the question anyway — `Domain` depends on nothing (DEP-1)
  and the engine may not depend on it (DEP-2).
- **Ordinals are per country and never restart per tier.** Tier 1 takes 0–17, tier 2 takes 18–35. If each
  tier restarted at zero, every provisioned tier would propose the same eighteen names and the unique name
  index would reject the second one. A test generates twelve tiers and asserts the names never repeat.
- **Name pools use invented places only, and never a real club's home town.** Generation is where licensed
  identity would enter the product, so the pools are the primary defence and the blocklist is the backstop
  (`FIC-5`). A test runs the blocklist check across every pool and every generated name.
- **A club's founding game year is the first season's game year.** A plausible older founding date would
  have to be invented from the seed, and inventing history is not what a reproducibility rule should be
  doing with its randomness.
- **The tier-1 money, stadium, and reputation baselines are provisional.** They halve per tier, which is the
  shape Stage 9 needs, and they are recorded in `game-rules.md` as balancing values rather than as rules.
  Calibrating them is Stage 9's multi-season simulation work, not a guess made now.
- **Provisioning is requested, not executed.** The takeover creates the `DivisionProvisioningRequest` and
  returns `CAPACITY_PROVISIONING` with its state; generating the tier, its squads, and its backfilled
  results is Stage 11. Until then a full country stays full, on purpose: a half-built tier that a manager
  could claim would be worse than a wait.
- **Only a club claim requires a verified address.** Master plan §12.1 lists club claims, bidding, listing,
  and display-name changes; creating a manager profile and resigning are authenticated but not
  verification-gated, because an unverified account can reach neither a club nor a bid.
- **The dashboard shows only what exists yet.** Identity, competition placement, control, and money. Squad,
  contracts, fixtures, and history join the response in the stages that create them rather than appearing
  now as permanently-empty fields.
- **The Playwright journey gives its club back.** It resigns at the end, which exercises the resignation
  path and returns the club to the pool. Without that, each run would consume one of the 108 clubs and the
  suite would start failing after eighteen runs for a reason that is not a defect.
- **`appsettings.json` had to travel with the seeder tool.** The generic host does not copy it the way the
  web SDK does, so the project declares it explicitly and the tool sets its content root to the binary's
  directory rather than the process's working directory.
- **`game_worlds` has no JSONB feature-flags column**, although master plan §6.3 lists one. Feature flags
  belong in `ops.feature_flags`, where they are queryable and versioned; a JSONB blob on the world row is
  precisely the unfinished modelling the JSONB policy forbids (`JSN-5`).
- **`club_season_entries` carries `season_id` as well as the division-season.** Without it, "one club
  appears in exactly one division per season" is not expressible as a database constraint: a promotion bug
  could enter one club into two divisions in the same season and every standings query would double-count
  it.
- **A failed provisioning request can be retried, and the retry reuses the recorded seed.**
  `unique (country_id, target_tier)` means a second request for the same tier cannot exist, so without that
  transition one failed run would block the tier permanently. Reusing the seed is what keeps `PYR-14` true
  across attempts. A domain test caught the gap.
- **Tier names are descriptive rather than evocative** — "England Top Division", "Spain Division 2" — so no
  generated name can drift towards a real competition's branding (`WORLD-3`).
- **A seeded division starts active and a provisioned one does not.** Stage 11's tier stays in
  `provisioning` until its generation, validation, and backfill all complete (`PYR-8`); the tier the seeder
  creates is claimable the moment it exists, because there is nothing left to backfill.

## Stage 2 — Identity and authenticated walking skeleton

The auth module: the account schema, the full credential lifecycle, session rotation and reuse
detection, the request security the rest of the product will build on, the Angular screens that drive
it, and the browser journeys that prove it works end to end.

### Added

- `auth` schema: `users`, `user_roles`, `refresh_sessions`, `email_tokens`, `user_consents`, with
  unique indexes on normalized email and display name, a unique index on each token hash, a check
  constraint on the status lifecycle, and restrictive foreign keys to `users` (ADR-0002).
- `ops.audit_log` and an `IAuditWriter` that stages audit rows **in the same unit of work** as the
  change they describe, so an action without an audit trail is impossible rather than merely
  discouraged. Auth actions (register, verify, login outcomes, rotation, reuse, logout, reset,
  profile change, deletion) are recorded with actor, target, correlation ID, and a hashed IP.
- Domain auth aggregates with the lifecycle as behaviour: `User` (register, verify, lockout,
  suspend/restore, change password, display name, deletion), `RefreshSession` (issue, rotate,
  revoke), `EmailToken` (issue, consume, supersede), `UserConsent`, `UserStatusRules`, and a pure
  `AccountLockoutPolicy` escalation ladder.
- Use cases for the whole lifecycle: `RegisterUser`, `VerifyEmail`, `ResendVerificationEmail`,
  `Login`, `RefreshAccessToken`, `Logout`, `LogoutAll`, `ForgotPassword`, `ResetPassword`,
  `GetProfile`, `UpdateProfile`, `DeleteAccount`, plus the shared `EmailTokenIssuer` and
  `SessionIssuer`. Each returns an explicit outcome rather than throwing for expected results.
- FluentValidation request validators with shared email, display-name, and password rules, returning
  per-field Problem Details that the client can render against individual form controls.
- Infrastructure providers: `IdentityPasswordHasher` over ASP.NET Core's password hasher with an
  explicit work factor and transparent rehash, `SecureTokenService` (CSPRNG tokens, SHA-256 storage
  hashes, HMAC client fingerprints), `JwtAccessTokenIssuer` (HS256 with the security stamp embedded),
  `SmtpEmailSender` (MailKit), and the EF Core repositories implementing the auth ports.
- The endpoints from master plan §10.1, including `GET/PATCH/DELETE /api/v1/me`.
- Bearer authentication that re-reads the account on every authenticated request and compares the
  security stamp, which is what makes a stateless token revocable. Policies for a verified manager
  and for the `admin`, `operator`, and `support` roles — the groundwork for the Stage 14 MFA
  requirement.
- Response security headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, a
  `default-src 'none'` CSP, and `Permissions-Policy`), plus rate limiting on the auth endpoints.
- The HttpOnly, `Secure`, `SameSite=Lax` refresh cookie, scoped to the refresh path, with `Secure`
  relaxed only in Development where the client is served over plain HTTP.
- A fake-email local workflow: `Email__Host`/`Email__Port` point at the Compose mail catcher, and the
  integration tests substitute a recording sender.
- The Angular auth screens — register, sign in, confirm email, forgotten password, reset password, and
  settings — as standalone components with reactive forms. Field messages are resolved in one place:
  the client's own rule wins while the manager is editing, and the server's per-field `errors` map
  takes over afterwards.
- The `SessionStore`, which holds the access token in memory and nowhere a template or another script
  can reach it, restores the session before the first route activates, and **shares one rotation
  between concurrent callers**. Without that sharing, a screen firing three requests on load would
  present an already-consumed refresh token and trip the server's reuse detection against its own
  legitimate user.
- The route guards in both directions, and a `returnUrl` that is honoured only when it is a
  same-origin absolute path — navigating to an arbitrary value would turn the sign-in screen into an
  open redirect.
- The token interceptor: attaches the bearer token, keeps auth endpoints out of both the token and the
  retry (a rejected sign-in is an answer, not an expiry), recovers from an expired token exactly once,
  and ends the session when the rotation fails instead of retrying forever.
- The Playwright suite in `tests/web-e2e`. It owns its stack — `globalSetup` brings up PostgreSQL and
  the mail catcher and applies migrations, then the config starts the API and the web client — and it
  reads the confirmation and reset links out of the mail catcher rather than being handed a token.
  Ten journeys cover registration and confirmation, the session surviving a reload, the unconfirmed
  account's restriction, account closure, renaming with the shell following, signing out everywhere,
  password replacement with a single-use link, and the guards including the external-`returnUrl` case.
- Frontend unit coverage for the session store, the guards, the interceptor, and the field-message
  precedence (43 web tests in total, up from 8).
- An `e2e` CI job that installs Chromium, typechecks the suite, runs the journeys, and uploads the
  report on failure.

### Notes

- **Access tokens carry a security stamp that is checked against the database on every authenticated
  request.** This costs one indexed primary-key lookup and is what makes suspension and
  "sign out everywhere" take effect immediately instead of after the token expires. A cache belongs
  here only when profiling shows it is needed.
- **`DELETE /api/v1/me` takes the confirming password in a JSON body.** ASP.NET Core refuses to infer
  a body parameter for `DELETE`, so the binding is explicit.
- **Only the auth endpoints are rate limited for now.** The broader per-route and per-user limits are
  Stage 14 work, where they can be tuned from load evidence instead of guessed at. The permit limit is
  configuration, so load and functional tests raise it rather than tripping it.
- **`refresh_sessions.revocation_reason`** is an addition to the column list in master plan §6.2:
  reuse detection has to distinguish "rotated" from "signed out" to know whether a presented token is
  a replay, and the reason is also what an operator needs when investigating a session incident.
- `Auth:SigningKey` is validated at startup in every environment. Development has a committed
  dev-only value, following the precedent of the local database password; every other environment
  must supply its own.
- **Playwright arrives in Stage 2, not Stage 7.** `docs/testing/test-strategy.md` originally deferred
  journeys until the match viewer, but master plan §16 makes the full auth lifecycle a Stage 2 exit
  criterion and `docs/product/mvp-traceability.md` asks for a registration journey at F-01. The test
  strategy has been corrected. The remaining master plan journeys still arrive with the screens they
  exercise, because a journey for a screen that does not exist tests nothing.
- **The journey stack raises `RateLimiting:AuthPermitLimit`.** The suite signs in far more often from
  one address than a person would, and a throttle that fired halfway through would look like a bug in
  the feature under test. The limiter is still tested — by the API integration tests, which build a
  host with a deliberately tiny limit and can therefore assert on it directly.
- **The suite is one worker, deliberately.** Every journey shares one database and one mail catcher,
  and a test that read another test's message out of the mailbox would fail for a reason that is not
  real.
- Anonymization after the deletion cooling period, session listing on the settings screen, and TOTP
  enrolment remain outstanding; they belong to the privacy workflow (Stage 14) and the settings
  screen (Stage 13).

### Fixed

- **The settings screen told managers the manager-name field was unavailable while leaving it
  editable.** The input carried both `formControlName` and a `[disabled]` binding. A reactive form
  owns the disabled property — `FormControlName` re-applies the control's own state on every change —
  so the binding was silently ignored. An unconfirmed manager could type a new name into a field the
  same screen said was locked, and only the save button stopped them. The control's own state is now
  set from the profile, and an end-to-end journey asserts the field really is disabled.
- The migration for the auth tables initially omitted the foreign keys to `auth.users`; the migration
  was regenerated rather than patched, since it had not been applied anywhere.

## Stage 1 — Monorepo scaffold and engineering guardrails

### Added

- Solution `TouchlineManager.slnx` with `Domain`, `Application`, `Infrastructure`, `Contracts`,
  `MatchEngine`, the `Api` and `Worker` composition roots, seven test projects, and two tool
  projects. Identifier naming uses the real product name throughout.
- Central package management (`Directory.Packages.props`), lockfiles, and a pinned SDK
  (`global.json`). `TreatWarningsAsErrors` with .NET analyzers at `Recommended`.
- Durable job queue on PostgreSQL: `ops.jobs` with enqueue idempotency on
  `(job_type, business_key)`, `FOR UPDATE SKIP LOCKED` claims, leases with expiry, exponential
  backoff with jitter, and dead-lettering for permanent domain failures (ADR-0003).
- Job poller as an Infrastructure hosted service, registered only by the worker composition root so
  the API can never execute a deadline.
- API composition root: validated options that fail startup when misconfigured, correlation
  middleware, RFC 9457 Problem Details, liveness/readiness/detailed health endpoints, CORS
  allowlist, OpenAPI in Development, and one route group per bounded module.
- Log redaction filter shared by both composition roots, enforcing the redaction list in
  `docs/security/data-classification.md` §4, with tests.
- Angular 22 PWA: standalone components, strict TypeScript and strict templates, PrimeNG 22 with
  Aura and Tailwind 4, documented CSS layer ordering (`tailwind-base, primeng, tailwind-utilities`),
  service worker and manifest, responsive shell with safe-area insets, skip link, reduced-motion
  handling, connectivity banner and correlation-ID footer.
- `IClock` seam with a fake clock in tests; no code path uses server local time.
- Docker Compose providing PostgreSQL 17 and a mail catcher, plus `npm run dev` as the single
  command that starts the backing services, the API, the worker and the web client.
- GitHub Actions pull-request pipeline: locked restore, `dotnet format` verification, Release build
  with warnings as errors, all test projects, generated migration SQL as a review artefact, and the
  Angular typecheck/build/test job.
- Architecture tests that fail the build when the dependency rules in
  `docs/architecture/modules.md` §2 are crossed.

### Fixed

- `global.json` pinned an SDK version the installed preview sorts below, so no SDK resolved.
- `EnqueueNoOpJob` and the job handler registry were registered as singletons while consuming the
  scoped `DbContext`. Use cases are now scoped, and the worker resolves handlers from each job's own
  scope. Caught by the API integration tests at startup.
- The log redactor missed `refresh_token`-style keys, because `_` is a word character and therefore
  provides no word boundary for a plain `\btoken\b` pattern.
- The private-field naming rule also applied to `const` and `static` fields, which are PascalCase by
  convention. Only private instance fields take the `_` prefix now.
- Local PostgreSQL publishes host port **55432**, not 5432. Docker Desktop will happily bind a second
  listener on an already-occupied host port, after which connections land unpredictably — observed
  here when 5433 was chosen and a second process bound it too. A port outside the standard and
  ephemeral ranges removes the class of problem.
- Both `package.json` files carry an `.npmrc` setting `include=dev`. A shell exporting
  `NODE_ENV=production` otherwise silently omits devDependencies, producing an install that cannot
  build.

### Notes

- EF Core is pinned to 10.0.4 to match the Npgsql provider's floor, so the graph contains exactly
  one EF Core version.
- FluentAssertions is pinned to 7.2.0. Version 8 changed to a licence that is not free for
  commercial use.
- Container images for the API, worker and web, and separate database roles for migrations and
  runtime, are deferred to Stage 14 where they are built and exercised for real rather than
  half-implemented now.
- `docs/product/match-engine.md` and the operations runbooks are intentionally absent; they are
  Stage 5 and Stage 14 deliverables.

## Stage 0 — Product rules, architecture, and executable specifications

### Added

- `docs/product/master-plan.md` adopting the approved plan.
- `docs/product/game-rules.md`: the normative rule set, with stable references for every
  configurable value, a consolidated constant table, and an explicit list of values deliberately
  left to balancing.
- ADR-0001 … ADR-0009 covering the modular monolith, authentication and sessions, the PostgreSQL
  job queue, the deterministic match engine, dynamic pyramid expansion, semantic highlights,
  PWA-first delivery, deployment topology, and time/identity/concurrency conventions.
- C4 context and container diagrams, the module map, and the first ER diagrams.
- Glossary; UI tone and fictional-data policy; disaster/recovery-relevant threat model and data
  classification; MVP traceability for 51 features plus a guardrail per non-goal.
- `docs/product/stage-0-review.md`: the consistency review, the resolved contradiction about where
  shortlists live, and the three decisions requiring a product owner's answer.

### Fixed

- Master plan §5.2 and §6.5 disagreed about the schema for shortlists. Resolved to
  `market.shortlists` and recorded, because placing it in `squad` while the market module writes it
  would violate the module-ownership rule.
- Closed two gaps that would otherwise have produced two implementations: what happens at the top of
  a pyramid (`PR-9`, `PR-10`) and whether an unserved suspension carries across rollover (`DIS-8`).
