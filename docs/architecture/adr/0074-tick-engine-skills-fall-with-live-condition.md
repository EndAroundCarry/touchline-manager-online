# ADR-0074: In the tick engine a player's skills fall with his live condition

- **Status:** Accepted
- **Date:** 2026-10-19
- **Stage:** Tired skills plan (`at-the-moment-the-quizzical-truffle.md`), Milestones 0-5
- **Related:** [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0072](0072-match-instructions-and-their-measured-worth.md), `MAT-15`, match-engine sections 6.3, 14.29, 14.41

## Context

The match-instructions impact study (ADR-0072) found that bringing on fresh legs is worth about nothing, because in the tick engine a tired player is only slower: `TickPlayerPhysics` cuts speed and acceleration to a 60% floor, while passing, shooting, tackling, decisions and keeping stay at 100%. The legacy engine lowered skills with tiredness (`EffectiveSkill`) but now only replays stored `engine-v11` matches. The product owner asked that a player's skills fall as his in-game condition falls: about nothing while fresh (2% at 90%), 20% at 60%, and a floor of 30%.

## Decision

**1. The curve is a share of the sheet value, linear between breakpoints.** 0% at 100% condition, 2% at 90%, 20% at 60%, 30% at 40% and below. Mental skills take half; Pace and Acceleration none (the body already slows); goalkeeping skills take the full drop. The drop is taken off the 1-20 sheet value and then passes through the engine's skill curve, so a fresh player is bit-for-bit unchanged and a tired one never plays below a sheet-1 player.

**2. Skills are held in hundredths of an attribute point**, and the places where a fraction of a point disappeared (kick error and accuracy, the dribble-choice mean, the interception factor, offside carelessness, support commitment, the back-line step-up mean) are fixed-point products. Cliffs (decision interval, first-time Technique, the through-ball Vision floor, keeper reaction ticks, every ranking) stay cliffs, compared in hundredths.

**3. Skills are refreshed from the live condition once a minute, at every stoppage, after the interval's rest and at full time.** The condition is the one the match centre shows. A substitute comes on fresh; a starter who kicks off under 100% carries his drop. The physics profile, the attack edge and the legacy engine are left alone. `engine-v12` is not bumped; the tick golden hash is re-pinned.

**4. The laboratory starts at 100% condition** (it started at 80%, which under this rule would take about 11% off every skill from the first minute). All baselines were re-taken.

**5. The drain is not changed.** The census (match-engine section 14.41) shows only a high press, high tempo and the stacked all-out setups go under 60% condition in the laboratory.

**6. The result was measured and one guard moved, by design.** Whole-league figures hold (`tick 500` goals 3.04 to 3.01, completion 83.0% to 82.8%) and late-game pass completion does not fall (83-84% in every window). Fresh legs lean positive (+0.08 to +0.19 points) without being proven against a +0.25 control. The all-out attacking side against a counter-attacking side (S6b) goes from 53/15/32 to 41/19/40, outside the weaker-side guard (stronger side at least 60%, weaker at most about 25%); the cell was already outside it before the curve. The product owner accepted this on 2026-10-19: an all-out attack that tires is the intended effect, so nothing was re-tuned.

## Consequences

- A fresh substitute is worth something in the tick engine now, though the lab cannot yet prove how much; high-press and all-out attacking styles lose a little more late in a match.
- `MAT-15` states the rule. Section 14.41 holds the curve, the census, the measurements and the method.
- `TickEngineTests` weaker-side miniature's stronger-side floor is 0.55 (was 0.60); it plays on 100% legs. `TestMatchFactory` still starts at 80%, so tick unit-test matches carry an 8% drop from the first minute.
- **Open, offered and not done:** lighten the drain so only the stacked setups go under 60%; a gentler curve between 80% and 60%; re-tune the weaker-side levers for S6b.
