# ADR-0072: Match instructions are rules the engine carries out, and what they were measured to be worth

- **Status:** Accepted
- **Date:** 2026-10-18
- **Stage:** Match instructions plan (`i-want-you-to-curious-globe.md`), Milestones 1-7
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0068](0068-tactics-in-the-tick-engine-counter-attack-low-block-and-time-wasting.md), [ADR-0071](0071-ai-clubs-carry-a-match-plan.md), `MAT-6`, `MAT-13`, `MAT-14`, `SQ-5`

## Context

Nothing could change once a match began, and matchdays are simulated in a batch, so a manager could not click during one. The Tactics page needed a way to say "if I lead by two from 70', do this", and the engine's own substitution planner was the only source of changes.

## Decision

**1. Rules written beforehand, carried out by the engine.** A tactical plan holds up to 10 match instructions: a condition (a minute range, a score, at most one event) and the changes it makes (a formation, instruction changes with a relative mentality, up to 3 substitutions). The engine checks them at every stoppage and at half-time, in the manager's order, each fires at most once, and the runner draws no random number. Injuries come first and the fatigue substitutions last (unless the plan switches the assistant off); the 5-substitution limit and the one-appearance rule still hold (`MAT-6`, `MAT-13`).

**2. A formation change is a best fit, not a lottery.** `ShapeFit` places the players on the pitch in the new shape by a deterministic greedy assignment plus a swap pass, keeps the goalkeeper in goal, places the manager's own overrides first and drops the cheapest, most advanced slot when a side is a player short (`MAT-14`). It exists in three copies (engine, domain, web) pinned by one shared table of cases.

**3. No version bump.** A side with no rules and the assistant on is `MatchPlanV1.None`, writes no plan line, and plays bit for bit as before; both golden hashes did not move at any milestone. `TacticalChange` is a new event type, which ADR-0004 would count as an engine-version change. The tick milestones before this one re-pinned hashes instead of bumping `EngineVersions` (ADR-0068), nothing already stored contains the new event, and the stored events' new columns are nullable, so the convention was followed and `EngineVersions` was not bumped.

**4. AI clubs follow the same rules** through the same validator (ADR-0071).

**5. What a rule is worth was measured before anyone relied on it** (match-engine section 14.39, part 2: 68 paired cells, 10 confirmed on a second seed). In this engine the useful rule is chasing a game (+0.2 points where it fires on equal sides); defending a lead is not helped; a fresh forward against tired defenders is worth nothing measurable; no formation switch beat staying put.

## Consequences

- A manager's plan can now lose points as well as gain them: a Defensive low block when leading, 4-4-2 to 4-3-3 with a wings focus against an equal side, and keeping the shape with ten men each cost about 0.1-0.2 points on both seeds. The page's templates should not present them as improvements.
- **Open decision for the product owner:** tiredness only costs speed in the tick engine, so a substitution for fresh legs is worth almost nothing. Letting tiredness lower technical and mental skills too, as the legacy engine's `EffectiveSkill` does, would make substitutions matter but moves every match in the league and needs its own balance pass. It is not part of this plan.
- A new condition, event or action is a new enum value (appended, never renumbered), a validator rule on both sides, and a `plan.*` line the serializer writes only when it is used.
