# Game Rules (rule set v1)

> **Status:** Settled for the public MVP. This document is the normative rule source; the
> master plan [`master-plan.md`](master-plan.md) explains intent, this file defines behaviour.
> **Applies to:** rule set version `2`, engine version `2`.
> **Change control:** see [§19](#19-change-control).

Every rule carries a stable reference (`WORLD-4`, `CAL-2`, …) so that migrations, tests,
operator runbooks, and support answers can cite the exact rule they implement.

Conventions used below:

- **MUST / SHOULD / MAY** follow the master plan's normative language.
- Times are UTC. "Game season" and "game year" are not real time — see `TIME-*`.
- Money is `bigint` minor units of a single display currency. No floating point.

---

## 1. Rule-set versioning

| Ref | Rule |
|---|---|
| RULE-1 | All tunable values live in a single versioned rule set, never as magic constants in code. |
| RULE-2 | The active rule-set version is recorded on the world, on every season, and on every match input snapshot. |
| RULE-3 | Changing a value that affects results, money, deadlines, or fairness requires a new rule-set or engine version. Released versions are never edited in place. |
| RULE-4 | Promotions, relegations, contracts, and finances already applied are never recomputed because a rule changed. |
| RULE-5 | A rule change is recorded as an audit event including actor, previous value, and new value. |

---

## 2. World model

| Ref | Rule |
|---|---|
| WORLD-1 | Production runs exactly one persistent `GameWorld`. The schema must still carry or derive `world_id` so test and future shard worlds are possible. |
| WORLD-2 | Launch countries: **England, Spain, Germany, Italy, France, Romania** (stable codes `ENG`, `ESP`, `GER`, `ITA`, `FRA`, `ROU`). |
| WORLD-3 | All club, competition, and player identity is fictional. See [`content-and-fictional-data-policy.md`](content-and-fictional-data-policy.md). |
| WORLD-4 | Every active division has **exactly 18 clubs**. There is no other size in the MVP. |
| WORLD-5 | A new world begins with **tier 1 active in all six countries**, and every club starts under AI control. |
| WORLD-6 | Clubs persist forever. Only an audited administrative repair may retire a club. |
| WORLD-7 | Managers never own club records. Control is expressed through time-bounded `ClubTenure` rows. |
| WORLD-8 | A new manager may take over a club **only in the country's lowest active tier**. Upper-tier vacancies stay under AI control until promotion/relegation changes them or a post-MVP job market exists. |
| WORLD-9 | A takeover inherits the club exactly as it exists: squad, contracts, cash, table record, fixtures, suspensions, injuries, transfer commitments, and history. Nothing resets. |
| WORLD-10 | Every active division in a country shares the same season and the same matchday calendar. |

---

## 3. Pyramid expansion

Expansion is **monotonic**: once a tier exists it is never removed, even if occupancy later
falls. See [ADR-0005](../architecture/adr/0005-dynamic-pyramid-and-backfill.md).

| Ref | Rule |
|---|---|
| PYR-1 | After every successful takeover and every tenure-status change, evaluate the country's current lowest active tier. |
| PYR-2 | If all 18 clubs in that tier hold active human tenures and no provisioning record exists for the target tier, create a `DivisionProvisioningRequest` for tier `N + 1`. |
| PYR-3 | The evaluation runs in a PostgreSQL transaction holding a **country-scoped advisory lock**. `unique (country_id, target_tier)` prevents duplicate tiers. |
| PYR-4 | A durable worker generates the new tier: 18 AI clubs, balanced squads, finance accounts, division-season entries, fixtures, and a historical baseline. |
| PYR-5 | The new division uses the **current country season** and the **same matchday calendar**. |
| PYR-6 | Matchdays already passed in the current season are simulated in sequence as deterministic AI-vs-AI **bootstrap** results, and their table/player/finance effects are applied, so the new division inherits a legitimate table. |
| PYR-7 | Bootstrap results are marked as generated history. They never appear as matches a human manager played. |
| PYR-8 | The division becomes claimable only after generation, validation, and backfill complete. Validation asserts 18 clubs, 18 legal squads (two goalkeepers each), a complete 34-round schedule, one standing per club, and standings reconciling with published bootstrap fixtures. |
| PYR-9 | If provisioning is requested while rollover holds the country lock, it targets the **next** season. The closing season is never mutated. |
| PYR-10 | If capacity is temporarily unavailable, onboarding returns a stable `CAPACITY_PROVISIONING` response with request status and a polling hint. It never partially assigns a club. |
| PYR-11 | When tier 2 fills, tier 3 provisions through the identical generic path. There is no hard-coded maximum tier. |
| PYR-12 | Losing human occupancy never deletes or shrinks a tier. |
| PYR-13 | A tier provisioned during a season participates in promotion/relegation at the next rollover. |
| PYR-14 | Each provisioning run records a generation seed and generator version; the same seed and version reproduce the same logical tier. |

---

## 4. Occupancy, inactivity, and abandonment

"Human managed" means an **active** `ClubTenure` whose account is not suspended and whose
release date is null.

| Ref | Rule | Value |
|---|---|---|
| OCC-1 | Send an inactivity warning | after **10 days** without a login |
| OCC-2 | Mark the tenure `inactive` and let AI make safe lineup/training decisions; the manager may resume simply by logging in | after **14 days** |
| OCC-3 | Close the tenure and return the club fully to AI control, after an inbox/email warning | after **21 days** |
| OCC-4 | Voluntary resignation closes the tenure immediately and starts a cooldown before another takeover | **7 days** |
| OCC-5 | Existing fixtures, auctions, bids, transfers, and financial commitments continue through inactivity and abandonment. Abandonment never rewinds club state. | — |
| OCC-6 | A suspended account loses write access immediately. An administrator may assign temporary AI control before the standard inactivity period. | — |
| OCC-7 | All OCC thresholds are configuration values. Changing one requires an audit event. | — |
| OCC-8 | An inactive tenure still counts as human occupancy for pyramid capacity purposes until it closes. | — |
| OCC-9 | A manager with an active or inactive tenure cannot claim another club. | — |

---

## 5. Season calendar

| Ref | Rule | Value |
|---|---|---|
| CAL-1 | One season is a double round-robin | **34 matchdays**; each club plays every other club once home and once away |
| CAL-2 | Standard kickoff | **19:00 UTC** on **Tuesday, Thursday, Sunday** |
| CAL-3 | Team sheets lock before kickoff | **30 minutes** |
| CAL-4 | Deadlines are displayed in the viewer's local timezone; UTC remains authoritative. | — |
| CAL-5 | The first production season begins on a **configured future Tuesday** with onboarding lead time. It is never derived from deployment time. | — |
| CAL-6 | Rollover period after matchday 34 | **7 days**; the next season starts on the first configured matchday after rollover |
| CAL-7 | All six countries share the same real-time cadence. Jobs, data, and standings remain country/division scoped. | — |
| CAL-8 | Schedules are generated with a tested circle/Berger algorithm and mirrored for the second half. | — |
| CAL-9 | Schedule validation asserts: exactly 34 fixtures per club; exactly one fixture per club per round; each pair meets once home and once away; home/away streaks acceptable. | — |
| CAL-10 | A division's matchday resolves as a unit. Individual fixtures are never published early. | — |
| CAL-11 | If simulation is delayed, the scheduled kickoff is not changed and no forfeit is invented. Operators display status instead. | — |

---

## 6. League table and tie-breakers

Points: **3 win / 1 draw / 0 loss** (`TBL-1`). Ordering is applied in this exact sequence,
first difference deciding:

| Ref | Tie-breaker |
|---|---|
| TBL-2 | Points |
| TBL-3 | Goal difference |
| TBL-4 | Goals scored |
| TBL-5 | Wins |
| TBL-6 | Head-to-head points among the tied clubs |
| TBL-7 | Head-to-head goal difference among the tied clubs |
| TBL-8 | Fewer red cards |
| TBL-9 | Fewer yellow cards |
| TBL-10 | Deterministic season draw derived from season ID and club IDs |

| Ref | Rule |
|---|---|
| TBL-11 | The final draw key is generated **before** the season, stored, and visible in competition rules. |
| TBL-12 | Database row order or insertion order is never a tie-breaker. |
| TBL-13 | Standings are a transactional projection of published fixtures and must be exactly rebuildable from them. |
| TBL-14 | A division's table is reconciled by a rebuild that recomputes it from its published fixtures; the rebuild must reproduce the live projection exactly, correct only what has drifted, and leave nothing behind. |

The ordering is shown to managers rather than described: `GET /divisions/{divisionId}/rules` returns the
points, this sequence as stable codes, and the season's stored draw — the seed, its published hash, and each
club's derived key — so `TBL-11`'s "visible in competition rules" is a page anyone who can sign in can open.
The criteria arrive as codes and the wording is the client's, the same split the commentary and inbox
templates use; the codes are the ones `StandingsCalculator` ranks with, so the page describes the order the
table actually follows.

---

## 7. Promotion and relegation

| Ref | Rule | Value |
|---|---|---|
| PR-1 | Promotion and relegation automatically apply between adjacent active tiers | **3 up / 3 down** |
| PR-2 | The lowest active tier has no relegation. | — |
| PR-3 | Movement applies to **clubs**, so a human manager stays with the club. | — |
| PR-4 | Movement occurs only during season rollover, and only after all fixtures, discipline, finance postings, and table validations are final. | — |
| PR-5 | All movements in a country are applied in one country-scoped serializable transaction, followed by creation of the next season's entries and fixtures. | — |
| PR-6 | Season-entry and history rows are immutable. A prior season's division membership is never rewritten. | — |
| PR-7 | Promoted/relegated clubs retain squad, contracts, cash, and history. | — |
| PR-8 | If a tier was provisioned during the closing season, it takes part in promotion/relegation at that rollover (subject to `PR-2`). | — |
| PR-9 | There is no promotion out of the **top active tier** of a country; there is no higher division to promote into. | — |
| PR-10 | A country with only one active tier therefore has neither promotion nor relegation in that tier until a lower tier exists. | — |

Movement is computed by a pure, versioned rule (`promotion-relegation-v1`) from each active tier's finalized
ordering. Three up and three down between every adjacent pair of active tiers means the top tier promotes
nobody and the lowest relegates nobody as consequences of there being no adjacent tier rather than as special
cases, and a club moves at most one tier. It is applied once, at rollover, by the world-scoped resumable state
machine in [ADR-0031](../architecture/adr/0031-season-rollover-state-machine.md): the season it closes and the
season it opens are distinct rows, so a finished season's membership, results, and table are never rewritten
(`PR-6`), and the movement a finished season earned is reproducible from its stored standings.

---

## 8. Squad and registration

| Ref | Rule | Value |
|---|---|---|
| SQ-1 | Generator target squad size | **22 senior players** |
| SQ-2 | Minimum registered senior squad | **18**, including **at least 2 goalkeepers** |
| SQ-3 | Maximum registered senior squad | **25** |
| SQ-4 | Match team sheet | exactly **11 starters**, up to **7 substitutes** |
| SQ-5 | Maximum substitutions per match | **5**, chosen by deterministic engine rules because matches are asynchronous |
| SQ-6 | A player has exactly one active club contract and one current registration. | — |
| SQ-7 | A player transferred after a fixture snapshot locks is eligible only for later fixtures. | — |
| SQ-8 | If expiry or an administrative repair would leave a club below the minimum, **audited emergency replacement players** on minimum contracts are created. This is a safety net, not a squad-building route; it emits an operations alert. | — |
| SQ-9 | Players below the minimum are never silently fielded. A club below minimum must be repaired before its next lock. | — |

---

## 9. Tactics

### 9.1 Formations (MVP presets)

| Ref | Formation |
|---|---|
| TAC-1 | 4-4-2 |
| TAC-2 | 4-3-3 |
| TAC-3 | 4-2-3-1 |
| TAC-4 | 4-1-4-1 |
| TAC-5 | 3-5-2 |
| TAC-6 | 5-3-2 |

| Ref | Rule |
|---|---|
| TAC-7 | Managers may drag slots within validated tactical zones but cannot create overlapping or out-of-bounds positions. |
| TAC-8 | Every slot has a position family and a role. |
| TAC-9 | Slot coordinates are stored as scaled integers normalized to 0–10,000. |
| TAC-10 | A plan names exactly eleven slots. Its default lineup names either nobody or all eleven: a plan with some but not all of its slots filled is refused, so a saved side is never short (see `SQ-4`). The client's `If-Match` version is required to save. |

### 9.2 Team instructions

| Instruction | Allowed values | Ref |
|---|---|---|
| Mentality | defensive, cautious, balanced, positive, attacking | INS-1 |
| Tempo | low, normal, high | INS-2 |
| Passing | short, mixed, direct | INS-3 |
| Width | narrow, normal, wide | INS-4 |
| Pressing | low block, mid block, high press | INS-5 |
| Defensive line | deep, normal, high | INS-6 |
| Tackling | stay on feet, normal, aggressive | INS-7 |
| Time wasting | off, situational, on | INS-8 |

| Ref | Rule |
|---|---|
| INS-9 | Every instruction has a bounded effect **and** a trade-off. No tactic may multiply team strength without a counter-cost (fatigue, space conceded, discipline risk, or reduced chance quality). |
| INS-10 | Out-of-position players receive a deterministic familiarity penalty derived from slot position family versus player position and familiarity. |
| INS-11 | A club has exactly one default tactical plan; a fixture may have its own draft team sheet referencing a plan version. |
| INS-12 | Humans and AI are validated by the identical validator. AI receives no bypasses. |

An AI-controlled club — one no manager holds — is set up by a pure, versioned policy (`ai-policy-v1`) run
by the worker's daily evaluation job. The policy draws a formation and the eight instructions from the
club's identity, picks its default eleven by position suitability, condition, ability, and stable player
ID — the same ordering `DIS-6` repairs a side with — and trains at the neutral `balanced`/`normal`. Every
plan it produces is accepted by the same `TacticalPlanValidator` a manager's save goes through, so `INS-12`
is enforced by the code path rather than by intention. It fills only a missing plan: a side or training
plan a manager or an earlier pass set is left exactly as it is, and a club a manager holds — active or
merely inactive (`OCC-8`) — is never considered.

---

## 10. Training and player state

| Ref | Rule | Value |
|---|---|---|
| TRN-1 | Team training focus values | balanced, recovery, fitness, attacking, defending, technical, tactical |
| TRN-2 | Optional individual focus selects one attribute family. | — |
| TRN-3 | Daily progression job runs | **02:00 UTC** |
| TRN-4 | Attribute scale | **1–20** displayed; training can never push a displayed attribute outside 1–20 |
| TRN-5 | Condition | **0–10,000** basis points |
| TRN-6 | Fatigue | **0–10,000** basis points |
| TRN-7 | Morale | **0–10,000** basis points |
| TRN-8 | APIs convert basis points into user-facing values; the database is authoritative in basis points. | — |
| TRN-9 | Development is deterministic from player identity, day, training plan, age curve, hidden potential, facilities baseline, and engine version. It is reproducible for the same inputs. | — |
| TRN-10 | A partial development remainder carries forward across days so that progression is not lost to rounding. | — |
| TRN-11 | Matches consume condition and increase fatigue based on minutes, intensity, stamina, and tactics. Rest and recovery restore them. | — |
| TRN-12 | Training injuries and match injuries create explicit unavailability records measured in **fixtures**, not wall-clock days. | — |
| TRN-13 | Morale reacts to playing time, results, contracts, and transfers with bounded changes. | — |

Match load is a pure, versioned rule (`match-load-v1`) over the frozen facts of a result — each player's
minutes, their stamina, their side's instructions, and the scoreline — and it is applied by the matchday
publication in the same transaction that publishes the results, alongside cards and injuries (ADR-0017).
Condition and fatigue are additive costs the daily recovery is measured against; morale moves with the
result in proportion to the minutes a player contributed, so a player who did not appear carries none of it.
Contracts and transfers are the remaining `TRN-13` inputs and arrive with the stages that own them.

---

## 11. Injuries and discipline

| Ref | Rule | Value |
|---|---|---|
| DIS-1 | Injury severities map to fixture absences | **1–6 fixtures** |
| DIS-2 | League yellow cards trigger a suspension | **5 yellows → 1 match** |
| DIS-3 | Yellow accumulation resets at season rollover. | — |
| DIS-4 | A red card produces a suspension | **1 match** in MVP rules |
| DIS-5 | Suspensions are served against the club's **next eligible league fixtures**. | — |
| DIS-6 | If a locked team sheet contains a player who became ineligible, the snapshot builder repairs it deterministically from the bench, then from reserves, using position suitability, condition, ability, and stable player ID tie-breaking. | — |
| DIS-7 | Every repair is recorded and reported to the affected manager in the inbox. | — |
| DIS-8 | An outstanding suspension that was not served by season end carries into the next season. | — |
| DIS-9 | A division's discipline — every player the season's cards have touched, with the suspension they still owe — is public game data, read from the stored accumulation and the open absences rather than kept a second time. The rows are ranked most sendings-off first, then most bookings, then name. | — |

The division's discipline is the detail behind the table's card columns (`TBL-8`, `TBL-9`), which break
ties but name nobody: a book is the season's accumulation (`DIS-2`, `DIS-4`) and the suspension figure is
read from the same open absence the publication serves (`DIS-5`), so the page cannot disagree with the side
a manager may actually name. It is read beside the table, the statistics, and the competition rules, and
neither the ordering nor the counts are stored a second time.

---

## 12. Contracts

| Ref | Rule | Value |
|---|---|---|
| CON-1 | Contract length | **1–3 game seasons** |
| CON-2 | Wages are charged | **weekly, after the Sunday matchday** |
| CON-3 | Renewal presents a deterministic server-calculated quote derived from ability, potential, age, playing time, morale, tier, and remaining term. | — |
| CON-4 | The manager accepts or declines the quote. Free-form negotiation is post-MVP. | — |
| CON-5 | A transfer closes the seller's contract and creates the buyer's contract, which was displayed before the bid was placed. | — |
| CON-6 | Expired players become free agents at rollover unless renewed. | — |
| CON-7 | Free-agent signing beyond emergency replacements is post-MVP, unless it proves necessary to keep the transfer market healthy. If enabled it uses the same timed-auction mechanism with a zero seller fee and an explicit signing wage. | — |
| CON-8 | Contract years advance at **season rollover**, never on the real-world anniversary. | — |

---

## 13. Finances

Money is stored as `bigint` minor units of one canonical in-game display currency
(`FIN-1`). Floating-point money is forbidden (`FIN-2`).

### 13.1 Income

| Ref | Source | Basis |
|---|---|---|
| FIN-3 | Home-match gate revenue | tier, attendance factor, form, fixed stadium baseline |
| FIN-4 | Weekly sponsorship credit | fixed baseline per tier |
| FIN-5 | Promotion award and final-position award | tier and final rank |
| FIN-6 | Transfer income | completed auction proceeds |

### 13.2 Expenses

| Ref | Expense |
|---|---|
| FIN-7 | Weekly player wages |
| FIN-8 | Transfer fees |
| FIN-9 | A small fixed weekly operating cost |

### 13.3 Rules

| Ref | Rule |
|---|---|
| FIN-10 | A club cannot place a bid that exceeds available cash **after existing reservations**. |
| FIN-11 | Cash and reserved funds update transactionally together with an append-only ledger entry. |
| FIN-12 | The ledger is append-only. Corrections use compensating entries; balances are never edited directly. |
| FIN-13 | Cash and reserved balances are never negative. Enforced by database check constraints. |
| FIN-14 | No loans, debt, overdrafts, owner injections, stadium spending, or user purchases exist in the MVP. |
| FIN-15 | AI clubs obey identical affordability constraints. They never receive hidden discounts or unlimited money. |
| FIN-16 | A safety job detects clubs that cannot field a legal squad or pay the next wage run and applies a **logged emergency grant** only when required to preserve competition integrity. It emits an operations alert and is tuned out through balancing. |
| FIN-17 | Every financial operation is idempotent under retry and carries a correlation key. |
| FIN-18 | Ledger replay must exactly reconstruct cash and reserved balances. |

A club's money is an append-only ledger. Every balance change is a `finance.ledger_entries` row that records
the move — a cash delta and a reserved delta, either of which may be zero — beside the balances it produced,
its category, its source, and the correlation key of the operation it belongs to (`FIN-11`, `FIN-17`).
`finance.club_accounts` is the projection those rows sum to, so opening a club's account records its `FIN-1`
opening balance as the first entry rather than writing a starting value onto the row, and no path moves a
balance without an entry (`FIN-12`, `FIN-18`). A posting is refused where it would take cash or reserved funds
below zero, or reserve more than the cash behind it (`FIN-10`, `FIN-13`); a retried operation carries the same
correlation key and category and collides with its first entry rather than posting a second time. This is the
record the income and expense sources above, the awards, and the market's reservations are added to, each
milestone contributing its own category as it lands.

### 13.4 The income and expense formulas

Each of the sources above is a named value in the rule set (`RULE-1`), so a balancing change is a version bump
rather than a hunt for a constant. The study of the six leagues staying viable over several seasons is what
calibrates the baselines; the shapes below are fixed from this version on.

- **Gate revenue (`FIN-3`)** is the club's fixed stadium baseline (already scaled to its tier) times a base
  fraction of 20%, times an attendance factor of 8000–12000 basis points that rises with league position and
  clamps at both ends. It is posted for the host club of every published fixture, inside the publication
  transaction, priced against the table that round produced.
- **Weekly sponsorship (`FIN-4`)** is a fixed tier-1 credit halved per tier below the first.
- **Weekly player wages (`FIN-7`)** are the sum of the club's active contracts' weekly wages, charged after the
  Sunday matchday.
- **The weekly operating cost (`FIN-9`)** is a small fixed tier-1 cost halved per tier below the first.
- **The promotion and position award (`FIN-5`)** is a tier-1 champion's award decayed linearly by final
  position and halved per tier, settled at rollover.
- **The emergency grant (`FIN-16`)** is not a formula: it is the exact shortfall the safety step computes when a
  club cannot cover its next wage run, recorded as its own category and source so it is visible in the ledger.

---

## 14. Scouting and transfers

### 14.1 Scouting

MVP scouting means a **global searchable player database with exact public attributes**,
filters, sorting, player profiles, and private shortlists (`SCT-1`). Attribute uncertainty,
fog of war, and staffed scouting networks are post-MVP (`SCT-2`). Shortlist notes are private
and length-limited (`SCT-3`).

### 14.2 Timed auctions

| Ref | Rule | Value |
|---|---|---|
| TRF-1 | A seller may list an eligible player with a minimum fee. | — |
| TRF-2 | Listings resolve at fixed daily resolution windows with at least **48 hours** of exposure. | — |
| TRF-3 | Auction resolution is never scheduled within **6 hours** before a matchday kickoff. | — |
| TRF-4 | Bids are ascending; the current amount and bidder count are visible. Manager identity may stay hidden until completion. | — |
| TRF-5 | A configured minimum increment is enforced. | — |
| TRF-6 | A club has at most one active bid per listing, and may raise it. | — |
| TRF-7 | The leading bid's funds are reserved transactionally; the former leader's reservation is released when outbid. | — |
| TRF-8 | Resolution uses the highest valid amount. Equal amounts resolve to the **lowest database-assigned `bid_sequence`** (earliest committed), then to the immutable bid ID. | — |
| TRF-9 | On resolution, account status, balance reservation, squad limits, seller minimum squad, active contracts, and listing status are revalidated in one **serializable** transaction. | — |
| TRF-10 | The winning club pays, the seller receives funds, registration changes, the old contract closes, and the generated new contract begins — atomically. | — |
| TRF-11 | A bid failing an invariant is cancelled or skipped per documented rules and the next valid bid is considered. Every outcome is audited. | — |
| TRF-12 | AI clubs list surplus players and bid within valuation, positional need, squad size, and budget bands. | — |
| TRF-13 | Direct offers, private negotiation, loans, swaps, clauses, installments, anti-sniping extensions, and transfer windows are post-MVP. | — |
| TRF-14 | A player with an active listing cannot be listed again. | — |
| TRF-15 | Cancelling a listing releases all active reservations and invalidates its bids with an audited reason. | — |

### 14.3 The AI market

`TRF-12` is implemented as a pure, versioned policy (`ai-market-v1`) run by the worker's daily evaluation.
An AI-controlled club — one no manager holds, active or inactive (`OCC-8`) — reads its own squad and the
listings that were already open before the day began, and decides:

- **What to list.** It trims a squad that is above `ai_market_target_squad_size`, or a family above its
  generator quota (`SQ-1`), choosing the least valuable players first — a lower squad standing, then lower
  ability, then the shorter remaining term. It never lists a player whose sale would take it below `SQ-2`,
  and it lists nobody while it already has a listing open, so one pass cannot flood the market. The asking
  price is the player's value, varied by a factor stable to the club.
- **What to bid.** It buys where it has a positional need — a family below its quota — or where the listed
  player improves on the weakest it already holds in that family; never on its own player or its own
  leading bid, never above the player's valuation, never above a bounded share of its spendable cash
  (`FIN-10`), and never once its squad is at the maximum (`SQ-3`). Each pass is bounded to a handful of
  listings and bids per club.

A player's **value** is a versioned number of weeks of the wage they command (`TRF-12`), moved by bounded
age and potential factors, so a squad, a renewal, and a fee are priced by one family of rules. The value is
class C2 and never reaches a manager-facing response (`MAT-11`); the asking price a listing shows is the
only figure derived from it that a manager sees.

The policy reads no clock, database, culture, or global random source, and every choice it makes is carried
out through the same listing and bid writers a manager's command uses, so `INS-12` holds by the code path:
an AI club is refused by the same eligibility, squad-legality, and affordability rules a human is. Every
decision is recorded in `market.ai_market_decisions` with the digest of the inputs it read and the policy
version. The value and band constants are in §18.

### 14.4 Integrity controls

| Ref | Rule |
|---|---|
| INT-1 | Clients never submit outcomes, ratings, balances, seller identity, player value, or deadlines as authoritative. |
| INT-2 | Listing and bid commands require an idempotency key and verified club ownership. |
| INT-3 | Bid IP/device-risk hashes are recorded for support analysis. Shared households are never auto-punished. |
| INT-4 | Repeated below-value transactions, reciprocal patterns, account-overlap signals, and rapid resign/reclaim behaviour create review cases. Results are never silently altered. |
| INT-5 | Listing and bid commands are rate-limited per manager and per club. |
| INT-6 | Completed market history is public enough for transparency. |

---

## 15. Match rules

| Ref | Rule |
|---|---|
| MAT-1 | Matches are simulated only by the worker, only from an immutable input snapshot, and only with a versioned engine. |
| MAT-2 | There is no public command that simulates or influences a match. |
| MAT-3 | Simulation covers 90 regulation minutes plus deterministic stoppage time, modelled as a sequence of possessions. |
| MAT-4 | Goals are produced by resolved chances, never by an independent per-minute roll. |
| MAT-5 | The final score equals the goal events. Statistics reconcile exactly with events. |
| MAT-6 | Human managers make no live in-match changes in the MVP; substitutions are chosen deterministically by the engine from the selected bench. |
| MAT-7 | A division matchday publishes atomically: either all nine fixtures or none. |
| MAT-8 | Commentary and highlights consume events and cannot change the outcome. |
| MAT-9 | Match input and output hashes are stored; re-running the same snapshot, seed, and engine version reproduces the same output hash. |
| MAT-10 | A void/replay requires an operator reason and uses the original snapshot and seed unless a documented engine defect requires a versioned remediation. |
| MAT-11 | Hidden attributes, the raw seed, internal valuations, and engine diagnostics never appear in player-facing responses. |

Detailed engine formulas (unit ratings, possession and chance resolution, bounded tactical
modifiers, and all versioned constants) live in `docs/product/match-engine.md`, which covers
engine version 2.

### 15.1 Player statistics

A published match carries each participant's assists and a match rating (`engine-v2`), and the matchday
publication advances a player's season totals from the result and its events (master plan §6.4). The line
is a projection of published results, like the table, and is read from the stored result rather than
re-simulated.

| Ref | Rule |
|---|---|
| STA-1 | A player's season statistics are advanced by the matchday publication, in the same transaction that publishes the round (`MAT-7`). A republished round advances no line twice. |
| STA-2 | The line is appearances, starts, minutes, goals, assists, shots, shots on target, saves, yellow cards, red cards, and average rating, keyed on `(division-season, player, club)`. |
| STA-3 | Goals, assists, minutes, cards, and the match rating are the engine's player line; shots and saves are counted from the match's events. The match rating is the engine's own and is never recomputed by the projection. |
| STA-4 | A player who did not take the pitch produces no line. A penalty has no assister; every other goal has exactly one. |
| STA-5 | A match rating is stored in basis points (0–10,000) and exposed on a 0.0–10.0 scale (`TRN-8`). A player who did not appear is given no rating. |
| STA-6 | A player's season statistics are reconciled by the same rebuild as the table, which recomputes every line from the division-season's published results and removes a line no published result supports. |

A player's profile carries their own line of the same projection (master plan §11.1): the goals, minutes,
cards, and rating the division leaderboard shows for them, read from the stored row rather than recomputed,
so the profile and the leaderboard cannot disagree about a player's season. A player who has not appeared
has no line, and the profile says so rather than showing a row of zeros (`STA-4`). Like the leaderboard and
the table, it is the owning manager's read — the public, unattached profile is a scouting feature.

---

## 16. Communications

What the game tells a manager, and how (master plan §6.9, §10.7, F-41).

| Ref | Rule |
|---|---|
| COM-1 | A message is addressed to the manager who held the club when the event it describes happened. An AI-controlled club is told nothing; a message outlives the tenure that received it. |
| COM-2 | A message carries a stable template key and its parameters, never stored prose. The English a manager reads is derived from those two, so a message can be re-rendered in another language without being rewritten (`MAT-8`). |
| COM-3 | A message's content is public game data — a result, a league position, a card, an injury, or a repaired side — and never a hidden value (`MAT-11`). |
| COM-4 | A message is written in the same transaction as the event it describes, so a result and the news of it become public together (`MAT-7`). |
| COM-5 | The inbox is per manager and paged by a keyset cursor, newest first. Reading is the only mutation of a message, and reading one that is already read is a no-op. |
| COM-6 | The division news feed is public game data. An item is scoped to a world and optionally to a country and a division, carries a stable template key and parameters like an inbox message, and is paged by a keyset cursor, newest first. It is written in the same transaction as the event it reports. |
| COM-7 | A deadline reminder is sent **once per round**, to every club a human holds in it, when the round's team-sheet lock falls within `reminder_lead_hours` of now. A round that has already locked or published is never reminded. |
| COM-8 | Notification preferences are per manager and govern **email only**. Every switch defaults on, and a manager who has never set one has no row and is treated as all-on. An email is written to the outbox only when the matching preference is on; the inbox message is never suppressed. |
| COM-9 | An email leaves the game asynchronously: the workflow records the intention in `ops.outbox_messages` inside the transaction that produced the event, and the worker's dispatcher sends it later, at least once (`MOD-4`). A transport fault is rescheduled; an unknown message kind, an unreadable payload, or an exhausted attempt budget is dead-lettered and alerted. Auth transactional email is the one exception and stays on the request path. |

The events that produce an inbox message are the ones the matchday, market, occupancy, and
reminder workflows decide: a club's result, its changed league position, a player's suspension,
a player's injury, a repaired side (`DIS-7`), an outbid or completed transfer, an inactivity
warning, a return-to-AI notice, a welcome, and a deadline reminder. The news feed carries the
division-provisioned, transfer-completed, and result-published events. A message that is written
but never shown is not built early.

---

## 17. Time, identity, and concurrency rules

| Ref | Rule |
|---|---|
| TIME-1 | Real instants are UTC `timestamptz`; APIs expose ISO 8601 with an explicit offset. |
| TIME-2 | Server local time is never used or stored. Time enters through `IClock`; tests use a fake clock. |
| TIME-3 | **Game seasons and game years are not real time.** Player aging, contract years, and progression advance at season rollover. |
| TIME-4 | Deadlines, kickoffs, auction windows, and inactivity are real UTC time. |
| TIME-5 | Responses return absolute deadlines plus server current time so client clock drift cannot mislead a manager. |
| TIME-6 | A compressed test clock is permitted only in non-production environments; a Production host refuses to start with one configured. |
| ID-1 | Identifiers are server-generated UUIDv7. Clients never propose identifiers for new aggregates. |
| ID-2 | Database names are `snake_case`, C# names are `PascalCase`. |
| CONC-1 | Mutable aggregates carry `version bigint`, exposed as a strong ETag, with `If-Match` required for conflicting updates. |
| CONC-2 | Club takeover, auction resolution, matchday publication, provisioning, and rollover use explicit transactions and, where required, serializable isolation. |
| CONC-3 | Financial, claim, bid, rollover, and publication operations carry explicit idempotency or correlation keys. |
| CONC-4 | Equal-amount or equal-priority outcomes are always resolved by a stable, stored, deterministic key — never by row order or arrival timing. |

---

## 18. Consolidated constant reference

Values referenced by more than one rule. Changing any value here is a rule change (RULE-3).

| Constant | Value | Ref |
|---|---|---|
| `clubs_per_division` | 18 | WORLD-4 |
| `launch_countries` | ENG, ESP, GER, ITA, FRA, ROU | WORLD-2 |
| `matchdays_per_season` | 34 | CAL-1 |
| `kickoff_utc` | 19:00 | CAL-2 |
| `kickoff_weekdays` | Tue, Thu, Sun | CAL-2 |
| `team_sheet_lock_minutes` | 30 | CAL-3 |
| `max_consecutive_home_or_away` | 4 | CAL-9 |
| `rollover_days` | 7 | CAL-6 |
| `points_win` / `points_draw` / `points_loss` | 3 / 1 / 0 | TBL-1 |
| `promoted_per_tier` / `relegated_per_tier` | 3 / 3 | PR-1 |
| `promotion_relegation_version` | `promotion-relegation-v1` | PR-1 (FIC-8) |
| `squad_target` | 22 | SQ-1 |
| `squad_min` | 18 | SQ-2 |
| `squad_min_goalkeepers` | 2 | SQ-2 |
| `squad_max` | 25 | SQ-3 |
| `starters` | 11 | SQ-4 |
| `substitutes_max` | 7 | SQ-4 |
| `substitutions_max` | 5 | SQ-5 |
| `attribute_min` / `attribute_max` | 1 / 20 | TRN-4 |
| `state_basis_points` | 0–10,000 | TRN-5..7 |
| `daily_progression_utc` | 02:00 | TRN-3 |
| `slot_coordinate_scale` | 0–10,000 | TAC-9 |
| `team_instructions` | 8 enumerated | INS-1..INS-8 |
| `injury_absence_fixtures` | 1–6 | DIS-1 |
| `injury_severity_bands` | minor 1–2, moderate 3–4, major 5–6 | DIS-1 |
| `yellow_suspension_threshold` | 5 | DIS-2 |
| `yellow_suspension_matches` | 1 | DIS-2 |
| `red_suspension_matches` | 1 | DIS-4 |
| `contract_seasons_min` / `_max` | 1 / 3 | CON-1 |
| `inactivity_warning_days` | 10 | OCC-1 |
| `inactivity_ai_days` | 14 | OCC-2 |
| `inactivity_close_days` | 21 | OCC-3 |
| `resignation_cooldown_days` | 7 | OCC-4 |
| `provisioning_poll_seconds` | 30 | PYR-10 |
| `provisioning_generator_version` | `division-gen-v1` | PYR-14 |
| `reminder_lead_hours` | 24 | COM-7 |
| `notification_preferences_default` | all on: deadline reminders, inactivity warnings, market messages, news digest | COM-8 |
| `outbox_dispatch_bucket` | 1 minute | COM-9, `MOD-4` |
| `generator_version` | `world-gen-v3` (the bootstrap: clubs, squads, and fixtures) | FIC-8, PYR-14 |
| `club_generator_version` | `world-gen-v1` | FIC-8, PYR-14 |
| `player_generator_version` | `player-gen-v1` | FIC-8, PYR-14 |
| `schedule_generator_version` | `schedule-gen-v1` | CAL-8, PYR-14 |
| `player_attr_version` | `player-attr-v1` | FIC-8 |
| `player_name_pools_version` | `player-name-pools-v1` | FIC-8 |
| `training_progression_version` | `training-v1` | TRN-9 (FIC-8) |
| `match_load_version` | `match-load-v1` | TRN-11, TRN-13 (FIC-8) |
| `ai_policy_version` | `ai-policy-v1` | INS-12 (FIC-8) |
| `match_statistics_schema` | `match-statistics-v3` (carries the player lines, the assists, and the rating) | JSN-1, TRN-11 |
| `season_stats_version` | `season-stats-v1` | STA-1 (FIC-8) |
| `match_rating_scale` | 0–10,000 basis points (displayed 0.0–10.0) | TRN-8, STA-2 |
| `match_rating_base_bp` | 6,000 | STA-5 (balancing) |
| `match_rating_win_bp` / `_draw_bp` / `_loss_bp` | 600 / 120 / 350 | STA-5 (balancing) |
| `match_rating_goal_bp` / `_assist_bp` | 1,000 / 450 | STA-5 (balancing) |
| `match_rating_save_bp` (cap 400) | 60 | STA-5 (balancing) |
| `match_rating_yellow_bp` / `_red_bp` | 350 / 1,400 | STA-5 (balancing) |
| `match_condition_cost_per_minute_bp` | 10 | TRN-11 (balancing) |
| `match_fatigue_gain_per_minute_bp` | 7 | TRN-11 (balancing) |
| `match_stamina_factor_bp` | 12,000 (stamina 1) → 8,000 (stamina 20) | TRN-11 (balancing) |
| `match_intensity_bp` | 10,000 neutral, bounded 6,000–16,000 | INS-9 (balancing) |
| `match_morale_win_bp` / `_draw_bp` / `_loss_bp` | 350 / 40 / 350 | TRN-13 (balancing) |
| `name_pools_version` | `name-pools-v1` | FIC-8 |
| `club_name_capacity_per_pool` | 1,980 (220 × 9) | FIC-4, PYR-11 |
| `squad_composition` | 3 GK / 7 defenders / 7 midfielders / 5 attackers | SQ-1 (balancing) |
| `player_age_range` | 17–34 game years | SQ-1 (balancing) |
| `ability_mean_tier1` | 13 / 20 | TRN-4 (balancing) |
| `ability_drop_per_tier` | 2 | TRN-4 (balancing) |
| `weekly_wage_minor_per_ability_squared` | 300 minor units | CON-2 (balancing) |
| `tier_scaling_factor` | 2^(tier − 1) | WORLD-4 (balancing) |
| `opening_cash_minor_tier1` | 50,000,000 minor units | FIN-1 (balancing) |
| `opening_stadium_baseline_tier1` | 25,000,000 minor units | FIN-3 (balancing) |
| `opening_reputation_tier1` | 70 / 100 | WORLD-3 (balancing) |
| `gate_revenue_base_fraction_bp` | 2,000 (20% of stadium baseline) | FIN-3 (balancing) |
| `gate_revenue_form_factor_bp` | 8,000–12,000, pivoting on ninth place | FIN-3 (balancing) |
| `gate_revenue_form_factor_step_bp` | 300 per place | FIN-3 (balancing) |
| `weekly_sponsorship_minor_tier1` | 3,000,000 minor units | FIN-4 (balancing) |
| `weekly_operating_cost_minor_tier1` | 1,000,000 minor units | FIN-9 (balancing) |
| `position_award_minor_tier1_winner` | 40,000,000 minor units | FIN-5 (balancing) |
| `weekly_finance_utc` | Sunday 23:00 UTC | CON-2 |
| `payroll_risk_weeks` | 4 | FIN-16 (balancing) |
| `listing_min_exposure_hours` | 48 | TRF-2 |
| `auction_blackout_hours_before_kickoff` | 6 | TRF-3 |
| `auction_resolution_utc` | 12:00 UTC daily | TRF-2 |
| `minimum_bid_increment_minor` | 250,000 minor units | TRF-5 (balancing) |
| `player_valuation_version` | `player-valuation-v1` | TRF-12 (FIC-8) |
| `player_valuation_weeks_of_wage` | 60 weeks of the player's wage | TRF-12 (balancing) |
| `player_valuation_age_factor_bp` | 13,000 (≤21) → 6,000 (32+) | TRF-12 (balancing) |
| `player_valuation_potential_factor_bp` | 9,000 + 150 × potential (9,150–12,000) | TRF-12 (balancing) |
| `ai_market_policy_version` | `ai-market-v1` | TRF-12 (FIC-8) |
| `ai_market_target_squad_size` | 21 | TRF-12 (balancing) |
| `ai_market_max_listings_per_club` | 3 per pass | TRF-12 (balancing) |
| `ai_market_max_bids_per_club` | 3 per pass | TRF-12 (balancing) |
| `ai_market_bid_budget_fraction_bp` | 5,000 (half of available cash) | TRF-12 (balancing) |
| `refresh_token_lifetime_minutes` | 15 (access) | ADR-0002 |
| `highlight_target_seconds` | 5–8 | ADR-0006 |
| `match_presentation_payload_budget_kb` | 750 | ADR-0006 |
| `single_highlight_budget_kb` | 75 | ADR-0006 |

---

## 19. Change control

1. A rule change requires an ADR **and** an update to this file in the same change set.
2. Changing anything that affects results, money, fairness, deadlines, or persistent history
   requires a decision before implementation (master plan §17.3).
3. Rule-set and engine versions are recorded on the world, season, and match snapshot
   (RULE-2), so any historical result can be explained by the rules that produced it.
4. `docs/product/game-rules.md`, `docs/architecture/data-model.md`, and the operations runbooks
   must stay synchronized with executable behaviour.

### Open items deliberately deferred (not blockers)

These are recorded so they are not silently invented later:

| Item | Deferred to |
|---|---|
| Calibration of the opening cash, stadium, and reputation baselines and the per-tier scaling | Stage 9 (balancing) |
| Exact gate-revenue, sponsorship, award, and wage formula constants | Stage 9 (balancing), recorded in rule set |
| Exact AI valuation and bidding bands | Specified in §14.3 and §18 (Stage 10, AI market milestone) |
| Collusion review signals (`INT-4`) and market trace views | Stage 14, with the operator surface that would read them |
| Exact training development curve constants and age curve | Stage 4/5 |
| Retirement rule specifics: an **announce-then-play** mechanism — a player has a 30% chance to announce a retirement at 32, rising 15 percentage points each season, forced by the rollover entering 37 (outfield) or 39 (goalkeepers); an announce plays one final season; gated by ability and fitness so only very good, very fit players reach the cap and the rest retire around 32–35 (outfield) and 33–36 (goalkeepers) | Stage 12 (rest of the stage) |
| Whether free-agent signing is enabled during MVP | Stage 10, decided by market-health measurement |
