# ADR-0011: Squad schema — hidden player values are server-only columns, and contract/registration agreement is an application invariant

- **Status:** Accepted
- **Date:** 2026-09-25
- **Stage:** 4
- **Related:** [ADR-0009](0009-time-identity-and-concurrency.md), [data-classification.md](../../security/data-classification.md) §2.1, [data-model.md](../data-model.md) §3.2, master plan §6.5, game rules `SQ-6`, `SQ-7`, `TRN-9`

## Context

Three decisions in the `squad` schema are expensive to reverse later, because each one fixes how a
later stage may read or write the tables.

**1. Where a player's hidden potential lives.** A player carries an immutable generation ceiling that
bounds development (`TRN-9`) and that must never reach a manager (`MAT-11`). The classification is C2:
`data-classification.md` §1 puts hidden potential in the same class as internal valuations and seed
material, and §2.1 offers exactly two permissible homes — a separate restricted table, or columns
explicitly marked server-only and excluded from every DTO — with the rule that whichever is chosen has
a test that fails if the value appears in a manager-facing response. Master plan §6.5 says the same
thing in one line.

**2. Whether "one active contract and one current registration, and they must agree" (`SQ-6`) is a
database constraint.** A partial unique index can say "at most one active row per player". It cannot say
"the active contract and the active registration name the same club", because that is a predicate across
two tables. `data-model.md` §3.2 already records the agreement as an application invariant with
integration tests; the question this ADR settles is whether anything more can be pushed into the
database.

**3. What to do about `fixture_id` before fixtures exist.** `squad.fixture_team_sheets` and
`squad.player_unavailability` both reference a fixture, and `competition.fixtures` does not exist until
Stage 6. The Stage 4 deliverable list names the team-sheet schema, and Stage 3 already shipped the
competition and finance shells ahead of their stages.

## Decision

**1. `potential` and `reputation` are ordinary columns on `squad.players`, marked server-only, never
mapped to a DTO.**

Not a separate table, and not a JSONB blob on the player row. They are read on the engine's hot path —
the Stage 5 input snapshot freezes a player's hidden ceiling alongside their attributes — and by AI
valuation in Stage 10, so a second table would add a join to every engine input. Confidentiality does
not come from the storage shape; it comes from the mapping boundary, which is the layer where a leak
would actually happen. `data-classification.md` §2.1 requires a test that fails when a C2 value appears
in a manager-facing response, so that test lands with the first manager-facing player response (Stage 4's
squad reads) and is the reason to prefer the DTO-boundary control over the table split.

**2. Contract/registration agreement is enforced in the application transaction, and proven by an
invariant integration test. The partial unique indexes stay as the row-level backstop.**

`ux_player_contracts_active_player` and `ux_player_registrations_active_player` make two active rows
impossible, which is the part a constraint can express. The agreement on club and status is a cross-row
predicate; expressing it in SQL would take a trigger or a denormalised mirror, both of which add a
failure mode and a migration to every transfer. The trade is explicit: a transfer that closes one row
and opens another in the same unit of work is the application's responsibility, and a test asserts the
end state.

**3. `fixture_id` columns ship now without a foreign key. Stage 6 adds it.**

Stage 4's chosen scope is the schema plus generation, because a fixture-specific team sheet cannot be
written, read, or locked before fixtures exist. Shipping the tables with an un-keyed `fixture_id`
follows Stage 3's precedent of shipping a module's shell with its stage and lets the fixture-independent
default-lineup work have somewhere to land; withholding the table until Stage 6 would mean a second
migration against the same module for no benefit.

## Consequences

**Positive**

- One row per player for everything read together at match-lock time, so the Stage 5 snapshot is a
  single join rather than three.
- No trigger and no mirrored `club_id` on the registration to keep in step, so a transfer is two writes
  in one transaction with nothing to reconcile afterwards.
- The partial unique indexes still make the race that matters — a renewal and a transfer resolving at
  once — impossible to corrupt, even if the application check were bypassed.

**Negative**

- The C2 protection depends on a mapping discipline plus a test rather than on the database refusing to
  return the column. A query written outside the squad read port could select `potential` and expose it;
  the leak test catches it reaching a response, not the query itself.
- A workflow that bypasses the use case (an operator repair script, for instance) can leave a contract
  and registration disagreeing. The invariant test is the only thing standing between that and a
  silently ineligible player.
- `fixture_id` has no referential protection until Stage 6, so nothing prevents a team sheet referencing
  a fixture that does not exist in the meantime. Nothing writes one yet, and Stage 6 adds the constraint
  in the same migration that creates `competition.fixtures`.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A separate `squad.player_hidden_values` table | Real protection against an accidental `SELECT *`, but it costs a join on the engine's snapshot path and still needs the same DTO-level test, because the risk is at the response boundary rather than at the table. |
| JSONB for hidden values or for attributes | The JSONB policy forbids it: attributes are filtered on and used by formulas, and hidden values are compared against ability. `JSN-4`. |
| A `CHECK`-style trigger comparing the active contract and registration | A trigger is invisible in the aggregate that owns the invariant, fires per row, and would have to be redefined for every transfer and rollover path. The state is cross-row, so the check belongs where the transaction is composed. |
| A `club_id` mirror on `player_registrations` with a unique index | Denormalisation without a benefit: it makes disagreement expressible as a constraint only by also making it storable. |
| Defer the whole team-sheet schema to Stage 6 | Contradicts the Stage 4 deliverable list and the fixture-independent-lineup work, and Stage 3 set the precedent of shipping a shell with its stage. |
