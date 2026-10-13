# ADR-0068: Tactics in the tick engine: counter-attack, low block and time-wasting

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stage:** Engine roadmap, `i-want-you-to-eager-beaver.md` (the weaker-side plan) Milestones 0–5
- **Related:** [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0067](0067-tick-film-whole-moves-fluid-motion-and-smarter-play.md), [ADR-0004](0004-deterministic-match-engine.md), game rules `MAT-*`

## Context

Sweeps of the tick engine (300 to 500 matches a cell) showed that it rewarded attacking setups at every skill level and punished defensive ones. With a side of
18 at home to one of 16, the weaker side took 0.43 points by playing the default, 0.37 by parking the bus, 0.40 by countering and **0.98 by attacking all-out**.
Four causes were found in the code:

1. `CounterAttack` and `TimeWasting` were read only by the possession engine's ratings and the film, never by the tick engine.
2. A Defensive mentality switched the attack off (no runs in behind, shot eagerness 70, progress 90), so a ball won deep had nobody to be played to.
3. The low block did not protect the box: only the Defence family marked, at most three lanes were screened and the shape compacted 24%.
4. Committing forward cost almost nothing, because a high press and a high line left space nobody exploited and tired legs were not modelled to any effect.

The user's intent: a weaker side that defends deep, with or without the counter, earns some draws and the odd narrow win, **and nothing more**; the better
side still mostly wins; a side of 6 against 18 never gets a result; between equal sides no setup dominates.

## Decision

**1. A yardstick first.** `tickbalance` (`tools/simulation-benchmarks`) plays a fixed table of scenarios (S1 17-20 v 15-18, S2 v 13-15, S3 v 6, S4 16 v 16
in a 5 x 5 matrix, S5 the counter flag at 16 v 16, S6 the speed edge, S7 time-wasting) with both keepers fixed, and prints results, points, shots, breaks and
the minutes played. Every milestone was judged against it at 300 matches a cell (the standard error of a flag effect is about 0.11 points).

**2. The counter is a break window that depends on the opponent.** On an open-play interception or a won tackle in the side's own 60%, a side with
`CounterAttack` opens a window of up to 7 s with up to three runners that start at once and a carrier who values the ball forward. The gain is a function of
the opponent's **posture** (mentality, defensive line, pressing, summed and clamped to -4..+4, formation not counted), times a **speed factor** of the
breakers (the side's Attack and Midfield families) against the recoverers (the opponent's Defence and Midfield families), the plain mean of Pace and
Acceleration, 15% a point, no threshold. The fixed cost is a waiting penalty: a counter side that is not on a break values going forward 28 points less. An
out-ball (a man left up the pitch) was tried and dropped: it lowered the weaker side's points in every form.

**3. A low block that packs the box**, and **an outlet for a Defensive side.** LowBlock pressing, or a Deep line under a Defensive or Cautious mentality,
makes the block follow the ball deeper (compactness +6), midfielders take more of the deep line (85%), up to two midfielders mark runners after the back
line has picked and two more lanes are screened, while the ball is in the last 45% of the side's pitch. A Defensive side that wins the ball in its own half
gets a 5 s window with one runner and a mild lift in progress, with or without the counter flag.

**4. Committing forward costs legs.** Energy drains 130% faster for a side that presses high, 100% for a high tempo and 35% for each step of mentality above
Balanced; a spent player keeps 60% of his pace (was 70%) and his acceleration falls too. A default side is unaffected.

**5. Time-wasting is read.** A side that wastes time (On, or Situational while leading) holds its goal kicks 5 s longer, throw-ins 4 s and free kicks 3 s, and
its goalkeeper a caught ball 4 s.

**6. The size of the counter and low-block bonus is set at 100% of the sizes they were tuned at.** The user chose 125% after Milestone 2b; after the legs and
time-wasting changed the play, the table was re-run at 125%, 100% and 75%. At 125% the 6-skill side took 10% wins plus draws on COUNTER (cap 2%) and the
fast-breaker cell broke its caps (stronger side 54%, weaker 28%); 75% was not neutral against a balanced side. The user chose 100%.

**7. The label stays.** `engine-v12` has not been released. The golden hash was re-pinned once, at Milestone 3 (tired legs); the other milestones do not
change a fixture that sets none of the new instructions.

## Consequences

- **What works.** The counter flag is neutral against a default opponent (+0.06 points), a clear plus against an attacking one (+0.26 against all-out, +0.45
  against attacking), a minus against a deep one (-0.51), and a small minus when the opponent is quicker (-0.14 / -0.07). A side of 6 against 18 gets wins
  plus draws in 0-2% of matches. The weaker side's own all-out attack at 18 v 16 wins 15% (31% at baseline). Numbers and tables: [`match-engine.md`](../../product/match-engine.md)
  §14.24–§14.31.
- **What the plan hoped for and did not get.** A weaker side that sits deep does **not** earn more than one that plays its default (0.2 to 0.3 points against
  0.47), and all-out attack is still the best setup between equal sides (away 1.6 points against a default home side, the counter 0.84). The size of the bonus,
  the low-block levers and the legs together did not close it.
- **A cost.** The tired-legs floor is global: it thins the stronger side's play against any defensive side. A match costs about the same to play.
- Tuning remains a rules-version change: any constant in §14.4 or §14.24–§14.31 moves the hash of a fixture that uses it.

## Not decided here

- The weaker side's 0.6–0.8 points for sitting deep (S1), and the attack-versus-counter balance at equal strength (S4). Both need a lever that is not in this
  plan: either the stronger side's shots against a low block must fall, or committing forward must cost more than legs.
- The waiting cost is not gated by skill; the S3 cap holds at 100% by size alone (2% against a cap of 2%).
- The shot volume on stored squads (42.6 a match against a band of 22–28) and the 3.85 goals of `tick 500`, left over from the keeper-gift plan.
