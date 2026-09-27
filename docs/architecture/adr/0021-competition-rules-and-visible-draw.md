# ADR-0021: The tie-break order and the season's draw are public, and the criteria have one definition

- **Status:** Accepted
- **Date:** 2026-09-27
- **Stage:** 8
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0020](0020-projection-rebuild-and-reconciliation.md), master plan §3.5, §6.4, §10.5, §11.1, game rules `TBL-1`…`TBL-12`, `MAT-9`, `MAT-11`

## Context

Master plan §3.5 and game rules `TBL-11` require the final tie-break draw to be "generated **before** the
season, stored, and visible in competition rules". The first two halves have held since Stage 6:
`competition.division_seasons` carries `tie_draw_seed` and `tie_draw_hash`, written when the world is seeded,
and `StandingsCalculator.DrawKeyOf` derives each club's key from that seed as the table's last criterion.
The third half did not: nothing read the stored draw, so the rule was satisfied in storage and not in
visibility. A manager could see the table but not the draw its order was decided by.

Four questions follow.

**1. Is the tie-draw seed public or protected.** The world holds a protected value already — the match seed is
derived by HMAC from `World:MatchSeedSecret` and stored only as a commitment (`MAT-9`). Whether the
tie-draw seed belongs to the same class is a data-classification decision, and getting it wrong in either
direction is a real error: too open and a competitive mechanism is exposed; too closed and the one thing
`TBL-11` asks to be visible is not.

**2. Where the ordering lives, so a page cannot describe a different one.** The sequence exists as the body
of `StandingsCalculator.Rank`. A rules page that restated it would be a second definition of the order, and
the two would eventually disagree — the failure `TBL-12` exists to prevent.

**3. Whether the per-club keys are stored or derived.** A key per club per season could be a column, written
once at seeding beside the seed.

**4. What the read is, and who may call it.** The owners of the other division reads are public game data
gated on authentication alone, and the rules are the same kind of thing.

## Decision

**1. `TieBreakers` is the ordering, and the page projects it.** The criteria live in the domain as one
ordered list of stable codes (`points`, `goal_difference`, … `draw_key`), and `StandingsCalculator` implements
that sequence. The rules read takes the list from the domain and the client renders the words, so the page
cannot describe an order the table does not follow.

**2. The tie-draw seed and its hash are public.** `TBL-11` requires the draw to be visible, and the hash
beside the seed is what makes it checkable — a reader can confirm the seed has not been edited since the
season began. The seed decides only the order of two clubs that no result separated, and it is regenerated
per country and season from the world seed; it is not the match seed, which stays protected (`MAT-9`) and is
never serialized (`MAT-11`).

**3. A club's draw key is derived at read time, never stored.** `StandingsCalculator.DrawKeyOf` is the single
source, so a key cannot disagree with the seed it came from; storing the pair would be two answers to one
question.

**4. The points are the domain's too.** `StandingsCalculator.PointsForWin` / `PointsForDraw` /
`PointsForLoss` are projected rather than restated, so the page and the table award the same points
(`TBL-1`).

**5. It is a public read.** `GET /divisions/{divisionId}/rules` is gated on authentication exactly as the
table and the calendar are, and on nothing else: a rule only some managers can inspect would not be visible
in the sense `TBL-11` means. `/competitions/:divisionId/rules` is the screen, reached from the table of the
division it describes.

## Consequences

**Positive**

- `TBL-11` holds as behaviour: the draw the season committed to is a page a manager can open, and its
  published hash lets it be checked rather than taken on trust.
- The ordering and the points have one definition, in the domain the table is ranked with, so the rules view
  cannot drift from the rules applied.
- Deriving the keys keeps the stored seed the only fact, so there is nothing to keep in step.

**Negative**

- The tie-draw seed becomes public knowledge. It is accepted because it decides nothing a result could have
  decided and is not the value the integrity of a simulated match rests on; the accepted cost is that a
  future mechanism which did need the tie-draw seed to be secret would have to change this ADR rather than
  quietly reuse the column.
- The criteria list is now a public contract as well as an implementation detail, so reordering the table
  means updating `TieBreakers` and the calculator together. The domain test that pins the list as literal
  codes is what makes a partial change fail fast.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Publish only the hash before the season and reveal the seed at rollover | `TBL-11` asks for the draw key to be visible, and a hash alone proves nothing until the seed is known; showing both makes the commitment checkable at any time. |
| Keep the seed protected like the match seed | The match seed decides a simulated result (`MAT-9`); the tie-draw seed decides the order of clubs no result separated. Treating them alike would protect a value the rules require to be visible. |
| Store each club's draw key in a column | Two answers to one question: a stored key could disagree with the seed after any change to the derivation. |
| Send the ordering as prose from the server | The client owns wording and localization and the server owns the order — the split the commentary tokens and inbox templates already use (§8.6). Prose would also hard-code one language into the API. |
| A `division.rules` column holding the ordered criteria | The order is code, not data; a column could disagree with the calculator and would need a migration to change a rule. |
| A separate `GET /competitions/{id}/rules` resource path | §10.5 groups a division's reads under `/divisions/{divisionId}/…`; a second shape for the same resource family would be a needless second convention. |
| Restate the ordering in the web client | The second definition the rule set exists to avoid (`TBL-12`), and the codes would then have to be kept in step by hand. |
