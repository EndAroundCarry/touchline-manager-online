# ADR-0055: Engine-v6 puts skills where the design says they are

- **Status:** Accepted
- **Date:** 2026-10-03
- **Stage:** Engine roadmap, match-engine audit follow-up
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0013](0013-engine-arithmetic-and-scoreline-effect.md), [ADR-0053](0053-engine-v5-half-time-clock-and-restart-ownership.md), `docs/product/match-engine.md` §6.3 and §7.9

## Context

An audit of `engine-v5` (the *Match Engine Analysis & Duel Guide*) rebuilt the engine in a replica, reproduced its
own calibration targets, and found no formula that was mathematically wrong. It did find skills and instructions
that did not reach the result the way the design said they did:

1. **Team ratings were frozen between substitutions.** They were calculated at kick-off and again only on a
   substitution or a sending-off, so tiredness, morale, and the scoreline never reached them.
2. **Shooter against goalkeeper was almost flat.** The shot, save, and free-kick contests compared two numbers
   on the 1–20 scale but converted the gap with the rating-scale reference of 1,000, so the whole skill range
   moved a shot by about a third of a percentage point.
3. **Three ratings and one skill were computed and never read.** Fitness, Set pieces, and Cohesion fed no
   formula; Stamina did not slow tiring; Leadership was read nowhere; corners had no skill input.
4. **Penalties ignored skill** (a flat 76%) and the skill-based code was dead.
5. **Duels read raw skills** and ignored tiredness, position fit, and playing short, and drew any outfield
   player as the tackler.
6. **Fouls ignored Aggression and Tackling**, and the ground duel's own foul roll was thrown away.
7. **Balance constants lived outside the versioned rules.**
8. **Time wasting did less than it said.**

Wherever more than one fix was possible, the product owner chose between the options.

## Decision

`engine-v6` / `engine-rules-v6`:

1. **Ratings refresh once a minute**, both sides together. (The owner chose every minute over every possession.)
2. **A separate `ShotContestReference` = 150** for the shot, save, penalty, and free-kick contests, so finishing
   and goalkeeping can be tuned without touching the duel curve.
3. **Tiredness lowers skills.** The drop grows linearly with the condition lost: physical −40%, technical −20%,
   mental −10% at zero condition. Goalkeepers are exempt. The all-round condition factor (0.85–1.05) is gone, so
   tiredness is not counted twice. A player's own **Stamina** scales his condition loss.
4. **One definition of effective skill** (`EffectiveSkill`) read by the ratings and by every duel: position fit,
   tiredness, fatigue, morale, sharpness, and a milder penalty for each man a side is short.
5. **Duels pick their players by band** (carrier mostly midfield and attack, tackler mostly defence and
   midfield).
6. **A lost ground duel can end in a foul**, replacing part of the per-possession foul chance so a match still has
   about 21 fouls, and **Aggression raises and Tackling lowers a side's foul rate**, each bounded to ±15%.
7. **Penalties depend on taker and keeper** (Finishing and Composure against Reflexes, 55%–94%).
8. **Corners have a taker** (Set pieces and Crossing); his delivery moves the header contest and the chance a
   corner is headed at goal. **Leadership** scales morale shifts. The **Set pieces, Fitness, and Cohesion
   ratings are removed.**
9. **Time wasting is score-aware** (situational applies only while ahead) and **lengthens the wasting side's
   possessions** while it applies.
10. **Duel weights, the 15%–85% clamp, the tackling nudge, the shot-zone mix, and the penalty band move into
    `EngineRulesV2`**, so they are covered by the configuration hash. Seven constants nothing read were deleted.

Retuned to hold the calibration: `RatingDifferentialReference` 1,000 → 950, `HomeAdvantageBasisPoints` 10,380 →
10,420, `BaseShotGoalBasisPoints` 845 → 865, `BaseFoulBasisPoints` 1,100 → 780, `PenaltyFromFoulBasisPoints` 120 →
40, `ShortHandedPenaltyBasisPoints` 6,400 → 6,700.

## Consequences

- Every output hash changes. A database seeded under `engine-v5` must be archived and reseeded; the golden hashes
  in `DeterminismTests` are re-pinned.
- Measured over 20,000 matches the bands hold (2.90 goals, 21.4 fouls, 0.24 penalties, home advantage +4.0
  points); see `match-engine.md` §12.
- The rating weight table is `engine-ratings-v2` and the tactical table `engine-tactical-v2` (the dead Fitness
  and Set pieces deltas are gone).
- A three-point favourite is upset 17.2% of the time, against 16.4% before; the difference is inside the sample's
  standard error.
- Open questions the owner may revisit: how steep the shot contest should be (`ShotQualitySwingBasisPoints`),
  and whether Work rate should cost condition.
