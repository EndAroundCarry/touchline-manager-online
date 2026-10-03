# ADR-0054: Engine-v6 counts passes and take-ons on the player line, and the season statistics store them

- **Status:** Accepted
- **Date:** 2026-10-03
- **Stage:** Engine roadmap, player statistics
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0019](0019-engine-v2-and-season-statistics.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0053](0053-engine-v5-half-time-clock-and-restart-ownership.md), master plan §6.4, §8.5, game rules `MAT-5`, `MAT-9`, `STA-1`, `STA-2`

## Context

A player's profile shows goals, assists, shots, saves, cards, and a rating. A manager also wants to know what
a player does with the ball — how many passes he completes, how many defenders he beats — and the engine
recorded neither. Assists were the precedent: the event stream names only the scorer, so an assist is a fact
the simulation decides and carries on `MatchPlayerLineV1`. A pass is the same kind of fact (no event carries
one), and a take-on is the 1v1 ground duel the engine already resolves.

## Decision

**1. Two new counts pairs on the player line.** `PassesAttempted`/`PassesCompleted` and
`DribblesAttempted`/`DribblesCompleted`, each completed count a subset of its attempted one.

**2. A take-on is the ground duel.** `ResolveGroundDuel` already draws a carrier and a tackler and resolves
the duel. The carrier is credited an attempt, and a completion when the duel is won. No draw is added.

**3. A pass is a leg of the ball's approach.** The simulation plays a possession as phases, not passes, so the
passes follow from what the phases decided. Every leg of the approach the possession recorded is a pass; a
possession that fails to progress ends on a lost pass (the last leg played, or the first when none was); the
ball played on to the shot point is a pass, completed when the attack broke through and lost when creation
failed; a penalty's run into the box is a completed pass. A scramble lost is a lost 50/50, not a pass. Set
pieces, a goalkeeper's distribution, and the entry carry are not counted; a corner that sets a goal up counts as
the assister's completed delivery.

**4. Who passed is drawn from a stream of its own.** `PassTally` draws each leg's passer from the outfield
players on the pitch, from a stream derived from the match seed and the possession ordinal — never from the
play stream — for the reason `AssistPlanner` does: a draw taken from the play stream would move every decision
after it. Completed passes are weighted by `Passing`; the lost pass is weighted by one above the attribute
maximum minus `Passing`, so the better passer has the ball more and loses it less. The pass that creates a
goal belongs to the player the assist went to, so `Assists <= PassesCompleted` for every player.

**5. The season statistics store them.** `PlayerMatchStatLine`, `PlayerSeasonStatLine`, and `PlayerSeasonStat`
gain the four counts; `competition.player_season_stats` gains four `integer not null default 0` columns and
extends `ck_player_season_stats_counts` so a completed count never exceeds its attempted one. The matchday
publication accumulates them with the other columns, `RebuildDivisionProjections` includes them in its drift
check, and the profile's season summary and career totals read them (`SquadSeasonStatRow`,
`PlayerSeasonStatsResponse`). The match-statistics document is `match-statistics-v5` and the calculator
`season-stats-v2`.

**6. Versions.** `EngineVersions.Engine = 6` (`engine-v6`); the rules are untouched, so `RuleSet` stays 5 and the
rules hash is unchanged. The canonical serialization gains the four fields, so the golden output and input
hashes are re-pinned. As in ADR-0019, ADR-0051, and ADR-0053, stored `engine-v5` matches cannot be re-simulated
and their `match-statistics-v4` documents are refused, so the dev database is archived and reseeded.

## Evidence

- **Play is untouched.** The events, scorelines, and assists of 80 seeds are identical before and after (the
  event stream's SHA-256 matches seed by seed); the golden match is still 2–3.
- **Volumes read like football** (400 even matches): 333 passes a side at 79.8% completion, 49.5 take-ons a
  side at 50.1% won, and at most 63 passes by one player.
- **Skill shows through.** With every other player given `Passing` 18 and the rest 4, the gifted passers
  attempt more passes and complete a higher share (`BallPlayStatisticsTests`).
- **The migration applies** over the whole chain on PostgreSQL 16, the new columns default to zero, and the
  check constraint rejects a completed count above its attempted one.

## Consequences

**Positive**

- The profile can show pass accuracy and take-on success, for the season and the career.
- No scoreline distribution moves, so no calibration is re-opened.

**Negative**

- A take-on is the one duel per progressed possession, so a side makes about 50 of them a match against the
  ~15–20 a real side does; the rate is a consequence of the engine's structure, not a calibrated figure, and
  is shown as a count and a rate rather than compared with the real game.
- Goalkeepers record no passes, and set pieces other than a corner assist record none either.
- `engine-v5` data is archived rather than migrated, as with every engine version before.
