# ADR-0057: Training programmes, an age curve, and hidden aptitude (`training-v2`)

- **Status:** Accepted. Decisions 2, 4 and 6 are superseded by [ADR-0060](0060-per-attribute-training-progress.md),
  which holds progress per attribute (`training-v3`); the programmes, age curve and aptitude stand.
- **Date:** 2026-10-04
- **Stage:** Stage 4 follow-up, training rework
- **Related:** [ADR-0012](0012-daily-progression-materialised-job.md), [ADR-0018](0018-ai-club-policy.md), [ADR-0039](0039-accessibility-baseline-and-axe-gate.md), master plan §3.9, §10.4, §11.1, game rules `TRN-1`…`TRN-17`

## Context

`training-v1` gave a club one team focus (balanced, fitness, attacking, …) and one intensity, and let a manager
point a player at an attribute family. It had four problems.

- **"Balanced" was strictly best.** Gain scaled with how many attributes a focus covered, so the widest focus
  grew the most skills and a specialised one gave up development for nothing.
- **Position was ignored.** A striker could train Handling.
- **Age stopped at 25.** Development ended abruptly, nothing ever declined, and every player trained at the same
  speed whatever their talent for it.
- **There was no history.** The player page's Training report was placeholder data, so a manager could not see
  whether a choice was working.

The goal is a model in which the manager picks a position-specific programme per player, sees exactly which
skills it trains, and sees how each player progresses over time.

## Decision

**1. Programmes replace the team focus.** `TrainingProgramme` has nine codes: `goalkeeper`, `defender`,
`wingback`, `midfielder`, `winger`, `forward`, `mental`, `physical`, and `recovery`. Each is a weighted list of
attributes (3 core, 2 important, 1 supporting), held in one static catalogue (`TrainingProgrammes`) that is the
single source of truth and is served to the client in `GET /training`, so the web app never repeats a weight.
The first six are the default for the positions they name (`TrainingProgrammes.DefaultFor`); `mental`,
`physical`, and `recovery` are chosen by hand. Programmes are **per player only**: there is no club-wide
programme. A player trains their position's default unless the manager sets an override
(`squad.player_training_focus`, now holding a programme), and clearing it returns them to the default.
Intensity stays a single club-wide setting.

**2. Daily growth does not depend on how many attributes a programme covers.** The day's budget is
`BaseDailyMilli (140) × age factor × aptitude × intensity factor × fatigue factor`, with bounded jitter, in
thousandths of an attribute point. It is spread over the programme's attributes by weight, never above
`min(20, Potential)`; a capped attribute's share moves to the others. A narrower programme therefore
concentrates the same gain on fewer skills, and "balanced" is no longer a free lunch. Intensity factors are
0.70, 1.00, and 1.35; fatigue costs nothing up to 6,000 bp and falls linearly to a factor of 0.6 at 10,000 bp,
so intense training pays now and charges tomorrow. Whole points are awarded by a weighted deterministic draw
from `Pcg32`, and the fraction carries in `DevelopmentRemainder` (`TRN-10`).

**3. An age curve that never reaches zero.** The growth factor is 1000 permille up to 19, falls 100 a year to
400 at 25, then multiplies by 0.75 a year with a floor of 20 (about 94 at 30 and 21 at 35). A 30-year-old still
improves a trained skill, very slowly.

**4. Decline, by attribute and age.** Each attribute has its own start age and yearly slope: pace and
acceleration from 28 (0.30), agility from 29, stamina and jumping from 30, strength from 31, technical skills
from 29 or 32 at 0.10 or 0.08, work rate from 31, goalkeeper reflexes from 32, the other goalkeeping skills from
34, and the rest of the mental family never. The yearly figure is
`slope × (age − startAge + 1)`, divided by 86 progression days. An attribute the current programme trains is
**shielded**: its decline is halved, so training slows ageing. Decline carries in a new `DeclineRemainder`, and
a whole point is removed by a draw weighted by each attribute's rate, never below 1.

**5. Hidden aptitude is derived, not stored.** `TrainingAptitude.For(playerId)` is a pure function seeded from
the player id: 550–1450 permille, triangular around 1000. Because nothing is stored, there is no migration, no
change to the player generator's draw order, no golden-digest change, and every existing world has an aptitude
from the first day. A high-potential player with low aptitude may never reach their potential before the age
curve flattens, and a hidden star emerges the same way. Like `Potential` it never crosses an API boundary
(`TRN-9`).

**6. History is recorded daily.** The run writes one `squad.player_training_days` row per player per progression
day (programme, intensity, development and decline in thousandths, points gained and lost, and the attribute
changes as compact `[[attributeIndex, delta], ...]` JSON). The unique `(player_id, day)` index is a second
guard on the run's idempotency. `GET /players/{id}/training?days=N` serves the regime, the days, and a
per-programme summary (with labels) to the player page's Training tab.

**7. Versions.** `DailyProgression.Version = training-v2`. A replay of the same world under `training-v2`
produces different attributes than under `training-v1`; this is the point of the change.

## Evidence

- **Calibration** (`DailyProgressionTests.Calibration_…`): a 17-year-old starting seven points under a potential
  of 18, on normal intensity for seven seasons, ends 0.00 points from potential in the trained attributes at
  average aptitude and at the quickest aptitude, and 3.10 points short at the slowest.
- **Shape tests** pin the age factor as non-increasing and never zero, the growth cap, the independence of
  total growth from programme width, Intense growing more than Normal and costing more fatigue, and at 30 a net
  change ordered physical below technical below mental with the trained attributes shielded.
- **The progression stays inside the scale** over a 40-year simulation.

## Consequences

**Positive**

- A programme is a real decision: it trades breadth for speed, and its skills are visible on the training table
  and the player page.
- Players age. Pace goes first and mental skills last, so squads turn over and a veteran's value shifts from
  legs to head.
- Talent matters without a visible number: two players with the same potential develop differently.
- The AI trains by the same rules and uses the position default (`INS-12`), so it is not exempt from ageing.

**Negative and risks**

- **Balance moved.** The mean attribute feeds valuation, renewal, and retirement, and `training-v2` changes how
  fast it moves. Those downstream rules were tuned against `training-v1` and need watching.
- **History grows without bound.** About 200,000 rows a season for the seeded world, kept in full for this
  milestone. **Follow-up:** prune or roll up days older than a few seasons.
- **Two-step migration.** The retired `team_focus` and `focus_family` columns are kept, unread, until a later
  contract migration drops them (rule `MIG-3`).
- Training injuries (`TRN-12`) are still out of scope.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Keep a club-wide programme and let players inherit it | A club-wide choice cannot be right for a goalkeeper and a winger at once; it brings back "balanced is best" as the safe pick. Per-player defaults by position give the right answer with no clicks. |
| Store aptitude as a column | It needs a migration and a backfill, forces a draw into the player generator (moving the golden digests), and gives existing worlds no value until reseeded. A pure derivation has none of that and is just as hidden. |
| Make gain scale with the number of attributes trained | That is what made "balanced" dominant in `training-v1`. |
| Stop growth at a fixed age | A cliff makes late development meaningless and invites a day-one gamble on age. A curve that flattens never reaches zero. |
