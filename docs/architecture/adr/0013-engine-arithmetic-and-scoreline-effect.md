# ADR-0013: Integer basis-point arithmetic and a bounded scoreline effect in the engine

- **Status:** Accepted
- **Date:** 2026-09-25
- **Stage:** 5
- **Related:** [`../../product/master-plan.md`](../../product/master-plan.md) §8, [ADR-0004](0004-deterministic-match-engine.md), [`../../product/match-engine.md`](../../product/match-engine.md)

## Context

ADR-0004 settled that the engine is pure, deterministic, versioned, and reproducible from a frozen
snapshot. Implementing it raised two questions the earlier decision did not answer, both of which
change results and are therefore expensive to revisit once a season has been played.

**How are probabilities represented?** A simulation is a long chain of probabilistic decisions —
possession, progression, creation, shot outcome, foul, card, injury. Each decision feeds the next, so
any non-determinism or platform dependence anywhere in the chain compounds. Floating-point arithmetic is
reproducible for the same binary and the same instructions, but .NET makes no cross-platform guarantee
for the transcendental functions (`Math.Pow`, `Math.Exp`, `Math.Log`), and a rating formula that reaches
for one is a formula whose result could differ between the machine a match was played on and the machine
a dispute is investigated on.

**Does the scoreline affect the simulation?** Modelled naively, every goal is an independent event, and
the total goals in a match is then very close to a Poisson variable. Measured against forty thousand
simulated matches, that produced a mean of 3.15 goals and **4.2% of matches with seven or more goals**.
Real football produces about 2.5%, because a side three goals up stops chasing a fourth and a side three
down faces a defence content to sit deep. Master plan §8.4 permits momentum "only if explicitly
bounded", which leaves the question of whether to bound it at all.

## Decision

**Arithmetic.** The engine uses **no floating-point number anywhere**. Two scales carry every formula:

- A **rating scale** of `0…100`, where one attribute point is exactly five units.
- A **basis-point scale** of `0…10_000`, where 10_000 is certainty.

Every probability is an integer on the basis-point scale, every multiplier is an integer basis point
around 10_000, and two probabilities compose exactly as `a * b / 10_000` in integer arithmetic. A random
decision is a comparison against a draw, never a comparison against a floating-point threshold. Ratings,
modifiers, and chances are all derived by integer multiply and divide, in a documented order.

**The scoreline effect.** A bounded, score-derived modifier scales the attacking side's chance creation
in `PossessionSimulator.GameStateModifier`. It is a pure function of the current score — it consumes no
random draw and cannot drift — and it is clamped so that it can never move creation by more than
`MaxGameStateModifierBasisPoints` in either direction. The leading side creates less from a two-goal
margin; the trailing side creates more.

**Shot quality on events.** A shot event carries the goal probability it was resolved against
(`QualityBasisPoints`), so highlight selection can distinguish a good chance from a bad one. It is a fact
derived from attributes the owning manager can already see, not a hidden player value. It is nevertheless
not something a player-facing response may carry (`MAT-11`), so the transport mapping and the
data-classification test are the mechanism that keeps it out of a DTO.

## Consequences

**Positive**

- A result is bit-identical across operating systems, architectures, and .NET patch levels, which is
  what makes the golden-hash tests meaningful and a disputed result re-derivable on any machine.
- The score distribution has a football-shaped tail: 2.6% of matches with seven or more goals, against
  4.2% before, with the mean unchanged at 2.8.
- A balance change is one integer in one file, and its effect on the distributions is measurable in
  seconds with the simulation laboratory.
- Integer arithmetic is faster and allocation-free in the simulation path: p95 of 3.5 ms per match against
  a 100 ms budget.

**Negative**

- Every probability must be reasoned about on a fixed scale, and a formula that "obviously" wants a
  logarithm has to be expressed as a bounded linear or piecewise-integer approximation instead. This is a
  real constraint on how the engine can be tuned.
- Integer division truncates, so a chain of several multiplicative modifiers loses a fraction of a basis
  point at each step. The loss is bounded and deterministic, and the golden hashes pin the exact result,
  but it is not the arithmetic most people reach for.
- The scoreline effect is a second-order mechanism that exists to shape a distribution rather than to
  simulate a manager's decision. It has to be understood as such, or somebody will try to expose it as a
  tactic.
- Carrying shot quality on an event increases the surface on which a hidden value could leak. It is
  guarded by an allowlist in the commentary tests and by the data-classification test over the contracts
  assembly, but the guard is a test, not a type.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `double` throughout, with a fixed evaluation order | Reproducible today on one binary; not reproducible across platforms for transcendentals, and the engine's whole value is that a result is re-derivable anywhere. |
| `decimal` | Reproducible and exact, but orders of magnitude slower, allocated, and its rounding modes make the composition rules more complex than basis points. |
| Fixed-point with a fraction denominator other than 10_000 | 10_000 is a basis point, which is the unit the project's existing rules, thresholds, and player state already use (`TRN-5`, `TRN-7`). A second scale would be a second thing to convert. |
| No scoreline effect, accept the Poisson tail | The mean would be right and the tail wrong, and the tail is what a manager screenshots. It also makes a 5-0 rout as likely as football says it should be 3-1, which distorts goal difference and therefore the table. |
| A stronger, unbounded scoreline effect | Unbounded momentum makes comebacks likely and makes a settled game unstable. The effect is deliberately capped so a rout stays a rout. |
| Inferring "worth watching" from the event type alone | A save from three yards and a save from thirty are identical in an event stream without a quality signal, and "notable saves above a configured threshold" is then not expressible. |
