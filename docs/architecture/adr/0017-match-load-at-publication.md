# ADR-0017: A match's load on the squad is derived from the stored result and applied at publication

- **Status:** Accepted
- **Date:** 2026-09-27
- **Stage:** 8
- **Related:** [ADR-0014](0014-matchday-lock-resolution-and-publication.md), master plan §3.9, §7.4, §8.5, game rules `TRN-5`, `TRN-11`, `TRN-13`, `MAT-9`

## Context

Stage 8's first milestone made publication the point at which a result reaches the squad: a round's cards
and injuries become discipline records and absences, applied inside the publication's serializable
transaction. The rest of that subject is the ordinary load a match places on the players who played —
condition consumed, fatigue accumulated, and morale moved by the result and by how much they contributed
(`TRN-11`, `TRN-13`).

Four decisions in that work are hard to change once a world is running.

**1. Where the load is computed.** The plan offers two readings — the engine could return state deltas, or a
rule could decide them (`TRN-11` names minutes, intensity, stamina, and tactics as the inputs). ADR-0012
took the rule reading for training.

**2. Where the minutes come from.** The engine produces a line per participant carrying the minutes they
played, but the stored result document held only the two sides' statistics. Publication therefore had no
per-player facts at all, and the two ways to get them were to store the lines or to re-derive them by
re-simulating the frozen snapshot.

**3. When the load lands.** A result is private until its round publishes (`MAT-7`), so an effect derived
from it must be private too, and the milestone that added cards and injuries already established that
publication, not simulation, is where a squad is touched.

**4. Who carries the result.** `TRN-13` names playing time as an input to morale, so a player who never left
the bench is not obviously a player a result should move.

## Decision

**1. The load is a pure, versioned calculator, not an engine output.**

`MatchLoadCalculator` (`match-load-v1`) turns the frozen facts of a match — each participant's minutes, their
stamina, their side's instructions, and the scoreline — into one signed delta per player. It reads no clock,
database, culture, or random source, so it produces no draw: the rule is arithmetic, and a replay reproduces
it. Its coefficients live with it and are versioned, exactly as `DailyProgression`'s do (`training-v1`), so a
coefficient change is a named rule change rather than a silent constant tweak.

**2. The engine's player lines are stored in the result document; they are not re-derived.**

`match-statistics-v1` becomes `match-statistics-v2`, carrying the `PlayerLines` the engine already produces
beside the two sides' statistics. Publication reads the minutes from there rather than re-simulating the
snapshot. Re-simulation would couple publication to whatever engine build is deployed when the round is
published, and a result delayed across an engine release could no longer be published honestly; the stored
lines are the facts the result was made from. The document is the engine's own output, written once at
staging, so this is one copy and not a second source of truth. A version-1 document is refused rather than
read, because a reader that accepted it would apply no load to anybody.

**3. Publication applies the load, in its own transaction.**

`PublishMatchday` loads the round's match loads and the tracked `PlayerState` rows of the players who
appeared, and applies the calculator's deltas with the same `SaveChanges` that publishes the nine fixtures,
rebuilds the table, and applies the cards and injuries. A round that is not fully staged publishes nothing
and loads nobody, and a republished round loads nobody twice — the commit-or-nothing property `MAT-7`
already required now covers the squad too.

**4. Only the players who appeared are loaded.**

A line's minutes are what the rule reads, so a player with none is not an input: an unused substitute carries
none of the result. Morale is scaled by minutes, so a late cameo carries less of the result than a full
match while a starter carries all of it. The other inputs `TRN-13` names — contracts and transfers — belong
to the stages that own them.

## Consequences

**Positive**

- The load cannot drift from the result that caused it: it is published in the same transaction, from the
  same stored facts, and the calculator is a pure function of them.
- A delayed publication is safe across an engine deploy, because it reads the stored lines rather than
  re-running the engine.
- The rule is testable arithmetic with exact expectations, and its coefficients are versioned separately from
  the world's rule set and the engine's.
- Cards, injuries, condition, fatigue, and morale now reach the squad through one publication path, so there
  is one place to read when asking "what did this round do to a club".

**Negative**

- The result document grows by one line per participant (up to thirty-six per match). It is a JSONB payload
  nothing filters on, so the cost is storage and read size, not query shape.
- Bumping the document schema to version 2 makes a version-1 row unreadable. No production world exists yet,
  so the only cost is that a database seeded before this change must be reseeded for a played match to be
  readable.
- The `AggregateStatistics` document now mixes team statistics and participation, which is a slightly wider
  name than its contents. Splitting it would be a second document and a second write site for no reader that
  wants one without the other.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Have the engine return state deltas | Changes the engine's output contract and its golden hashes, and puts a game-balancing rule inside a library that must stay a pure, versioned simulation (`MAT-9`, ADR-0004). |
| Re-derive the minutes by re-simulating the snapshot | Couples publication to the deployed engine build: a round published after an engine release could not reproduce an older result, and publication would pay a simulation per fixture. |
| A `match.lineup_participation` table | Master plan §6.6 lists one, but the project already chose to store the engine's output as a versioned document rather than as tables nothing queries (`MAT-8`), and participation is read only by publication. |
| Apply the load at simulation | An effect would become visible before its result, which `MAT-7` forbids and which the discipline milestone already settled. |
| Give every squad member a morale change | `TRN-13` names playing time as an input; a player with no minutes has none. The absence of an input is not evidence of a mood. |
