# ADR-0076: Experience is frozen into the snapshot as a skill bonus

- **Status:** Accepted
- **Date:** 2026-10-20
- **Stage:** Player experience plan (`i-would-like-to-composed-eich.md`), Milestones 1-4
- **Related:** [ADR-0017](0017-match-load-at-publication.md), [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0074](0074-tick-engine-skills-fall-with-live-condition.md), `EXP-1` to `EXP-6`, `TRN-5`, match-engine sections 14.4, 14.12, 14.41, 14.43

## Context

The game had no notion of experience: a player of 35 and a player of 18 with the same sheet played the same. The request was a simple rule. Experience runs 0 to 100 points, half a point a match of at least 15 minutes, five even levels, and a level is worth that many per cent on the skills the engine plays with, shown on the player page and the player popup. A player with no level has to play bit-identically to before, so stored matches, their replays and the golden hashes stay valid.

## Decision

**1. One pure rule in the domain.** `PlayerExperience` (`experience-v1`) holds the unit (basis points of the maximum, like condition and morale), the earning (50 bp for 15 minutes or more), the five levels (2,000 bp apart), the bonus (100 bp of the skill a level) and the starting value by age (`(age - 18) x 400`, between 0 and 5,000). `PlayerState.ExperienceBp` stores it. The API converts it to points; the web shows what the API sends.

**2. Earned at publication, with the match load.** The matchday publication already applies match load from the stored player lines in one serializable transaction (ADR-0017). Experience is added there, so a round published twice loads nobody twice, and a player on the books at publication always has a state row.

**3. The bonus, not the experience, is frozen into the snapshot.** At lock the snapshot builder writes `ExperienceBonusBasisPoints` (0 to 500) on each player's match state. The engine never learns what a level is, a replay re-simulates from the stored snapshot whatever the player has earned since, and an old snapshot without the field reads as 0.

**4. It acts on the played skills, after the curve and tiredness, and is not capped.** `TickPlayerSkills.Value` multiplies the value the engine plays with by `(10,000 + bonus) / 10,000`, rounded, after the skill curve and the tiredness drop. Pace and Acceleration, the movement physics (`TickPlayerProfile`), the raw-attribute readers (`TickAttackEdge`, `EffectiveSkill`) and the legacy engine are left alone. There is **no ceiling at 20**: a sheet 20 at +5% plays as 21 where the engine reads it raw (dead balls) and as 15.75 instead of 15.00 where it reads the curved value. Each formula protects its own result with a clamp, none rejects a skill above 20, and the goalkeeper and free-kick smoke tests play a full match at sheet 20 with the bonus at 500.

**5. Hashed only when above zero.** The canonical serializer writes `experienceBonus` only when it is above 0, so every existing snapshot and every match of players without a level keeps its hash. `EngineLabel` and `RuleSetLabel` did not change, and the tick golden hashes, the determinism tests and `tick 500` (digest `91cb3c94fc41bf8a`) are identical to the commit before the plan.

**6. Starting experience is by age, with no spread.** 17 and 18 year olds start at 0, then 4 points a year, capped at 50 points from age 30. The same rule gives a generated player his value and was the backfill for the players already in the database (a migration, no reseed). Adding it drew no random number, so `PlayerGenerator.Version` stayed `player-gen-v2`.

## Consequences

- Experience is a public number, like age, shown with its level and the "+n% skills in matches" line on the squad player page, the player popup and the scouting card. The sheet attributes stay 1-20 everywhere.
- **Measured worth** (match-engine section 14.43; uniform 15 against 15, 500 matches a cell, half with the bonus side at home): level 5 (+5%) is worth +0.21 points a match (44.5% wins, 24% draws, 31.5% losses against 37 / 25 / 37 without it), level 2 (+2%) is worth +0.09, within noise. A whole sheet point on every skill is worth +0.53, so the bonus is about 40% of a sheet point. Nothing was tuned; the figures are reported for the product owner.
- On played skills alone the bonus looks larger than a sheet point (5% of a curved 15, which plays as 13.8, is 0.69 of a played point; a whole sheet point is 0.38), yet it measured smaller. Most likely a sheet point also lifts Pace, Acceleration, stamina and the attack edge, which the bonus leaves out (EXP-4); this was not isolated.
- **Open, offered and not done:** whether level 5 should be worth more (a bigger step per level, or the bonus also on Pace and Acceleration); a youth intake (17-18 year olds start at 0 by the same rule); an experience-weighted valuation, wage or training rule (none reads experience yet).
