# ADR-0019: The player line carries assists and a rating, and season statistics are a projection of published results

- **Status:** Accepted
- **Date:** 2026-09-27
- **Stage:** 8
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0017](0017-match-load-at-publication.md), master plan §6.4, §8.5, §10.5, game rules §15.1 (`STA-1`…`STA-5`), `MAT-5`, `MAT-9`, `TBL-13`, `TRN-8`

## Context

Stage 8 asks for player and club season statistics. ADR-0014 deferred the projection with a specific
reason: the engine's player line had no assists and no rating, so a statistics projection would publish
columns that could never be filled. The engine is where the gap has to be closed, and three questions
follow from closing it.

**1. How an assist is decided.** A goal event names the scorer and the goalkeeper it beat and nothing
else — there is no creator in the event stream — so the player who set a goal up has to be chosen when the
goal is scored.

**2. What the rating is, and who owns it.** "Average rating" is one of the columns §6.4 lists. It can be
computed by the projection from a match's events, or produced once by the engine with the result.

**3. What happens to an already-played match.** ADR-0004 says a released engine version is never altered in
place, and the golden hashes pin what a historical snapshot replays as. Adding fields to the output is a
change to the output contract.

A fourth question is what the projection is keyed on, and a fifth is the scope of the milestone: §6.4 lists
both `player_season_stats` and a `club_season_stats`.

## Decision

**1. An assist is attributed at the goal, drawn from a stream derived from the match seed and the goal's
sequence number, never from the play stream.**

A goal from open play or a corner credits exactly one teammate: an outfield player on the pitch, excluding
the scorer, drawn weighted by vision, passing, technique, crossing, and dribbling, in slot order. A penalty
has no assister. The assist is kept on the player line beside the goals, because the only durable place it
can live is the result the engine produced.

The derived stream matters more than the weighting. Taking the draw from the play stream would advance
every subsequent decision and change the scoreline the engine was calibrated to, moving the distributions
§12 records. A second `Pcg32` seeded from the match seed and the goal's sequence number is still seeded from
the snapshot — the purity contract holds — and it leaves the play untouched. The consequence is that
version 2's play is byte-identical to version 1's for a given seed, and only the output contract and its
hash moved.

**2. The match rating is engine output, computed from facts the match already records, and never recomputed
by the projection.**

The rating is arithmetic over minutes, goals, assists, saves, cards, and the result, on the 0–10,000
basis-point scale (`TRN-8`), clamped to rules bounds, with no rating at all for a player who did not take
the pitch. It is produced once with the result so a season's average has one definition, and it carries no
hidden value: every term is a fact a manager can already read (`MAT-11`). Its constants live on the rules
set and are part of the rules hash, so the rating is versioned like every other formula.

**3. The engine and rules versions become `engine-v2` / `engine-rules-v2`, the result document becomes
`match-statistics-v3`, and no previous version is retained — yet.**

Both changes are additive to the output and neither changes a play formula, so the golden output hash and
the rules hash are re-pinned and the measured distributions are unchanged. A compiled version 1 is not
retained, which is the same call the statistics document made when it went from version 1 to 2: there is no
production world, so the cost is that a database seeded before the change must be reseeded. **Retaining a
compiled previous engine version becomes required before the public launch**, where a played match must
remain replayable; it is recorded here as a pre-launch item rather than left implicit.

**4. Season statistics are a projection advanced by the matchday publication, keyed on
`(division-season, player, club)`.**

`competition.player_season_stats` holds appearances, starts, minutes, goals, assists, shots, shots on
target, saves, cards, and the sum and count behind the average rating. It is advanced inside the same
serializable transaction that publishes the round, so the totals and the results they summarise become
public together (`MAT-7`, `STA-1`), and a republished round advances nothing because publication returns
before it applies anything. The club is part of the key because a player may move mid-season, and the
average is computed from the stored sum and count rather than stored, for the same reason a table's goal
difference is.

The pure `SeasonStatisticsCalculator` reads the engine's line for the goals, assists, minutes, cards, and
rating, and counts the shots and saves from the match's events — the same events the score is derived from
(`MAT-5`). The projection is the source of truth for a read; it is rebuildable from published results, and
the rebuild tool is a later milestone.

**5. `club_season_stats` is not built.**

§6.4 calls it "aggregates not represented in standings", and a club's competitive row is already the
standings projection. A club-level shooting or possession aggregate has no read and no screen, and building
the table now would ship a row nothing shows (`§17.12`). It belongs with the screens that would consume it.

## Consequences

**Positive**

- A season's totals are a sum of published match facts, so they cannot drift from the matches they
  summarise, and a delayed publication reads the facts the result was made from rather than re-simulating.
- Version 2 changes no formula and no draw of the play, so the distributions the engine was tuned against
  are untouched and the existing calibration stands.
- The rating has one definition, owned by the engine and versioned with it, and it exposes no hidden value.
- The projection follows the table's and the discipline record's shape — a row advanced by publication,
  rebuildable by the same arithmetic — so the repair path a later milestone adds is the live path.

**Negative**

- The output hash of every match changes, so a database seeded before this change must be reseeded.
- A compiled version 1 is not retained, which is acceptable only because there is no production world; it
  becomes a launch blocker.
- The assist attribution is a plausible model rather than a re-simulation of the chance's creator, and it
  credits one teammate per goal even when the engine did not literally track the pass.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Draw the assister from the play stream | Advances every subsequent decision and moves the scoreline distributions the engine was calibrated to; a version bump is allowed to change results, but doing so without need throws away the calibration for a secondary statistic. |
| Derive assists in the application layer from the event stream | Nothing records a creator, so the projection would have to invent one anyway — and it would be a second, unversioned definition of a match fact, which is exactly what ADR-0014 refused. |
| Compute the rating in the projection instead of the engine | Two definitions of "how well did they play" that can disagree, and the average would no longer be reproducible from the stored result alone. |
| Put assists and rating on the goal event rather than the player line | A goal event names one principal participant; an assist is a second player's contribution, and the season line is where it is consumed. |
| Keep `match-statistics-v2` and read it leniently | A version-2 document has no assists and no rating, so a reader that accepted it would silently produce zeroes for every player — the failure mode the schema discriminator exists to refuse (`JSN-5`). |
| Build `club_season_stats` now | No read and no screen consume it; a table nothing shows is the half-built surface `§17.12` keeps out. |
| Read the season totals by re-simulating each published match per request | Defeats the point of a stored, hashed result and would re-simulate under whatever engine build has since shipped (`ADR-0014`). |
