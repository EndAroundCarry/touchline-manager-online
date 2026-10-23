# ADR-0076: Potential is drawn before skills, with a tail of talents and gems

- **Status:** Accepted. Training's cap of `min(20, potential)` on every skill (decision 3, consequences) and youth
  intake drawing around the generated squads' tier mean are superseded by
  [ADR-0081](0081-every-attribute-develops-to-a-limit-set-by-potential.md): a limit from potential and position,
  and youth potential on its own constants (`TRN-19`, `TRN-20`). The generated squads' draw stands.
- **Amended by:** [ADR-0085](0085-fair-seeded-squads-and-two-tiers-from-the-first-season.md): a seeded squad is a fair template with exactly one gem and three talents, not a draw; the 80/15/5 draw stays for the youth intake and the legacy generators.
- **Date:** 2026-10-19
- **Stage:** Potential plan (`create-a-plan-to-tidy-blum.md`)
- **Related:** game-rules `TRN-4`, `TRN-9`, `TRN-14`, `TRN-18`; `PlayerGenerator`, `GeneratedPotential`

## Context

A player's hidden potential is set when the game creates him (world seeding, a new division, an emergency replacement at rollover). It was `mean skill + (25 - age) + 0..2`. A young player's skills are lowered by the years he has left to develop, and the potential formula then added those years back, so the two cancelled: every ceiling in a tier landed in a narrow band, about the tier mean +1 to +2, give or take 3. There were no wonderkids and no duds.

## Decision

**1. Draw the ceiling first and derive the skills from it.** `potential = clamp(tierMean + offset, 1, 20)`, drawn at the point the skill spread used to be drawn. `baseAbility = potential + AgeAbilityAdjustment(age)`, which replaces `tierMean + AgeAbilityAdjustment(age) + spread`. A player under 24 sits `24 - age` below his ceiling, one from 24 to 29 is at it, and an older one sits `age - 29` below it. Position bonuses and per-skill jitter are as they were. Reputation, wage and squad status still come from the generated skills.

**2. The offset is a band with a tail.** Normal 80%: two four-faced dice less four, -4 to +2, most often -1. Talent 15%: +3 to +5. Gem 5%: +6 to +7. The bands do not overlap. The mean offset is about +0.1, so a tier's squad strength is kept; the normal band sits one point low to pay for the tail. The constants live in `WorldRuleSet`, the draw in `GeneratedPotential`.

**3. The user's choices.** The draw follows the club's tier. There stays one ceiling for every skill (the training rule is unchanged, so `training-v3` is untouched). Existing worlds get the new potentials by reseeding, with no migration or backfill.

**4. Versions.** `PlayerGenerator.Version` is `player-gen-v3` (the draw order changed). `WorldRuleSet.Version` is `world-rules-v12` (the rule table changed). `PlayerAttributeProfiles.Version` stays `player-attr-v2`: no bonus changed. The golden digest in `PlayerGeneratorTests` is re-pinned (`941aab8c455dbc46` to `64306e99164964bf`). `PlayerSpread`, `DevelopmentAge` and `PotentialUpsideForAge` are removed as unused.

## Measured

100 clubs a tier, seed `potential-probe`, before and after (valuation is `PlayerValuation`, the wage is a two-season `ContractRenewalQuote`):

| | Before | After |
|---|---|---|
| Tier 1 mean ability | 11.45 | 11.46 |
| Tier 1 mean potential | 13.87 | 13.14 |
| Mean ability by age band, every tier | | within 0.28 of before |
| Tier 1 mean valuation | 3,214,355 | 3,165,792 (-1.5%) |
| Tier 1 mean renewal wage | 49,117 | 48,683 (-0.9%) |

By age, tier 1 valuation: 17-20 -5%, 21-23 -9%, 24-29 unchanged (+0%), 30+ +15%. A tier 4 17-20-year-old is worth 18% less. Mean potential falls about 0.7 points, not the 2 the plan expected, because the old formula's inflation was smaller than the plan assumed (+0.9 over the tier mean, not +1 to +2).

A freshly seeded scratch database (tier 1, 2,592 players) read gems 5.9%, talents 14.0%, mean offset +0.10, range -4 to +7.

## Consequences

- **Side effect, not retuned:** valuation and renewal read potential, so the youngest ordinary players cost a little less, talents and gems cost more, and players over 29 cost more (+15%) because their potential is now their peak, well above their declined skills. Whether that last shift is wanted is a decision for the product owner.
- Training reads potential as the cap and is unchanged. Over-29 players now have headroom between skills and cap; a trained skill of a player over 29 still grows, very slowly (TRN-14: growth floor of 2%), and the existing `DailyProgression` tests still pass.
- A world seeded before this change keeps its old potentials until it is reseeded.
