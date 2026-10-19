# ADR-0075: A back three or five slides across: the near wing-back steps out and the far one tucks in

- **Status:** Accepted
- **Date:** 2026-10-19
- **Stage:** Back threes plan (`create-a-plan-for-graceful-puffin.md`), Milestones 0-5
- **Related:** [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0070](0070-tick-engine-reads-the-slot-role.md), [ADR-0073](0073-shape-matchups-and-placing-substitutes.md), [ADR-0074](0074-tick-engine-skills-fall-with-live-condition.md), match-engine sections 14.38, 14.40, 14.42

## Context

The formation research check (ADR-0073, section 14.40) found the 3-4-3 and the 3-5-2 the two weakest of eight shapes in the tick engine (-0.26 and -0.18 points a match against an average shape; the other six between -0.03 and +0.20) and the two that concede most shots. Real back threes are competitive, so four missing habits were suspected: the centre-backs set the line and the wing-backs drop into it; the wide forwards of a 3-4-3 help defend; the wing-backs give the width one at a time; the near wing-back presses while the far one tucks in. The product owner's aim was to join the pack: each back-three shape at -0.10 points or better and about 28 shots conceded a match, with neither above +0.15, and one rule for every back five (5-3-2 and 5-4-1 as well).

## Decision

**1. Keep two changes; every change keys off the `WingBack` role,** so a match between two back-four shapes is bit-identical and the tick golden hash did not move. `engine-v12` is not bumped.

**2. A winger in a side that plays wing-backs screens lanes and tracks the opposing flank man** like a wide midfielder (`TickRoleProfile.HasWingBacks`, `Screens`, `Tracks`), so a 3-4-3 defends as a 5-4-1. A 4-3-3 winger is unchanged.

**3. With the ball on a flank in the defender's middle third, the ball-side wing-back steps out to the opponent's widest free man and the far wing-back tucks in 15 m** (`TickDefensiveAI.AssignWingBackStepOut`, `LineY`, `WingBackTuckIn`): the back three slides across into a back four with a pressing wing-back. It applies to every side with wing-backs.

**4. Three levers were measured and dropped.** The line set by the centre-backs alone was worse (3-4-3 -0.35 to -0.43): the wing-backs stand ahead of the centre-backs, so excluding them lowers the line, and the plan's hypothesis that the line was too high was backwards. Giving the winger the midfield's out-of-possession depth added nothing. A wing-back flank run and a lower cross line were null or worse (3-5-2 -0.22 to -0.29), because the run's target, the carrier plus 10 m, leaves the wing-back at about 60 m, short of any cross line.

**5. The target is not met and the plan stops here.** 300 matches a cell against the six other shapes: 3-5-2 -0.23 to -0.14, 3-4-3 -0.34 to -0.21; on the full eight-shape grid both read -0.13 (were -0.18 and -0.26), neither above +0.15, the 4-2-3-1 at +0.17. Shots conceded are 31.9 and 36.1 against the target of 28. The gain is goals scored, not shots prevented. The weaker side's 5-4-1 default moved from 0.98 to 0.92 points (guard 0.10).

## Consequences

- A back three or five defends in a back four's shape when the ball is wide, and a 3-4-3's wide forwards do defensive work; the shape cycle (4-4-2 over 4-3-3 over 3-5-2 over 4-4-2) survives, and 4-3-3 takes 0.23 points from 3-4-3.
- Wing-backs cross about half as often as before (1.15 to 0.48 a match for a 3-5-2); their width with the ball was not improved.
- **Open, offered and not done:** the step-out and the tuck-in were screened together at one distance, so which carries the gain is unknown; a wing-back run aimed at a fixed X in the attacking third; raising the back five's line; any further work on shots conceded, which is the larger part of the remaining gap.
