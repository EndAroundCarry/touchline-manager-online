# ADR-0073: Shapes are decisive from kick-off, a switch recovers a third of that, and a rule may place the player it brings on

- **Status:** Accepted
- **Date:** 2026-10-18
- **Stage:** Shape study plan (`snoopy-toasting-hamster.md`), Milestones 1-7
- **Related:** [ADR-0070](0070-tick-engine-reads-the-slot-role.md), [ADR-0072](0072-match-instructions-and-their-measured-worth.md), `MAT-13`, `MAT-14`, match-engine section 14.40

## Context

The impact study (ADR-0072) found that no mid-match formation switch beat staying put. Its lab sides were uniform (every skill 16, central midfielders everywhere), so a shape could not suit a squad. The product owner wanted to know why a change of shape does not win points when every player is in his perfect position, and wanted the manager to be able to place every player in the new shape, including the substitute the same rule brings on.

## Decision

**1. Measure with squads built from the game's own position profiles, and add no out-of-position penalty.** Squads are 16 plus `PlayerAttributeProfiles.BonusFor`, a uniform-16 keeper (the profile keeper halved the goals) and a bench with natural cover for every target shape. The tick engine has no out-of-position penalty in play and none is added here: a misplaced player costs what his role costs (`TickRoleProfile`). The study reports that cost; a penalty is a later decision.

**2. What was found** (section 14.40; 160 minutes of simulation, 300-600 matches a cell, a second seed for the cells that decided something):
- Shapes are decisive from kick-off. Best to worst is 0.46 points a match (4-2-3-1 +0.20, 3-4-3 -0.26), 4-4-2 beats 4-3-3 beats 3-5-2 beats 4-4-2, and the goal difference runs from +0.40 to -0.55 a match, at or above the roughly 0.2 to 0.4 goals of xG the Premier League working paper found.
- A switch reproduces the shape (no walk-in lag, no stale per-seat state) but recovers only the minutes left: at 60' it adds about +4 shots and +0.3 to +0.4 goals after the change, and +0.1 to +0.25 points with intervals of about ±0.10. Over 23 switches against a 4-4-2 the average is -0.02 points. A switch moves a side about a third of the way to the strength of the shape it goes to.
- Placement decides it. The engine's own best fit keeps the shots of a perfect placement; a deliberate mismatch erases them; a kick-off misplacement of a striker and a centre back costs 5.7 to 6.0 shots and 0.6 to 0.7 goals a match.
- Positions carry the effect (2.1 of 3.5 shots), roles the rest (0.8), pass focus none. Extra numbers in a zone do not become chances.

**3. A rule's placements may name the bench player the same rule brings on** (`MAT-14`). The engine already made a rule's substitutions before it fitted the shape; the domain validator (`MatchInstructionValidator.ValidateOverrides`) and the editor did not allow it. The validator now accepts the incoming player and gives two **warnings** (not errors): a placed player out of position for the slot's role (`INSTRUCTION_OUT_OF_POSITION`, the main board's rule, so a secondary position counts) and a placed player who is taken off by the same rule (`INSTRUCTION_PLACED_PLAYER_LEAVES`). The web editor previews the named swaps first, marks out-of-position slots, offers the incoming players in the picker and has a Natural fit button. No engine code and no golden hash changed.

**4. The engine's automatic best fit is left as it is.** It prefers distance to natural players and leaves about three of ten off-natural, and here it reaches the same shots as a perfect placement. Changing it would move AI plans' red-card reshapes and is not supported by the evidence.

## Consequences

- A manager who plans a change of shape should expect little from it unless the new shape is a better one against that opponent and the right players stand in it: 4-4-2 to 4-3-3 pays against a 3-5-2 and costs against a 4-4-2. The Tactics page should not promise that a switch wins points, and a templated switch should name its opponent.
- **Open decisions for the product owner** (none implemented): an out-of-position penalty in the tick engine; the engine levers the study points to (a cover term in duels, pass survival that rises with an unmarked team-mate, a cross value that depends on the far post); and the wing-back profile and a defensive line height that does not rise with a back three, which would help 3-4-3 and 3-5-2, the two weakest shapes. Each moves the whole league and needs its own balance pass.
- The `tickshape` probe stays in `tools/simulation-benchmarks` and shares the impact study's paired engine, so another shape question is a cell, not a new tool.
