# ADR-0062: A club's stadium is a count of places, its level is read from them, and gate revenue is what they sell

- **Status:** Accepted
- **Date:** 2026-10-06
- **Stage:** Facilities milestone, after Stage 12
- **Related:** game rules `FIN-3`, `FIN-10`, `FIN-14`, `FIN-17`, `STAD-1`…`STAD-6`, [ADR-0009](0009-time-identity-and-concurrency.md)

## Context

A club's ground was one number, `world.clubs.stadium_baseline`, set at generation from the club's tier and never
changed. Gate revenue was that number times 20% times a form factor. A manager could not see the ground, could not
change it, and a club's attendance, prices and capacity existed nowhere.

`FIN-14` said in terms that no stadium spending exists in the MVP. The product now wants a stadium a manager builds
and sees: a menu entry under a new *Facilities* heading, a ground of ten levels from 5,000 to 50,000 places, four kinds
of place with their own prices, as many places as the club can pay for, and a picture that changes as the ground
grows, with the seats in the club's colour.

## Decision

**1. The ground is a count of places and nothing else.** `world.club_stadiums` holds one row per club: standing,
seating, covered and VIP places, and a `version`. The level (`ceil(places / 5,000)`, 1 to 10) and the capacity are
derived and never stored, so the level — and therefore the picture — cannot disagree with what the manager bought. A
database check keeps a ground between 1 and 50,000 places.

**2. Every club opens with 5,000 places** — 3,000 standing, 1,000 seating, 900 covered, 100 VIP — whatever its tier.
Divisions differ in how many people come to fill the ground and what they pay, not in its size. World generation opens
the ground with the club; the migration gives every existing club the same one.

**3. A manager may add any number of places, of any kind, up to the top level.** The order is priced for the club's
tier and paid from available cash (`FIN-10`) through one ledger entry, category `stadium_construction`, source
`stadium`. The command requires the ground's version in `If-Match`, so a double click, a retry or a second device is
refused (`412`) rather than paid for twice (`CONC-1`). The ledger entry's correlation key names the version the order
produced, which is the same guarantee at the money's own layer (`FIN-17`). Places are never removed (`STAD-6`).

**4. Gate revenue is what the ground sells.** For each kind of place a home match sells the lesser of the places the
club has and the crowd that wants them, at that place's price. The crowd is a tier-1 figure of 9,000 that falls by a
quarter per tier, times the existing league-position factor of 0.8 to 1.2, split across the four kinds in fixed
shares. Prices and build costs are tier-1 figures halved per tier, like every other baseline. Places nobody turns up
for earn nothing, which is what stops a manager building to the cap without looking at the crowd.

**5. The old baseline goes.** `stadium_baseline`, its constant, its check constraint and its fields in the club
responses are removed; the migration drops the column after backfilling the new table. This is a destructive step,
taken deliberately: nothing reads the column any more, and keeping it beside a real stadium would mislead.

**6. The pictures are drawn, not downloaded.** The web client draws each of the ten levels as a top-down SVG whose
geometry is a pure function of the level. A level is always the previous one with something added — a stand deepens, a
terrace becomes seats, a roof, a second tier, floodlights, a closing wall — so a manager watches one ground grow. Seats
use the club's primary colour, from the palette the match viewer already derives from the club's identity
(`ClubPalette`, now public so the stadium read can use it); a colour that is not plain `#rrggbb` is replaced before it
reaches a drawing attribute.

**7. Rules version.** `WorldRuleSet.Version` is `world-rules-v10`. The stadium values live in `StadiumRuleSet`, beside
the world rule set, and are balancing values.

## Consequences

**Positive**

- A manager has a real decision to make with their cash, with its price, its payback and its limit stated on the screen.
- Level, capacity and picture are one fact, derived once.
- No new concurrency or idempotency mechanism: the existing version, `If-Match` and ledger correlation are reused.

**Negative**

- Gate income moves. At the opening ground a tier-1 or tier-2 club takes about 41% more a home match than the flat
  baseline paid, tier 3 about 29% more, tier 4 about 13% more, tier 5 about 5% less and tier 6 about 24% less. At those
  first tiers the opening ground sells out whatever the club's form, so position moves the gate only once the ground is
  bigger than the crowd or the club is in a lower division.
- The values were set by reasoning about payback and proportion to sponsorship, not by the multi-season simulation the
  other baselines wait on. They are provisional.
- A deep-tier club cannot profitably build: its crowd is smaller than the opening ground. That is intended, but it makes
  the stadium a tier-1-to-3 decision.
- The migration's backfill identities are generated by `gen_random_uuid()`, because PostgreSQL 17 has no UUIDv7
  function. They are server-generated but not time-ordered; new grounds use UUIDv7.

## Alternatives considered

- **Keep the baseline and add a capacity beside it.** Rejected: two numbers for one fact, and gate revenue would keep
  ignoring the thing the manager builds.
- **Store a level and buy levels.** Rejected: the request was to add seats freely, and a stored level could disagree
  with the places.
- **A fixed price per seat tier-independent.** Rejected: a tier-6 club's cash is a thirty-second of a tier-1 club's, so
  an unscaled cost could never be paid and an unscaled price would triple its income.
- **Bitmap pictures per level.** Rejected: ten images times every club colour, or a recolouring step; a drawing is
  crisp at every size, costs no download, and takes the colour directly.
