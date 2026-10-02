# ADR-0051: Engine-v4 plays a possession along a continuous passage, and records the film as a side channel

- **Status:** Accepted
- **Date:** 2026-10-02
- **Stage:** Engine roadmap, continuous replay milestone (M1 of `engine-v4-continuous-match-replay.md`)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0006](0006-semantic-highlight-keyframes.md), [ADR-0013](0013-engine-arithmetic-and-scoreline-effect.md), [ADR-0019](0019-engine-v2-and-season-statistics.md), master plan §8.2–§8.5, §9.2–§9.3, game rules `MAT-3`, `MAT-4`, `MAT-8`, `MAT-9`, `MAT-11`, `TBL-13`

## Context

The `engine-v3` spatial model located a possession on the pitch, but it did so with one absolute draw:
every possession was assigned a random point in the possessing side's **own** half (`PossessionSimulator.AdvanceBall`,
x 2,600–4,200 of 10,000), and every event inherited it through `MatchState.Emit`. Two consequences followed,
both verified in code:

1. **The ball moved randomly and never built up.** There was no pass, carry, cross, or duel position in the
   model at all; the replay director invented one arc from a fixed build-up X to that absolute point.
2. **Direct free kicks were dead code.** `AttackingX` was the own-half ball x, so
   `attackingX >= FreeKickShootingRangeX` (`6_500`) was unreachable. Shots and free kicks were logged in a
   team's own half, and any shot map or chance narrative built from event positions was nonsense.

The product owner confirmed two decisions that shape the fix: the replay should be **one continuous condensed
film of the whole match** (with a companion highlights reel over the same data), and the build-up should be a
**real simulated pass/carry chain** rather than an interpolation between sparse chance snippets. That makes
the ball's path the engine's problem, not the director's.

Three questions follow:

**1. How does a possession move the ball?** It must start where the last one left it, progress forward like
football, and end where its outcome says it should — and its location must not be a second, independent
random process that the scoreline was never calibrated against.

**2. Does the geometry change the result?** `MAT-9` and ADR-0004 pin a match's output hash. Reworking the
spatial model necessarily changes what a result is; the question is only whether it also *uncalibrates* the
engine, and whether a replay read can see the ball path without the path being able to move a play.

**3. What happens to a match already played?** A released engine version is never altered in place. This is a
new version, and there is no production world, so the cost is a reseed (ADR-0019's precedent).

## Decision

**1. A possession is a passage: a chain of touches from where the last one left the ball — or a restart — into
the attacking third, ending at an outcome-appropriate point.**

The passage begins at `MatchState.Ball` (continuity), overridden by restarts: the centre spot after kick-off,
half-time, and a goal; the goal area of the side that restarts after a keeper claims or parries. Its touches
advance the ball forward a drawn distance with lateral drift, bounded by the rules' touch count and per-touch
advance. Its **pressure point** — where the defending side engages and a foul is committed — is drawn from a
band spanning the middle and attacking thirds, so a free kick in shooting range is genuinely reachable; its
final point is chosen by the outcome:

| Outcome | Where the ball ends |
|---|---|
| Open-play shot | the final third, across the band of the shot's zone |
| Penalty | the penalty spot |
| Foul (no shot) | the pressure point |
| Direct free kick in range | the pressure point, in range |
| Corner | the corner flag, then the box for the header |
| Offside | the offside line |
| Plain turnover | the middle third |

`state.MoveBall(point[, altitude])` is called along the passage and **before every `Emit`**, so an event's x/y
is where it actually happened. Crosses, headers, and shots carry altitude; ground passes and carries do not.

**2. The geometry is drawn from a per-possession derived stream, never the play stream.**

Each possession's geometry (path, touch distances, lateral drift, the shot zone, and the participants of the
generic carry/pass touches) is drawn from
`new Pcg32(seed * 1_000_003 + ordinal)`, the same pattern `AssistPlanner` uses (ADR-0019). The outcome
decisions — the foul, the scramble, progression, the duel, creation, and the chance itself — keep their
existing draw order and probabilities on the main `Pcg32`. `AdvanceBall` and
`Min/MaxPossessionAdvanceBasisPoints` are deleted, so the main stream advances differently from version 3;
the *distributions* the engine is calibrated to are re-validated rather than re-invented.

**3. `MatchPassageRecorder` captures the film as an optional side channel, and the result does not carry it.**

`MatchSimulator.Simulate` gains an optional `MatchPassageRecorder`. Per possession it captures the ordinal,
side, start/end clock, emitted event sequences, ball waypoints `(fraction, x, y, z, kind)`, and touches
`(fraction, participantId, action, x, y, z)` using the participants the simulation actually picked — the duel
carrier and tackler, the shooter, the keeper, the corner header winner, the free-kick taker. The recorder
consumes no draw and changes no state, so the output hash is identical with and without it; the engine and
rules versions become `engine-v4` / `engine-rules-v4` and the golden hashes are re-pinned.

**4. The passage is a versioned model, and a database seeded before it must be reseeded.**

Engine-v4 invalidates stored matches: they cannot be re-simulated and will not present. There is no production
world; the dev database is archived (renamed, never dropped) and reseeded, as ADR-0019 did for version 2.
Retaining a compiled previous engine version remains the pre-launch requirement ADR-0019 recorded.

## Consequences

**Positive**

- The ball finally moves like football: a build-up, a final-third entry, and a located shot or free kick.
  Event coordinates become meaningful for the first time, which is what a shot map, a chance narrative, and
  the continuous film all need.
- Direct free kicks in range are reachable code: a foul deep in the attacking third now genuinely produces a
  `free_kick_shot`, measured at about 1.5 per match on even sides against the same goal and shot bands.
- The outcome formulas are untouched. The geometry is on a stream of its own, so the calibration is
  re-validated, not re-derived, and the with/without-recorder hash test pins the side channel's inertness.
- The film is a by-product of the ordinary simulation, so a replay read re-simulates the frozen input and
  recovers the same passage — no stored presentation, no second mode of simulation (`MAT-8`).

**Negative**

- The output hash of every match changes, so a database seeded before this change must be reseeded.
- A passage's waypoint fractions are the order the ball reached each fact, not a clock reading, because a
  possession's seconds are still drawn up front. The replay paces the film from this order; it is a display
  fact and is not hashed.
- A goal kick and a keeper claim are modelled as a goal-area restart, not as their own event types; the
  engine has no `goal_kick` event and this change does not add one.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Interpolate the ball from a fixed build-up point to the outcome, as the replay director did | Keeps the ball a director's invention rather than a simulation fact, and leaves event coordinates meaningless in the result. |
| Draw the geometry from the play stream | Advances every subsequent outcome and moves the scoreline distributions the engine was calibrated to, throwing away the calibration for a purely spatial gain. |
| Store the passage on the result | Changes the output hash of every match and puts a replay-only fact inside a hashed, audited document; a side-channel sink leaves the result unchanged. |
| Keep the absolute own-half point and only fix the free-kick threshold | Would leave the ball unrelated to the play and every shot map wrong; the symptom was the ball, not the constant. |
| Add `goal_kick` and `keeper_claim` event types now | A new event semantic ripples through commentary, highlights, the domain, and the API; the goal-area restart reproduces the geometry without a contract change. |
| Keep `engine-v3` and read old and new locations leniently | A historical result's hash claims a provenance it would not have; ADR-0004 refuses exactly this. |
