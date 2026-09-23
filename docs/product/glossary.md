# Glossary

Terms are defined here once, then used exactly this way everywhere: code, schema, API,
documentation, operator runbooks, and support replies. Where a term has a normative rule,
the rule reference in [`game-rules.md`](game-rules.md) is given.

---

## World and competition

| Term | Definition |
|---|---|
| **World** | The single persistent game universe containing all countries, divisions, clubs, players, seasons, finances, and history. Production runs exactly one (`WORLD-1`). Schemas still carry `world_id` so test worlds are possible. |
| **Country** | One of the six fictional national pyramids: England, Spain, Germany, Italy, France, Romania (`WORLD-2`). A country owns its divisions, clubs, and name pools. |
| **Division** | A competitive tier within a country, identified by `tier_number >= 1`. Every active division has exactly 18 clubs (`WORLD-4`). Tiers are monotonic: once created, never removed (`PYR-12`). |
| **Division-season** | The instance of a division playing one specific season. Fixtures, matchdays, standings, and season stats hang off the division-season, never off the division directly. |
| **Season** | One full competitive cycle in a country: 34 matchdays of double round-robin play, followed by a seven-day rollover (`CAL-1`, `CAL-6`). |
| **Game year** | The in-fiction year. Advances by exactly one at season rollover. Player aging and contract years follow the game year, not real elapsed time (`TIME-3`). |
| **Rollover** | The resumable workflow between seasons that finalizes standings, applies promotion/relegation, settles money and awards, expires and renews contracts, ages players, moves clubs, and generates the next schedule. |
| **Matchday** | A single round (1–34) of a division-season: nine fixtures played at one kickoff instant, locking 30 minutes earlier, publishing as an atomic unit (`CAL-10`, `MAT-7`). |
| **Fixture** | One scheduled match between a home club and an away club inside a matchday. A fixture owns its lock time, status lifecycle, team sheets, input snapshot, and result. |
| **Schedule** | The generated fixture list for a division-season: 34 rounds produced by a circle/Berger algorithm and mirrored, validated for the properties in `CAL-9`. |
| **League table / standings** | The transactional projection of published fixtures into played/won/drawn/lost, goals, points, cards, and rank, ordered by `TBL-2` … `TBL-10`. Exactly rebuildable (`TBL-13`). |

## People and control

| Term | Definition |
|---|---|
| **Account** | An authenticated identity (`auth.users`): email, credentials, status, and roles. One account owns at most one active manager profile. |
| **Manager** | The game persona attached to an account: display name, reputation, timezone, locale, and takeover cooldown. |
| **Club** | A persistent fictional organization with a squad, contracts, finances, stadium baseline, reputation, and history. A club is never owned by a manager and never resets on takeover (`WORLD-6`, `WORLD-9`). |
| **Tenure** (`ClubTenure`) | A time-bounded period during which a manager controls a club, with a control status of `active`, `inactive`, or `closed`. Control is derived from the tenure; a club has no mutable `human_manager_id` (`WORLD-7`). |
| **Human managed** | A club with an active tenure whose account is not suspended and whose release date is null. Only human-managed clubs count toward pyramid capacity. |
| **AI controlled** | A club with no active tenure, or one whose tenure is inactive/closed. AI decisions are made by the same validators as humans and receive no privileged information (`INS-12`, `FIN-15`). |
| **Inactive tenure** | A tenure that reached 14 days without a login. AI makes safe lineup and training decisions; the manager resumes simply by logging in (`OCC-2`). |
| **Vacancy** | A club in the lowest active tier with no active tenure. Takeover is permitted only in the lowest active tier (`WORLD-8`). |
| **Takeover / claim** | The atomic act of a manager taking control of an AI club, inheriting its complete existing state (`WORLD-9`). |
| **Cooldown** | The seven-day period after voluntary resignation during which the manager may not take over another club (`OCC-4`). |
| **Division provisioning** | The asynchronous creation of a new lowest tier when the current lowest tier fills with human managers, including deterministic backfill of already-played matchdays (`PYR-1` … `PYR-14`). |

## Squad and players

| Term | Definition |
|---|---|
| **Player** | A fictional athlete with generated identity, attributes, hidden potential, state, contracts, and career history. |
| **Attribute** | A displayed ability value from 1 to 20 (`TRN-4`). Technical, mental, physical, and goalkeeping families exist; there is no single authoritative "overall" rating (`§8.5` of the master plan). |
| **Hidden potential** | The immutable generation value that bounds a player's development ceiling. Never serialized to a player-facing response (`MAT-11`). |
| **State** | Condition, fatigue, morale, and match sharpness, stored in basis points from 0 to 10,000 (`TRN-5` … `TRN-7`). |
| **Condition** | Short-term freshness consumed by match minutes and restored by rest and recovery. |
| **Fatigue** | Accumulated load across matches; higher fatigue degrades performance and raises injury risk. |
| **Morale** | Bounded sentiment affected by playing time, results, contracts, and transfers (`TRN-13`). |
| **Contract** | An agreement binding a player to a club for 1–3 game seasons at a weekly wage, charged weekly after the Sunday matchday (`CON-1`, `CON-2`). |
| **Registration** | The eligibility of a player to represent a club from a specific fixture boundary. Contract and registration must agree (`SQ-6`). |
| **Unavailability** | A record preventing selection, measured in **fixtures** rather than wall-clock days: either an injury or a suspension (`DIS-1`, `DIS-5`). |
| **Emergency replacement** | A generated minimum-contract player created when a club would otherwise fall below the minimum registered squad. Audited and alerted; a safety net, not a strategy (`SQ-8`). |

## Preparation

| Term | Definition |
|---|---|
| **Tactical plan** | A club's saved formation, slot layout, roles, and team instructions. Every club has exactly one default plan (`INS-11`). |
| **Slot** | One of eleven positions in a tactical plan, with a position family, a role, and normalized coordinates (`TAC-7` … `TAC-9`). |
| **Team instruction** | One of the eight enumerated team-level settings (mentality, tempo, passing, width, pressing, defensive line, tackling, time wasting), each with a bounded effect and a counter-cost (`INS-1` … `INS-9`). |
| **Team sheet** | A club's selection for one specific fixture: exactly 11 starters and up to 7 substitutes (`SQ-4`). Either a fixture-specific draft or derived from the default plan at lock time. |
| **Team sheet lock** | The moment 30 minutes before kickoff when selections are frozen. Writes after lock affect later fixtures only (`CAL-3`, master plan §7.3). |
| **Snapshot** | The immutable, hashed input document for one fixture, containing frozen lineups, attributes, state, tactics, availability repairs, home advantage, and configuration hash (`MAT-1`). |
| **Training plan** | A club's team focus plus optional per-player individual focus. Drives the deterministic daily progression job at 02:00 UTC (`TRN-1` … `TRN-3`). |

## Matches

| Term | Definition |
|---|---|
| **Match** | The simulated output for one fixture: score, statistics, lineup participation, ordered events, and hashes, bound to a specific engine version and seed (`MAT-9`). |
| **Engine version** | The immutable identifier of the formulas, draw order, serialization, and event semantics that produced a result. Released versions are never altered (`RULE-3`, ADR-0004). |
| **Seed commitment** | A public hash of the protected RNG seed, published so a result can later be proven to come from a fixed seed (`ADR-0004`). |
| **Event** | One ordered, typed occurrence inside a match (kickoff, foul, card, injury, substitution, offside, corner, free kick, penalty, shot, save, woodwork, goal, halftime, fulltime) with actors, location, and versioned detail (`§6.6`). |
| **Commentary token** | Localizable event facts (`templateKey` + parameters) that the API renders as current English text. Commentary never reveals hidden attributes (`§8.6`). |
| **Highlight** | A semantic keyframe presentation derived from one event, with 22 players, a ball, normalized coordinates, and outcome metadata for narration (`ADR-0006`). |
| **Keyframe** | A timestamped position (and optional easing/facing/state) for one entity inside a highlight. The client interpolates between keyframes (`ADR-0006`). |
| **Publication** | The atomic transition that makes a whole division matchday's results public and applies every projection: standings, stats, discipline, injuries, fatigue, morale, gate revenue, and notifications (`MAT-7`). |
| **Bootstrap result** | A deterministic AI-vs-AI result generated during division provisioning to give a new tier a legitimate inherited table. Marked as generated history (`PYR-6`, `PYR-7`). |
| **Void / replay** | An operator-authorized remediation of a fixture that preserves the original record and reuses the original snapshot and seed unless a versioned engine fix is required (`MAT-10`). |

## Market and money

| Term | Definition |
|---|---|
| **Listing** | A seller's offer of one eligible player at a minimum fee, exposed for at least 48 hours (`TRF-1`, `TRF-2`). |
| **Bid** | An ascending offer on a listing, carrying a database-assigned `bid_sequence` used for deterministic tie resolution (`TRF-8`). |
| **Reservation** | Funds held against a club's balance to back a leading bid. Released when the club is outbid, transferred on resolution (`TRF-7`). |
| **Resolution** | The serializable transaction that validates and completes a listing: winner pays, seller is credited, registration moves, the old contract closes, and the new contract opens (`TRF-9`, `TRF-10`). |
| **Ledger** | The append-only record of every money movement, from which cash and reserved balances are exactly reconstructible. Corrections are compensating entries (`FIN-12`, `FIN-18`). |
| **Gate revenue** | Home-match income derived from tier, attendance factor, form, and stadium baseline (`FIN-3`). |
| **Emergency grant** | A logged, alerted integrity payment made only when a club cannot field a legal squad or pay the next wage run (`FIN-16`). |

## Platform and operations

| Term | Definition |
|---|---|
| **Job** | A durable row in `ops.jobs` representing a business deadline, with a unique business key, lease, retry policy, and terminal states (`ADR-0003`). |
| **Business key** | The deterministic identity of a job's business purpose (for example `fixture:{id}:lock`). Re-enqueueing it is a no-op, which is what makes handlers idempotent. |
| **Lease** | Ownership of a claimed job, with an expiry so a crashed worker's work is retried rather than lost. |
| **Outbox** | Messages written in the same transaction as domain state, then dispatched durably. The mechanism that makes cross-module side effects reliable (`MOD-4`). |
| **Idempotency key** | A client (or workflow) supplied key that makes a command safely repeatable, with the recorded request hash rejecting reuse for a different request. |
| **ETag / If-Match** | The optimistic-concurrency contract: the current `version` is exposed as a strong ETag, and conflicting updates return `412 Precondition Failed` with the changed state (`CONC-1`). |
| **Correlation ID** | A value threading one logical operation through API, jobs, database, email, and logs. Present on every audited workflow. |
| **Projection** | A derived table (standings, season stats, discipline, balances) that exists for query speed and must always be rebuildable and reconcilable from its source of truth (`§5` of data-model). |
| **Repair action** | An explicit, approved, audited operator intervention with a dry-run result and a compensating action. It never bypasses domain rules silently (`MIG`/operator rules). |
| **Dead-letter** | The state of a job whose failure is permanent and domain-level. Surfaces in operations and alerts; never silently retried forever. |
| **Read-only mode** | An emergency operational state that blocks manager writes while keeping published content available. |

## Abbreviations

| Abbreviation | Meaning |
|---|---|
| ADR | Architecture Decision Record |
| PITR | Point-in-time recovery |
| PWA | Progressive Web App |
| bp | Basis points (used for 0–10,000 state values) |
| ETag | HTTP entity tag used for optimistic concurrency |
| SLO | Service-level objective |
| OCC | Occupancy control (rule prefix) |
