# ADR-0080: Club structures: a snack bar and a merchandise shop

- **Status:** Accepted
- **Date:** 2026-10-21
- **Stage:** Structures plan (`create-a-plan-to-gentle-gizmo.md`), Milestones 1-6
- **Related:** [ADR-0062](0062-club-stadium-places-levels-and-gate.md), [ADR-0078](0078-fan-base-satisfaction-and-the-crowd.md), [ADR-0003](0003-postgresql-durable-jobs.md), game-rules `STRC-1`…`STRC-9`, `FIN-3`, `FIN-14`, `STAD-5`, `FAN-2`, `FAN-4`

## Context

A club had one building, its stadium, and one matchday income, the gate. The product wants a second Facilities page with two buildings, each on its own tab:

- a **snack bar** that earns a set amount for every spectator at a home match, lifted by up to 30% by the result and by whether the home side was the underdog;
- a **merchandise shop** that earns a set amount for every fan of the club each week, lifted by the fans' mood, with its sales and profit booked in the weekly finance run.

Each has ten levels. Both open at level 1, free. An upgrade costs cash and takes days to build, and each level adds a small weekly upkeep, so the page can show profit and not only takings. A finance rework is planned, so every number lives in one rule set.

Two written rules stood in the way. `FIN-14` said the only thing a club buys with cash besides players is stadium places, and the master plan listed "facilities beyond the stadium" and "merchandising" as non-goals. This ADR relaxes both on purpose.

## Decision

**1. Every club has both buildings, at level 1, free (`STRC-1`).** Two rows per club in `world.club_structures` (`kind` is `snack_bar` or `merchandise`, unique per club), created by the world generator and backfilled by the migration for clubs that already exist. A building holds its level, and while an upgrade is under way, the target level and the instant the work finishes. Nothing about a building is derived from another table.

**2. Only a human manager upgrades (`STRC-2`, `STRC-3`).** An AI-run club keeps both at level 1 and never builds. An upgrade is paid in full when ordered, from available cash, in one ledger entry (`structure_upgrade`, keyed `structure:{id}:v{version}`). It takes 1 to 7 days by the level it reaches. One upgrade per building at a time; the building trades at its old level until the work finishes. There is no cancelling. The order carries the building's `version` in `If-Match`, so a retried or doubled order is refused rather than paid twice, as with the stadium (`STAD-4`). Refusals: 409 `INSUFFICIENT_FUNDS`, `STRUCTURE_BUILDING`, `STRUCTURE_MAX_LEVEL` (the stadium's equivalent is a 400; the structures follow the rest of the API).

**3. The work finishes through the durable job queue.** The order enqueues `world.complete-structure-upgrade` (key `structure:{id}:level:{target}`, due at the finish time) in the same transaction that charges the club, so a lost race rolls the job back. The handler raises the level once, and writes an inbox message ("Your snack bar is now level 3", category `system`) only when a manager holds the club. A second delivery finds nothing pending and changes nothing.

**4. The snack bar is booked with the gate (`STRC-4`, `STRC-5`).** At publication, after each home gate, the club's takings are *spectators × spend for the level × (1 + result bonus)*, under the same `matchday:{fixtureId}` correlation key as the gate, in a different category (`snack_bar`). Spectators are the sum of the places sold (`StadiumRuleSet.AttendanceFor`); the gate itself is unchanged. The result bonus is judged from the home side's **frozen expectation**, the figure the fans judge the result by, with the fans' own thresholds (underdog under 1.00 expected points, favourite from 1.60): **win as the underdog +30%, win in an even game +15%, win as the favourite +5%, draw as the underdog +10%**, nothing otherwise. The cap is 30%. Where no expectation was frozen, the figure for two evenly matched sides with one at home (1.64 expected points, so a favourite) stands in, as it does for the fans.

**5. The merchandise shop and the upkeep are booked weekly (`STRC-6`…`STRC-8`).** In the Sunday 23:00 finance run, inside the same per-club loop as sponsorship and wages: merchandise right after sponsorship, *fans × spend for the level × (1 + mood bonus)*, with the mood bonus Furious 0%, Angry 0%, Unhappy +2%, Calm +5%, Content +10%, Delighted +18%, Euphoric +25%; then **one combined upkeep entry** (`structure_upkeep`) for both buildings after the operating cost. Both must be in this loop because a retried run skips a club that already has any entry under its weekly key. Upkeep is one entry, not two, because the ledger is unique on `(correlation_id, category)`. It counts toward the shortfall check that triggers the emergency grant (`FIN-16`). The fan review (23:30) follows, so the shop reads the week's closing mood.

**6. Spend per customer is the same in every tier; money baselines are not (`STRC-9`).** What a spectator or a fan spends does not depend on the tier. A lower division earns less only because its crowd and its fans are smaller. Upkeep and the cost of an upgrade are baselines like any other and are divided by the tier scaling factor. This was the user's choice on 2026-10-21; its consequence in the lower tiers is measured below.

**7. A history table feeds the page (`world.club_structure_takings`).** Append-only, one row per sale or charge: `match_sale` (snack bar, one per home match), `week_sale` (shop, one per week) and `upkeep` (one per building per week, negative), each with the level, customers, base, bonus in basis points, the bonus reason, the amount and the ledger entry it came from. The ledger alone cannot feed the page, because the upkeep of both buildings is a single entry. Partial unique indexes keep a retried publication or weekly run from writing a row twice.

**8. API and web.** `GET /structures` returns both buildings (level, spend, upkeep, the next level with its estimated change in weekly profit, any build under way, the bonus table with the current line marked, the last 8 weeks, the last 8 home matches for the snack bar, and the season's totals). `POST /structures/{kind}/upgrade` takes `If-Match` with that building's version, is refused in read-only mode, and is audited (`world.club_structure.upgrade_started`). The Structures item sits directly after Stadium in the Facilities group; the page has two tabs. The Finances page gains four labels.

**9. Rule set and schema.** The rule set advances to `world-rules-v15`. One migration, `ClubStructures`, creates the two tables, adds the four ledger categories (`snack_bar`, `merchandise`, `structure_upkeep`, `structure_upgrade`) and the `structure` source to their check constraints, and backfills level-1 rows. The season-line category check had lacked `stadium_construction` and `release_payout`, so a season that contained either would have failed at rollover settlement; the migration extends it to every category.

## What changes in the written rules

- **`FIN-14`.** Cash buys players, stadium places and structure upgrades. Loans, debt, overdrafts, owner injections and real-money purchases stay out.
- **Master plan.** "Facilities beyond the stadium" and "merchandising" leave the non-goals as far as the snack bar and the shop go. A training ground, academy buildings, naming rights and sponsorship negotiation stay out.
- **`FIN-3`, `FIN-4`, `FIN-9`.** The income and expense lists gain the snack bar (`STRC-4`), the shop (`STRC-6`) and structure upkeep (`STRC-8`).

## Measured

A short probe, with no match simulated: one season of 34 matchdays (17 home matches, 11⅓ weekly runs), both buildings at the same level, fans Calm, and a typical home season for the snack bar (5 wins in an even game, 2 as the underdog, 2 as the favourite, 1 underdog draw, 7 with no bonus, which lifts the base by about 9%). Figures are in major units; "gate" is the gate for the season at the same crowd. The probe is deleted; the figures are reproduced from `StructureRuleSet`, `StadiumRuleSet.GateRevenueMinorFor` and `WorldRuleSet`.

| Club | Level | Snack bar | Shop | Upkeep (both) | Profit | Gate | Profit as share of the gate | Cost of both from level 1 |
|---|---|---|---|---|---|---|---|---|
| Tier 1 opening (10,000 fans, 5,000 places, crowd 2,890) | 1 | 53,610 | 35,700 | 6,800 | 82,510 | 846,600 | 9.7% | free |
| | 5 | 107,219 | 83,300 | 22,667 | 167,852 | | 19.8% | 19,400 |
| | 10 | 174,231 | 142,800 | 56,667 | 260,364 | | 30.8% | 54,000 |
| Tier 1 big (40,000 fans, 20,000 places, crowd 11,560) | 1 | 214,438 | 142,800 | 6,800 | 350,438 | 3,386,400 | 10.3% | free |
| | 5 | 428,876 | 333,200 | 22,667 | 739,409 | | 21.8% | 19,400 |
| | 10 | 696,924 | 571,200 | 56,667 | 1,211,457 | | 35.8% | 54,000 |
| Tier 4 opening (4,219 fans, 5,000 places, crowd 1,264) | 1 | 23,447 | 15,062 | 850 | 37,659 | 53,322 | 70.6% | free |
| | 5 | 46,894 | 35,144 | 2,833 | 79,205 | | 148.5% | 2,425 |
| | 10 | 76,203 | 60,247 | 7,083 | 129,367 | | 242.6% | 6,750 |

For scale, a tier-1 club's sponsorship over the same season is 340,000 and its operating cost 113,333; a tier-4 club's sponsorship is 42,500 and its operating cost 14,167.

**Weeks to pay back one step, with the upkeep it adds** (snack bar / shop):

| Step | Tier 1 opening | Tier 1 big | Tier 4 opening |
|---|---|---|---|
| Level 1 to 2 | 1.9 / 2.2 | 0.4 / 0.5 | 0.5 / 0.6 |
| Level 4 to 5 | 3.0 / 3.4 | 0.6 / 0.7 | 0.7 / 0.9 |
| Level 9 to 10 | 4.6 / 5.4 | 0.9 / 1.0 | 1.0 / 1.2 |

What this says:

- **A tier-1 club at its opening numbers** earns about a tenth of its gate from the two buildings at level 1 and about 31% of it at level 10. The first step pays back in about two weeks and the last in about five. That was set on purpose on 2026-10-21: the buildings are there to help a club grow, not to hold it back, so an upgrade costs 2,000 for level 2 rising to 3,800 for level 10, and bringing both buildings from level 1 to 10 costs 54,000, about a tenth of a new club's opening cash. The build time (1 to 7 days), not the price, is what paces a manager. The first version of the table (25,000 rising to 315,000, a payback of 24 to 28 weeks at the first step and 378 to 450 at the last) was rejected as too slow.
- **A big tier-1 club** pays every step back within a week: the first step takes half a week and the last one week.
- **Every step is positive at every opening crowd**, in every tier tested (this is also a Domain test): upkeep never outgrows what the next level takes in.

## Alternatives considered

- **A fixed cost for upgrades, taking no time.** Rejected by the user: a build time makes an upgrade a plan, not a click, and gives the completion message a reason to exist.
- **Snack bar takings per match from the gate's own category.** Rejected: separate categories let the Finances page, the season summary and the new Structures page show each source on its own.
- **Booking the shop per match.** Rejected: fans do not buy a shirt on matchday only. A weekly credit also matches how Football Manager and Soccer Manager report commercial income, and reads the fans' mood once, at its week's close.
- **One upkeep entry per building.** Not possible: the ledger is unique on `(correlation_id, category)`, and a second category would split one weekly charge into two lines for no gain.
- **Letting the AI clubs upgrade.** Rejected by the user: they stay at level 1, so the buildings are something the human manager has that the AI does not, and the AI's economy is unchanged beyond level 1's takings.
- **Scaling spend per customer by tier, like ticket prices.** Offered and set aside by the user: see the consequence below.

## Consequences

- **A lower division gains far more from the buildings than a higher one.** With the same spend per customer in every tier and costs divided by 2^(tier − 1) while crowds and fans shrink only to ¾ per tier, a tier-4 club's buildings earn 71% of its gate at level 1 (tier 1: 10%), and its first upgrade pays back in half a week (tier 1: two weeks). At level 10 a tier-4 club earns 2.4 times its gate from the buildings. The AI clubs at level 1 get the same relative lift, so a lower-division AI club's income rises by about 70% of its gate with this change, and no season was simulated to see what that does to the leagues' cash. The planned finance rework should settle it. The options when it does: scale the spend per customer by tier, scale the cost of an upgrade by the club's fans instead of the tier, or accept it as a reward for managing a small club.
- **The week without a home match shows a small loss on the snack bar** (upkeep with no takings). The page shows the week as it is.
- **Upgrade cost and upkeep are provisional (`STRC-9`).** They are tables in `StructureRuleSet` so the rework changes values and not code.
- **The shortfall check counts the upkeep.** At tier 1 the upkeep alone cannot trigger a grant (sponsorship of 30,000 a week against at most 5,000 of upkeep on top of the 10,000 operating cost); a club that has already used all its cash on wages can.
- **No cancelling, no selling, no demolishing.** A building only goes up.
- **Not done, and left open:** a bigger crowd as a reason to build (the snack bar and the stadium do not interact beyond the crowd), naming rights, other buildings, and a quote of the weekly profit for several levels at once on the page.
