# ADR-0070: The tick engine reads the slot's role, not only its family

- **Status:** Accepted
- **Date:** 2026-10-17
- **Stage:** Engine roadmap, `at-the-moment-in-adaptive-muffin.md` Milestones 1-4
- **Related:** [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0068](0068-tactics-in-the-tick-engine-counter-attack-low-block-and-time-wasting.md), [ADR-0004](0004-deterministic-match-engine.md)

## Context

The game had no left or right midfielder. The wide men of a 4-4-2 were Attack-family wingers, so they ran the byline, shot like forwards and never tracked back. The tick engine could not tell the two apart anyway: `TickAnchorSpec` kept the slot's family and its coordinates and dropped `slot.Role`, so every behaviour that differed by job could only be keyed on Defence, Midfield or Attack.

## Decision

**1. New vocabulary, appended.** Positions `RightMidfielder = 10` and `LeftMidfielder = 11` (Midfield family), role `WideMidfielder = 9`, training programme `widemid`. Enum values are appended and never renumbered, because `CanonicalMatchSerializer` writes them as integers and `PlayerPositions.JoinCodes` orders by value. Squads grow from 22 to 24 and the registration ceiling from 25 to 27, which keeps three free places.

**2. The role reaches the tick engine.** `TickAnchorSpec` carries a `MatchRole`. A new static class `TickRoleProfile` holds every per-role number and function (shot and long-shot minimum chance, cross minimum X and bonus, who makes wide runs, who may screen, who tracks). Each is pure and draws no random number, so a role changes the choice a player makes, never the order of the random stream. The plain figures of the family apply to every role the engine has no special play for.

**3. Fit is role-aware.** `RolePositions.NaturalFor(role)` in the domain, mirrored by `MatchLineup.NaturalPositionsOf` in the engine and `isNaturalForRole` in the web client, lists the positions a role asks for. A player fits a slot when his primary or secondary position is in the slot's family or in the role's natural list. The wide midfielder's list includes wingers and full-backs, and the wing-back's and winger's lists include the left and right midfielders, so no existing player or formation is flagged.

**4. Formations.** 4-4-2, 4-1-4-1, 4-4-1-1, 4-5-1 and 5-4-1 use the wide midfielder. 3-4-3 was tried with wide midfielders instead of wing-backs and was put back: they stood at 50 m and never reached the cross line, and the formation lost 0.35 points. 3-5-2 and 3-4-2-1 keep wing-backs; 4-3-3 and 4-2-3-1 keep wingers.

**5. Behaviour.** With the ball a wide midfielder shoots only for a goal chance of 27% (winger 17%), never from long range, crosses from 68 m (winger 75 m) and values a cross 300 more. Without the ball he tracks the most advanced opponent on his flank, a loose 15 m goal-side shadow at half speed; a winger is never sent to screen. The numbers and the measured table are in `match-engine.md` §14.38.

**6. Versions.** `EngineVersions` was not bumped (the tick milestones before it did not either); the tick golden hashes are re-pinned with the hash in the assertion's `because` text. `PlayerGenerator.Version` is `player-gen-v2`, `PlayerAttributeProfiles.Version` is `player-attr-v2` and `WorldRuleSet.Version` is `world-rules-v11`. The migration `WideMidfielders` widens five check constraints. Existing worlds are not migrated: a development database is wiped and reseeded.

## Consequences

- A 4-4-2 plays as football describes it: the wide men cross 14.5 times a match (a winger 11.9), shoot 0.08 times from open play (a winger 0.84), attempt 3.4 tackles (a winger 1.9) and stand 59 m from their goal without the ball (a winger 77 m, a full-back 21 m).
- A 4-4-2 takes about 0.2 more home points from a 4-3-3 than before. It was left as it is.
- A future role (an inverted winger, a ball-playing defender) follows the same path: a `MatchRole` value, a natural-position list in three places, and its numbers in `TickRoleProfile`.
- Families still decide the line shift, the ball shift and the run budget; a role changes choices inside those, not the geometry.
