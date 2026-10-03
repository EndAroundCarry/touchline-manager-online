# ADR-0032: Rollover continuity — contract expiry, retirement, awards, and season finance summaries

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 12
- **Related:** [ADR-0022](0022-append-only-club-ledger.md), [ADR-0023](0023-income-expenses-and-gate-revenue.md), [ADR-0030](0030-provisioning-execution-and-bootstrap-provenance.md), [ADR-0031](0031-season-rollover-state-machine.md), game rules `CON-1`–`CON-10`, `SQ-8`, `FIN-5`, `PYR-9`, master plan §3.6, §3.11, §6.8, §7.5, §16 Stage 12

## Context

ADR-0031 shipped the rollover's spine: one world-scoped job advancing a checkpoint row through
`started → frozen → finalized → moved → completed`, closing the season's entries and moving clubs between
tiers. It left the season's *continuity* — what happens to the people and the money when one season ends and
the next begins — as "the rest of Stage 12". Three domain methods existed with no caller
(`Player.ReleaseToFreeAgency`, `Player.Retire`, and `LedgerPostings.PositionAward`), `PlayerStatus.FreeAgent`
and `PlayerContractCloseReasons.Retired` were stored states nothing produced, `WorldRuleSet` had no
retirement rule, and `finance.club_season_finances` was a planned table that did not exist.

The questions this ADR settles are: where in the state machine the squad work belongs, how contracts expire
and who renews them, how retirement is decided, how a club is kept legal when expiry thins it, where the
award is settled, and how a season's money is summarized.

## Decision

**1. One new `Squads` phase, between `Finalized` and `Moved`.** The rollover becomes
`started → frozen → finalized → squads → moved → completed`. Contracts are resolved before clubs are moved,
which is the plan's §7.5 order (settle finance, resolve contracts, then move). The phase **find-or-creates the
next season first**, because an emergency replacement's registration carries `effective_season_id`, and the
move phase then finds the season already created rather than making a second one. The `Move` transition's
guard moves from `Finalized` to `Squads`; `Complete` is unchanged. The phase is resumable and idempotent like
every other: a repeated phase run is a no-op by the checkpoint, and each sub-step is guarded (a closed
contract is not closed again, a replacement is only generated while the club is short).

**2. Expiry follows `CON-6` literally.** A contract whose `end_season_number` is the closing season and that
was not renewed **expires**: the contract closes `expired`, its registration ends, and the player becomes a
`free_agent`. A present manager who did not renew loses the player; free-agent signing is post-MVP (`CON-7`),
so an expired player is out of the world's squads for now. This is the rule as written, and the emergency
replacement path is what keeps the club legal afterwards.

> **Amended (`CON-11`):** a present manager's expiring contracts are now renewed by the board for two seasons, so no
> manager loses a player to expiry. Release to free agency applies only to the surplus of clubs nobody manages.

**3. A club nobody manages renews by a pure, versioned policy.**
`AiContractPolicy` (`ai-contract-v1`) is a deterministic function of a squad's shape: it renews the best
expiring players up to `AiContractTargetSquadSize`, then extends renewals only as far as legality requires
(the minimum registered squad and the goalkeeper minimum, `SQ-2`), and releases the rest. "Nobody manages"
means no tenure, **or** an inactive tenure — the AI already fills an inactive manager's gaps (`OCC-2`), and
renewal is the same kind of safe decision. A present manager's club is never auto-renewed.

**4. Retirement is announce-then-play, and its numbers are secret.** `RetirementPolicy` (`retirement-v1`) is a
pure, seeded function: a player past `RetirementAnnouncementStartAge` may announce, with a chance that rises
each season and is gated by ability and condition; a forced announcement age and a forced retirement age
(outfield and goalkeeper separately) guarantee the cap; and an announced player retires at the next rollover.
The announcement is stored on `squad.players.retirement_announced_season_number` and surfaced to the manager
as an **inbox message** — the manager knows an announcement is possible from the start age and that the
chance grows, but never by how much, so the constants live in `WorldRuleSet` and are never serialized. A
player who announced and whose contract is expiring is renewed for **one** season so their final season is
actually played.

**5. Emergency replacements are a deterministic repair, not a route.** `SQ-8` is implemented as a
`PlayerGenerator` single-player generation on its own seed stream: when expiry or retirement leaves a club
below the minimum registered squad or the goalkeeper minimum, replacements are generated until it is legal,
on one-season contracts. Each repaired club is audited
(`SquadAuditActions.EmergencyReplacement`) and logged as an operations alert, because this is a safety net.

**6. The final-position award is settled inside the finalize transaction.** `LedgerPostings.PositionAward`
has waited since Stage 9 for its caller; it is now posted for every closing club, through the club account, in
the same transaction that closes the season's entries. It is posted *after* the entry-close loop reads each
club's cash, so `ClubSeasonEntry.ClosingCashMinor` stays the figure the season actually ended on and the award
becomes the first money of the next season. Idempotence is structural: the phase checkpoint, and the ledger's
`unique (correlation_id, category)` on `rollover:{seasonId:N}:{clubId:N}` (`FIN-17`). The correlation key uses
the compact GUID form because the readable form would exceed the ledger's 80-character limit — a limit the
quiet, unused factory had never met.

**7. A season's money is summarized once, at rollover, for reporting only.**
`finance.club_season_finances` records a club's opening and closing cash for a season, and
`finance.club_season_finance_lines` records one signed total per ledger category. It is written in the
finalize transaction from committed ledger data over the season's own window
(`[starts_at, rollover_ends_at)`), so a category total cannot be a half-written number and the settlement
award (posted in the same transaction, after the window) is deliberately outside it. It is a **record**:
this milestone adds no read API or screen.

**8. Provisioning requested during the rollover targets the next season (`PYR-9`).** `CapacityEvaluator` and
the diagnostics trigger now resolve the current season; if it is not being played (it is `Rollover`), they
target `CurrentSeasonNumber + 1`. If the next season does not exist yet — the rollover has not reached its
move phase — the request is deferred rather than pointed at the closing season, and the next capacity
evaluation retries it. The closing season is never mutated.

## Consequences

**Positive**

- The whole season boundary is one resumable, idempotent checkpoint machine: contracts, retirements, awards,
  and summaries commit with the phase that reached them, and a redelivered rollover moves nothing twice.
- Squads stay legal by construction: an unmanaged club renews, and any club expiry thins is repaired and
  alerted, so no club ever starts a season below `SQ-2`.
- A player's retirement is a fact the manager sees and plans around, while the probability stays a secret —
  the shape the rules ask for.
- The historical season's money is captured once and immutably, and closing cash is unchanged from what
  `PR-4` already recorded.
- The two AI policies (`ai-contract-v1`, `retirement-v1`) and the generator are pure and versioned, so a
  season's squad movements are reproducible from the labels.

**Negative**

- The `Squads` phase reads every club's contracts, players, attributes, state, and season appearances in one
  pass and can write a whole world's contracts, so it is heavy and holds the world lock. The volume is
  bounded by the pyramid and observable through the phase row and the log's counts.
- The season finance summary is a new pair of tables with no reader yet; a later milestone adds the surface.
- Expired human players becoming unattached with no signing path is a deliberate gap until free-agent
  signing lands; a manager who lets contracts lapse will feel it in the squad and be repaired only to the
  minimum.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Do the squad work inside `MoveAsync` rather than a new phase | Mixing "resolve contracts" with "create and populate the next season" hides two resumable steps in one checkpoint; a separate phase is a finer resume point and matches §7.5's order. |
| Put the `Squads` phase after `Moved` so the next season already exists | The plan orders contract resolution before movement; find-or-creating the next season at the top of the phase satisfies the registration's `effective_season_id` need without reordering. |
| Auto-renew every club's expiring players | Diverges from `CON-6`: the expiring year is the manager's decision, and auto-renewing it removes the reason to plan contracts. Only clubs nobody manages are renewed. |
| A simpler fixed-age retirement with no announcement | The rules specify announce-then-play; a fixed cap would retire players with no notice and no final season, which is the behaviour the rule exists to avoid. |
| An immediate (unannounced) retirement at the forced cap | Breaks "an announcement plays one final season"; the cap is expressed as a forced announcement one season earlier instead. |
| Include the settlement award in the season finance summary | It would make the summary's closing cash disagree with `ClubSeasonEntry.ClosingCashMinor`; the award is the next season's first money, and the window excludes it explicitly. |
| A JSONB blob of category totals on the summary row | §4.5 confines JSONB; a child table is the relational form of "category totals" and stays correct as categories grow. |
| Point a rollover-window provisioning request at the closing season anyway | That is exactly the mutation `PYR-9` forbids; deferring is safer than mutating a season being sealed. |
