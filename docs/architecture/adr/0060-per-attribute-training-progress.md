# ADR-0060: Training progress is held per attribute (`training-v3`)

- **Status:** Accepted
- **Date:** 2026-10-05
- **Stage:** Stage 4 follow-up, training rework
- **Related:** [ADR-0057](0057-training-programmes-age-curve-and-hidden-aptitude.md), game rules `TRN-10`, `TRN-16`, `TRN-17`

## Context

`training-v2` (ADR-0057) kept a player's partial development in one pool, `DevelopmentRemainder`, and ageing in
another, `DeclineRemainder`. Each day's budget was added to the pool, and every whole point it produced was
given to one of the programme's attributes by a weighted random draw.

A manager could therefore see that a player was training but never how close any skill was to rising, because
that question had no answer: the pool belonged to the player, and which skill would take the next point was not
decided until the pool filled.

## Decision

**1. Progress belongs to the attribute.** `PlayerState.AttributeProgress` holds one signed value per attribute,
in millionths of a point, always strictly between minus one and one point. Positive is progress towards a gain,
negative towards a loss. It is stored as compact JSON (`[[attributeIndex, micro], ...]`, only the attributes with
progress) in `squad.player_state.attribute_progress`, so the player page can show `Finishing 19 (0.85)`.

**2. The day's budget is split by weight, not drawn.** The budget is unchanged: `BaseDailyMilli (140) x age x
aptitude x intensity x fatigue x jitter`, now computed in millionths. It is divided across the programme's
attributes in proportion to their weights (3 core, 2 important, 1 supporting). An attribute already at
`min(20, Potential)` takes no share and its share moves to the others, so the day's total is unchanged. The
integer remainder of the split goes to the heaviest attributes first. With a budget of ten points and the weights
3, 2 and 5 an attribute earns 3, 2 and 5 of them.

**3. Ageing comes off the same value.** Each attribute's decline, shielded to half while the programme trains it,
is taken from its own progress. A skill that is both trained and ageing nets the two. A point is gained when the
progress reaches +1 and lost when it reaches -1; the point is taken off and the rest is carried. An attribute at
its ceiling drops positive progress, and one at the floor drops negative progress.

**4. No random draw remains except the jitter.** `training-v2` drew a weighted pick for every point and for every
decline. `training-v3` has only the day's budget jitter and the morale drift, both seeded from the player and the
day, so a replay is still exact (`TRN-9`).

**5. The history records the split.** `squad.player_training_days.attribute_progress` holds, per day, how much of
the day's development each attribute earned and how much decline it incurred, in the same compact form. The
training tab lists it ("Finishing +0.054, Composure +0.036"). Days recorded before this change have an empty
split.

**6. The API sends progress in points.** `attributeProgress`, keyed by attribute code, is on the player profile
and on each player of the training screen, to three decimals, leaving out attributes with none. Like the
attributes themselves it is a visible quantity; potential and aptitude stay hidden (`TRN-9`).

**7. Version and migration.** `DailyProgression.Version` is `training-v3`. The migration is the expand step
(`MIG-3`): it adds the two `attribute_progress` columns and gives the retired `development_remainder` a default of
zero. `development_remainder` and `decline_remainder` stay in the table, unread and unwritten, until a contract
migration drops them. The partial points they held, under one point per player, are not carried over.

## Consequences

**Positive**

- A manager can see how close each skill is to rising, and what each day of training did for it.
- A core skill climbs faster than a supporting one by a visible, fixed ratio, so the programme's weights mean
  what they say.
- The progression has less machinery: no weighted draw and no decline draw.

**Negative and risks**

- **Slow skills move in steps.** A skill that declines a fifth of a point a season takes five seasons to lose a
  point; under the pooled draw some of them lost one earlier by chance. The ageing test runs six seasons for this.
- **Balance moved again.** The same budget now goes to the same skills in a fixed proportion, so two players on the
  same programme and age grow almost identically except for aptitude, intensity and jitter. If that proves too
  uniform, a per-attribute aptitude is the next lever.
- **History is a little larger.** Each day row carries up to ten more small pairs.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Show the player's one pool as "next skill point" | It cannot say which skill will rise, which is what the manager asked. |
| Estimate each skill's progress from the weights | It would be a number the engine never used, and it would drift from what actually happens. |
| Keep the weighted draw and track per-attribute expected progress | Two models of the same thing. The draw was the reason the pool could not be attributed. |
