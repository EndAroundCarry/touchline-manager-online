# ADR-0058: Engine-v8 lets a manager direct the ball through the centre and a flank (the pass focus)

- **Status:** Accepted (engine layer; the manager-facing instruction is not yet exposed)
- **Date:** 2026-10-04
- **Stage:** Engine roadmap, tactical instructions
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0055](0055-engine-v6-skills-where-the-design-says.md), [ADR-0056](0056-engine-v7-passes-and-take-ons.md), game rules `INS-9`, `MAT-5`

## Context

A manager can say how wide the team plays, but not where the ball should go. Passes were not steerable: the
lateral position of each possession's pressure point, where the defender engages and the approach ends, was
a uniform draw across the pitch, so the engine had no concept of a lane. The request was an instruction for the
centre alone, the centre and the left or right flank, or both wings, that sends more of the ball that way, with
measured shares of about 20/60/20, 37/37/20 and 37/20/37.

## Decision

**1. A ninth team instruction, `PassFocus`.** `Balanced` (the default), `Centre`, `CentreAndLeft`,
`CentreAndRight`, `Wings`. Left and right are the attacking side's own, as in the shot zones.

**2. The focus remaps positions the engine already draws.** `PassagePlanner.FocusLateral` reads a uniform
lateral position as a point in the cumulative share of three lanes (left below `PassLeftLaneMaxYBasisPoints`,
right above `PassRightLaneMinYBasisPoints`) and rescales it into the lane it falls in, in integer arithmetic. It
is applied to the pressure point's draw and to each touch of the approach, which begins wherever the possession
started, mostly in the middle: remapping the destination alone moved the measured shares only a few points,
because a middle start pulls every intermediate touch back. No draw is added or removed, so a `Balanced` side
consumes the stream exactly as `engine-v7` did.

**3. The lane shares are rules, calibrated to a measured result.** The shares are the proportions of the
uniform position given to left, centre and right: centre alone 30/40/30; centre and a flank 42 favoured flank,
27 centre, 31 other flank; both wings 46/8/46 (`PassFocusCentreFlankPercent` and its six siblings, each set
validated to sum to 100). They are not the shares of the ball. They were swept until the ball, measured, sat at
about the shares the manager asked for.

**4. Scope.** This steers the ball's geometry. It does not change who is credited with a pass: `PassTally`
still weights the passer by `Passing` alone, and the film still picks the receiver nearest the ball's
destination. Application, API, persistence and the tactics board do not expose the instruction yet; a side
built by the application plays `Balanced`.

**5. Versions.** `EngineVersions.Engine = 8` (`engine-v8`), `RuleSet = 7` (`engine-rules-v7`). The canonical
serialization gains `passFocus` per side and the rules gain nine constants, so the golden input and output
hashes and the rules hash are re-pinned. The golden match is still 2-2. As with every earlier version, stored
`engine-v7` matches cannot be re-simulated and the dev database is archived and reseeded.

## Evidence

- **The suite passes unchanged in shape.** The full engine suite (883 tests, including the calibration and
  distribution bands) passes with the rules re-pinned.
- **The ball moves where asked.** Over 60 seeded matches the share of a side's passes and crosses down the left,
  centre and right is, measured: `Balanced` 23/53/23; `Centre` 19/61/20; `Wings` 39/22/39; `CentreAndLeft`
  37/44/20; `CentreAndRight` 19/44/37. A `Balanced` side is already central because possessions start in the
  middle. `PassFocusTests` pins each within three points; it also checks that left and right mirror, that the
  away side is steered towards its own left, and that a side's focus does not move the other side's ball.
- **It changes where the ball goes and nothing about the result.** Over 200 matches per option, goals and shots
  for and against are identical to `Balanced` (1.42 for, 1.29 against, 13.9 shots for, 11.2 against): nothing in
  the engine reads a lateral position to decide an outcome.

## Consequences

**Positive**

- The engine now has a lateral axis a tactic can use, and `Balanced` sides replay unchanged.
- The mechanism is one remap of an existing draw, so more lane shapes (wide-only, a three-way mix) are a new
  enum value and a row of shares.

**Negative**

- **The instruction is cosmetic until something reads the lane.** Because goals and shots are unchanged, a manager
  gains nothing from choosing a focus today. Before exposing it, either let the lane matter (flank play changing
  crosses, shot zones or turnovers, with a cost on each side as `INS-9` requires) or present it as a style
  preference only.
- The shares are calibrated to a measurement that depends on where possessions start, so a change to restarts,
  clearances or the approach geometry moves the measured shares and wants the sweep repeated (`PassFocusTests`
  fails when it does).
- Passes received, and the per-player stats, do not follow the lane. Showing "who got the ball" needs a receiver
  count on the player line, a separate change.
- `engine-v7` data is archived rather than migrated.
