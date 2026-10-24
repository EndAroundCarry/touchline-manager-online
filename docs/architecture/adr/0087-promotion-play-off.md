# ADR-0087: Promotion play-off: two up, two down, and a play-off for the third place

- **Status:** Accepted
- **Date:** 2026-10-24
- **Stage:** Promotion play-off plan (`ethereal-rolling-snail.md`)
- **Amends:** [ADR-0031](0031-season-rollover-state-machine.md) (the rollover follows the play-offs), [ADR-0083](0083-sixteen-clubs-and-thirty-matchdays.md) (the straight line of three up and three down is replaced), [ADR-0004](0004-deterministic-match-engine.md) (a shoot-out outside an unchanged engine)
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0005](0005-dynamic-pyramid-and-backfill.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0049](0049-non-production-stepped-game-clock.md), [ADR-0086](0086-matchday-publications-take-turns.md), game rules `PR-1`…`PR-17`, `CAL-6`, `DIS-5`, `FIN-4`, `TBL-13`

## Context

Promotion and relegation were a straight line: the top three of a lower league swapped with the bottom three of the league above (`promotion-relegation-v1`). ADR-0083 promised that a play-off would replace it later. The growing-pyramid plan (ADR-0088) also needs a play-off to decide the third promotion place when a league has two feeder leagues.

A play-off could not run. There were no cup or friendly matches anywhere. The engine plays exactly 90 minutes with no shoot-out. An extra match on a league matchday would change that league's table, and a club from another league makes `StandingsCalculator.Rank` throw. The rollover was enqueued at matchday 30's kickoff with a retry budget of about an hour, so it would have run before any play-off match.

The calendar already had room. Matchday 30 is a Sunday on day 68 of the 77-day cycle and the next season's first matchday is the Tuesday on day 77. Three kickoff slots are free: Tuesday day 70, Thursday day 72 and Sunday day 75.

## Decision

**1. The format.** With one feeder league (every country today) the lower league's 1st and 2nd go up and the upper league's 15th and 16th go down. Two play-off matches decide a third place: a **semi-final, lower 3rd (home) against lower 4th**, then a **final, upper 14th (home) against the semi-final winner**. If the lower club wins the final it goes up and the 14th goes down; otherwise nobody else moves. Two or three clubs move each way and a league always has 16. Each match is **single**, hosted by the better-placed club (`PR-11`, `PR-12`).

With two feeder leagues (the growing-pyramid plan, not this one) both champions go up, the parent's 15th and 16th go down, a 2nd against 3rd semi-final is played in each feeder, the two winners meet, and that winner plays the parent's 14th. The bracket type, the tie table and the movement rule carry a feeder league so that plan only adds a branch.

**2. A draw goes to a penalty shoot-out, outside the match engine** (`PR-13`). `PenaltyShootout` (`shootout-v1`, in `MatchEngine/Shootout`) is pure and static. `MatchSimulator`, `EngineRulesV2`, `EngineVersions`, the formula hash and the golden hashes are untouched (ADR-0004): a league match is simulated exactly as before, and the shoot-out is run by the publication, only when a play-off match ends level.

- It is run from the match's stored snapshot and statistics lines, so a published match always yields the same shoot-out. Players on the pitch at full time take the kicks: started or came on, and not substituted off, sent off or injured.
- Each kick reads the taker's finishing and composure against the keeper's reflexes, around the engine's penalty base of 76% and inside its bounds. The gap is read on a steeper scale (`ContestReference` 40, against 150 for the open-play penalty), because the open-play formula moves a kick by under four points across the whole skill range and a shoot-out between a great and a poor side would be a coin toss. A 20 against a 6 converts about 87% of kicks, a 6 against a 20 about 65%. The open-play `PenaltyGoalChance` is not changed, since the legacy engine uses it.
- The rolls come from the match seed on a stream of their own. Takers go in order of finishing, the goalkeeper last. Five kicks each, stopping once one side cannot catch up, then sudden death. A side with fewer players makes the other drop its weakest takers to the same number.
- **Measured and reported, not changed:** the tick engine (engine 12) plays an open-play penalty as a real shot and converted 35% of 302 penalties in 600 matches, against the formula's 77%. The two paths differ on purpose: a shoot-out is a set piece with no goal-mouth scramble. The tick engine's low penalty rate is a separate engine question.

**3. Play-off matches reuse the matchday pipeline.** `Matchday` gains a `Kind` (league, play-off). A play-off matchday sits in **its host's division-season**, at round 30 plus the slot (31 to 33, inside the existing round check): the semi-final in the lower league's, the final in the upper league's. The host is always in the matchday's division-season, so the gate receipts, team sheets, jobs, scheduler, stepped clock and resume work unchanged (ADR-0014, ADR-0049). Slots are 1 (day 70), 2 (day 72) and 3 (day 75). Slot 3 is a spare for a semi-final that was resumed late, and is where the growing-pyramid plan's extra round goes.

**4. A new `PlayoffTie` entity holds the bracket** (`competition.playoff_ties`): season, country, parent and feeder league, stage, slot, home and away club with their league and place (the final's away side is empty until the semi-final is decided), the fixture, the winner and loser, how it was decided (score or penalties) and the shoot-out score and kicks. **All ties are created when the bracket is drawn**, so "the play-offs are over" means "no tie without a winner". Unique `(season, parent, stage, feeder)` with nulls not distinct, and unique `fixture_id`.

**5. What a play-off match leaves alone, and what it does** (`PR-14`).

- Left alone: the league table and its reconciliation, the players' season statistics, and the per-matchday television, sponsor and wage postings. Wages stay in 30 parts and the matchday ordinal never reaches round 31.
- Done as normal: the gate and the snack bar go to the host; each club's fans move at its own tier; cards and suspensions are recorded in each club's **own** division-season, so a red card in the semi-final is served in the final or in next season's first match; injuries, fatigue, morale and experience.
- Every league-only query ignores play-off matchdays by filtering on the kind. `GetClubFixturesAsync` is season-wide, so the final shows for the visiting club in fixtures, the calendar and the dashboard.

**6. The draw is part of the league's last publication.** It happens in the publication of the second matchday 30 of a pair (publications take turns, ADR-0086), in the same transaction, so the semi-final exists the moment the league season ends and the stepped clock cannot jump past it. The final is created and scheduled in the publication of the semi-final, in the same transaction as the decision. An idempotent catch-up (`PlayoffDraw.CatchUpAsync`) runs when a league is activated and on each scheduler pass.

**7. A late pair plays direct moves only** (`PR-16`). If the first slot has already locked when a pair could be drawn (a league activated after the semi-finals lock, or a world already past matchday 30 when this shipped), nothing is drawn and that season the pair exchanges only 1st and 2nd against 15th and 16th.

**8. The rollover waits.** `Season.EndsAt` keeps meaning matchday 30's kickoff. The rollover is enqueued only when `now >= EndsAt` **and the season is settled**: every matchday of it is published and no tie is undecided. `AdvanceGameClock` steps a day at a time, not to the season end, while the season is not settled. The preflight treats an undecided tie as transient, like an unpublished matchday; a pyramid that cannot be moved (two feeders, or a tier with two leagues) fails permanently with a clear reason.

**9. Movement v2 is pure** (`promotion-relegation-v2`). League standings plus play-off outcomes go in; one movement per club comes out, now with source and target division and a route (direct or play-off). Direct places always move; a final's outcome moves the challenger and the 14th when the challenger wins. A drawn bracket with no outcome is an error. The final's loser is treated as relegated by every consumer that reads the next tier from the movements (standing, parachute, the relegation wage clause, fans), so none of them changed.

**10. A second league in a tier is held back** until the growing-pyramid plan can promote over a tree. `PyramidShape.OneLeaguePerTier` is `true`, so a country that needs room opens 3.1 instead of 2.2. When the plan removes the hold, `NextLeague` must fill the highest tier that has room first (2.2 before 3.2).

## Consequences

- Rule set `world-rules-v21` (the straight-line constants `PromotedPerTier` and `RelegatedPerTier` are replaced by `DirectPromotionPlaces`, `DirectRelegationPlaces` and the play-off places, slots and kicks); `promotion-relegation-v2`; `shootout-v1`. The sponsor's stay-up place becomes `ClubsPerDivision - DirectRelegationPlaces - 1`, which is still 13th, so the play-off place does not count as staying up.
- Existing results are not recomputed (`RULE-4`). A world already past matchday 30 when this ships plays that season as a late pair.
- Migration `PromotionPlayoffs`: `competition.matchdays.kind` (default `league`) and the `competition.playoff_ties` table with its checks.
- The season is now 3 slots longer in the data (rounds 31 to 33) but not in the league: a division-season has 30 league rounds, 240 league fixtures and a table of 30 matches a club.
- A dead-lettered play-off match blocks the rollover until it is resumed, and must be resumed before next season's first matchday locks on day 77. The runbook describes it.
- The match engine's 2D film, commentary and stored result for a play-off match are the normal ones; the shoot-out has no film. The match page shows the shoot-out and its kicks after the result is revealed, behind the same gate as the score, because it would give away the draw.
- Open: the tick engine's open-play penalty rate (35%) against the shoot-out's formula (about 77% at equal skill); a promotion play-off between clubs of a country with two feeder leagues (ADR-0088).
