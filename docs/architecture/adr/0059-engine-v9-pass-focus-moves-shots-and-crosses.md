# ADR-0059: Engine-v9 makes the pass focus move the shots and the crosses

- **Status:** Accepted
- **Date:** 2026-10-05
- **Stage:** Engine roadmap, tactical instructions
- **Related:** [ADR-0058](0058-engine-v8-pass-focus.md), [ADR-0004](0004-deterministic-match-engine.md), game rules `INS-9`

## Context

ADR-0058 let a manager send the ball through the centre, a flank, or both wings, and exposed it on the tactics
board. It steered only the geometry of the passes: nothing in the engine read a lateral position to decide an
outcome, so a manager who asked for the wings saw the ball go wide and nothing else change. The request was for
the focus to mean something on the pitch: more crosses from the favoured side, and more shots from it, in
shares like the passes (centre about 20/60/20, wings about 38/24/38, centre and left about 37/43/20).

## Decision

**1. The shot zone follows the focus.** The roll that picks an open-play shot's zone is the one the engine
always took (`derived.NextInt(100)`); only the shares it is read against change. A side with no preference keeps
40 central, 20 per inside channel and 10 per wide zone. The centre alone is 54 central, 15 inside and 8 wide on
each side; the centre and a flank is 34 central, 28 inside and 15 wide on the favoured side, and 15 inside and 8
wide on the other; both wings is 12 central, 29 inside and 15 wide on each side. Each set is validated to sum to
100. The shares are calibrated, like the pass lanes, to a measured result: corners, free kicks and penalties are
always taken from the middle, so the measured central share is about 13 points above the rules'.

**2. Crosses are delivered from the flank, for every side.** The share of a possession's final approach that is
crossed depends on the lane the ball arrives in: 38.5% in a flank lane (`CrossShareFlankLaneBasisPoints`) and 7%
down the middle (`CrossShareCentreLaneBasisPoints`), chosen so a side with no preference crosses about as often
as before (28% of approaches) but from the flanks, not from anywhere. The roll is the one the engine always
took. The pass focus already decides which lane the ball arrives in, so both wings cross about a quarter more
often, almost only from the flanks.

A side that plays through the middle crosses from the middle too, so the shares also depend on the focus. They were
set to the cross lanes asked for, measured left/centre/right: the centre alone 24/52/24 (21% from a flank lane, 34%
from the centre lane), the centre and left about 54/31/15 (37% from the left, 33% from the centre, 14% from the
right). Both wings and a side with no preference keep the two lane shares above. The five focus shares are
`CrossFocusCentreFlank`, `CrossFocusCentreCentre`, `CrossFocusPairFlank`, `CrossFocusPairCentre` and
`CrossFocusPairOtherFlank`.

**3. The focus pays for where it shoots.** A zone is not worth the same: the centre scores 1.5 times the base,
an inside channel 1.0 and a wide zone 0.8. A side that moved its shots to the centre would simply score more,
and one that moved them wide would score less, so each focus also scales how often a progressed possession
becomes a shot (`ChanceVolume*BasisPoints`: centre 9,350, centre and a flank 10,300, both wings 11,600; no
preference 10,000). The values were set so goals for come out within about 2% of a side with no preference.
The result is a choice of style, not an upgrade: the centre takes about 7% fewer shots at about 11.4%
conversion, the wings about 13% more at about 9.4%.

**4. Versions.** `EngineVersions.Engine` = 9 (`engine-v9`), `RuleSet` = 8 (`engine-rules-v8`). The rules gain
twenty-one constants and lose `CrossShareOfPassageBasisPoints`, so the rules hash and the golden hashes are
re-pinned; the golden match is still 2-2. A side with no preference scores and shoots as it did
under `engine-v8`; its crosses are the part that moves, from anywhere to the flanks. A snapshot frozen under
`engine-v8` is refused by name, so the dev database is reseeded as with every earlier version.

## Evidence

Over 1,500 to 2,000 matches each (every cross of the approach, 1,500 for the crosses), the home side playing the focus against a side with no preference (events, with the
corners, free kicks and penalties included in the shot lanes):

| Focus | Goals | Shots | Conversion | Shot lanes L/C/R | Crosses per match | Cross lanes L/C/R |
|---|---|---|---|---|---|---|
| No preference | 1.57 – 1.33 | 14.9 – 12.3 | 10.6% | 26/47/26 | 17.5 | 46/8/46 |
| Centre | 1.59 – 1.32 | 13.9 – 12.3 | 11.4% | 20/60/19 | 16.4 | 24/52/24 |
| Centre and left | 1.58 – 1.34 | 15.3 – 12.4 | 10.3% | 38/41/20 | 18.0 | 54/31/15 |
| Centre and right | 1.58 – 1.34 | 15.3 – 12.4 | 10.3% | 20/41/38 | 18.0 | 15/31/54 |
| Wings | 1.58 – 1.36 | 16.8 – 12.6 | 9.4% | 39/21/39 | 22.1 | 49/2/49 |

`PassFocusShotsAndCrossesTests` pins the direction and the order of each: the zone shares sum to 100 and mirror
for left and right; the centre takes most of its shots from the middle; a left focus shoots and crosses more
from the left; both wings take more shots than the centre and cross more, almost only from the flanks; goals
stay within 15% of a side with no preference while each shot is worth more from the middle than from wide; and
one side's focus does not move the other side's shots.

## Consequences

**Positive**

- The instruction has a visible effect: the shot map and the crosses on the film follow the lane a manager asks
  for, and the choice has a price in shot quality, shot volume and (through `TacticalModifiers`) shape.
- Every roll is one the engine always took, so no draw moved; the change is thresholds only.

**Negative**

- **A cross is still a display fact.** A crossed approach is recorded and shown, but a cross does not make a
  header or change the chance that follows it; the shot that follows is the same strike from the same zone. A
  cross that creates a headed chance is a separate change with its own calibration.
- The chance volumes are calibrated to parity at the current zone multipliers and rating differentials; a change
  to either moves the goal rate of a focused side first, and `PassFocusShotsAndCrossesTests` (goals within 15%)
  is the guard.
- A side with no preference now crosses almost only from the flanks, so its films show different crosses than
  under `engine-v8`, though its goals and shots are the same.
- `engine-v8` data is archived rather than migrated.
