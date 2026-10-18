# ADR-0071: AI clubs carry a match plan

- **Status:** Accepted
- **Date:** 2026-10-18
- **Stage:** Match instructions plan (`i-want-you-to-curious-globe.md`), Milestone 6
- **Related:** [ADR-0018](0018-ai-club-policy.md), [ADR-0068](0068-tactics-in-the-tick-engine-counter-attack-low-block-and-time-wasting.md), `MAT-6`, `INS-12`

## Context

A manager can now write match instructions: rules the engine carries out when the score, the clock or a card says so. An AI club with none would be the only side that never reacts to the game, and the managers' rules would meet opponents that do not answer them.

## Decision

**1. A second pure policy.** `AiMatchPlanPolicy` decides five rules per club (protect a two-goal lead, hold a narrow lead, chase the game, go all out, settle down with ten men) and a temperament (cautious, balanced, bold) that moves their minutes and the size of the steps. It is a function of the club identity, its formation and its instructions, with no clock, repository or global random source.

**2. Its own seed.** The policy is seeded from `ai-match-plan-v1` (`AiPolicyVersions.MatchPlanVersion`) and draws from its own generator. `AiPolicyVersions.Version` is **not** bumped: it is folded into every draw of `AiClubPolicy`, so a bump would have given every club a new formation, new instructions and a new eleven. The plan said to bump it and also that no existing choice may move; the two cannot both hold, and the second is the one that matters. `AiClubPolicyTests` pins six clubs' choices.

**3. No bypass.** The rules are ordinary `MatchInstructionDefinition`s, name families and never players, and go through `MatchInstructionValidator` in `EvaluateAiClubs` (a refusal counts as skipped). `MatchSnapshotBuilder.PlanOf` is the one function that turns instructions into the engine's plan, public so the laboratory plays the plan exactly as a fixture would.

**4. Gap-filling stays.** `EvaluateAiClubs` still never overwrites a plan, so a club that already has a default plan keeps it, and an existing world's AI clubs keep rule-less plans until it is reseeded. A takeover inherits the rules and may edit them.

**5. Versions.** `EngineVersions` is unchanged and so are the golden hashes: no engine code changed, and a side with no rules plays as before.

## Consequences

- Matches of AI clubs now contain tactical changes and tactical substitutions, so league-wide results move (section 14.39 of the match engine document): draws rise 4-9 points and a two-goal lead from the 70th minute is held 2-5 points less often. The guards that held are goals a match, the weaker-side cells and the late-goal share. The remaining gap is a decision for the product owner.
- The plan's numbers are in three `Profile` records; retuning them changes AI behaviour for clubs created afterwards, not for a stored plan (stored plans are rows, written once).
- A new rule kind for the AI is a new `MatchInstructionDefinition` in the policy plus a new `ai-match-plan-vN` label, and it reaches only clubs created afterwards.
