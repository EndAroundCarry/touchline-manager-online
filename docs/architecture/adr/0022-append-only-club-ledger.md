# ADR-0022: The club ledger is append-only, and its balances are a projection of it

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 9
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0009](0009-time-identity-and-concurrency.md), master plan §6.8, §7.2, §16 Stage 9, game rules §13, `FIN-1`, `FIN-2`, `FIN-10`…`FIN-13`, `FIN-17`, `FIN-18`

## Context

Master plan §6.8 lists `finance.club_accounts`, `finance.ledger_entries`, and `finance.club_season_finances`.
Only the first existed: Stage 3 opened an account with a starting balance so a generated club had money to
spend, and the domain's own comment recorded that "the ledger, wages, reservations, and compensating entries
arrive in Stage 9". Stage 9 begins here, and four questions decide the shape of everything that follows.

**1. Where a balance actually lives.** `FIN-11` says cash and reserved funds update "transactionally together
with an append-only ledger entry", and `FIN-18` says "ledger replay must exactly reconstruct cash and reserved
balances". Read together, the ledger is the record and the account is a cache of it — but a starting balance
written directly onto `club_accounts` at generation is not in any entry, so a naive replay would reconstruct
zero and the two would disagree from the first club onward.

**2. What one entry holds.** A move has a cause (`FIN-3`…`FIN-9`), an amount, and a result. The data model's
columns list both the deltas and the resulting balances; whether both are stored, or the result derived, is a
choice about what a replay can prove.

**3. How a reservation is expressed.** `FIN-10` makes affordability a question about cash *after*
reservations, and `TRF-7` makes the leading bid's funds reserved and the former leader's released. Reserved
funds therefore move on their own — a bid reserves without paying, an outbid releases without receiving — and
the ledger has to be able to record a move in one balance without the other.

**4. How a retried operation is made safe.** `FIN-17` requires every financial operation to be idempotent
under retry and to carry a correlation key. The guarantee has to hold against a concurrent retry, not only a
sequential one.

## Decision

**1. The ledger is append-only and authoritative; the account is its projection.** A balance changes only by
a `LedgerEntry` being recorded, and `club_accounts.cash_minor` and `reserved_minor` are the running total of
the entries behind them (`FIN-11`, `FIN-12`, `FIN-18`). A correction is a new `Compensation` entry, never an
edit, so nothing rewrites history.

**2. The opening balance is a posting, not a value on the row.** `ClubAccount.Open` opens at zero, and the
seeder funds the club with a `LedgerCategory.OpeningBalance` entry. This is the change that makes `FIN-18`
true rather than approximately true: there is no balance in the database that a replay of the ledger cannot
reproduce.

**3. An entry stores both the deltas and the balances they produced.** The deltas are what moved; the
resulting balances are what the club held. Storing the result, and checking at insert that it is never
negative and never reserves more than the cash behind it, means a replay can be compared against the state the
account actually reached and not only against today's sum (`FIN-13`).

**4. A reservation is a reserved delta on an ordinary entry, and the account guards both balances.** Cash and
reserved funds move independently — `BidReservation` adds to reserved, `ReservationRelease` takes from it, a
wage takes from cash — so one `Post` carries both a cash delta and a reserved delta and refuses a move that
would leave either negative or leave reserved funds above the cash behind them (`FIN-10`, `FIN-13`). That
guard lives in `ClubAccount.Post`, which is the only writer of a balance, so no use case can skip it.

**5. A posting carries the entry's identity, and the description is a template and its parameters.** The
server generates the entry id (`ID-1`); the account turns the intent into a `LedgerEntry` with the next
sequence. The description is a stable template key and a stored parameter document rather than prose — the
contract the commentary and the inbox already use (`MAT-8`) — so a ledger line can be rendered in another
language later without being rewritten.

**6. Idempotency is a unique index, not a lock.** The correlation key is unique within a category
(`unique (correlation_id, category)`), so a retried operation inserts nothing the second time whether the
retry is sequential or concurrent (`FIN-17`). The sequence is unique within a club
(`unique (club_id, sequence)`), which is the ordered-walk guarantee `FIN-11` needs.

## Consequences

**Positive**

- `FIN-18` holds as behaviour: over a real seeded world, summing each club's entries reproduces its stored
  cash, reserved funds, and last sequence exactly, which an integration test asserts.
- There is one place a balance changes and one place the affordability rules are expressed, so a wage run, a
  gate receipt, an award, and a transfer all pass through the same check.
- The ledger is the audit trail the plan asks for: every move names its category, its source, and the
  operation it belongs to, which is what an operator follows when a balance is disputed.

**Negative**

- A balance is no longer a single write. Opening an account is now two staged rows, and every money-moving
  workflow must stage an entry beside its balance change — the discipline the module exists to require, but a
  discipline nonetheless.
- The account row and the ledger can drift if a future code path edits a balance directly. The database check
  constraints bound the damage (a balance can never go negative or break the reserved/cash relation) but do
  not prove the replay; the reconciliation a later milestone offers is what closes the remaining gap.
- `club_accounts` carries `last_ledger_sequence`, which must be advanced with the row. It is written in the
  same `Post`, so the two cannot diverge in practice, but it is a second fact beside the entries.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep the starting balance on `club_accounts` and treat the ledger as a log of later moves | A replay would not reproduce the opening, so `FIN-18` would hold for everything except the first fact — the worst kind of almost-true. |
| Derive the resulting balances instead of storing them | The entry would then have to be replayed against history to know what the club held, and a replay could not be checked at the row level. Storing the result is one column and makes the entry self-describing. |
| Model reservations as their own table (`market` holds the bid) and never in the ledger | `FIN-10` makes affordability about reserved funds, which live on the account; a reservation that bypassed the ledger would be a balance moved by a path `FIN-11` forbids. |
| A per-club advisory lock instead of the unique index for idempotency | A lock serialises operations that need not contend and still depends on the caller taking it; the unique index refuses the duplicate write whatever the caller does. |
| Store the rendered description text on the entry | It would hard-code one language into the ledger and make a wording change a migration — the same argument that keeps commentary and inbox messages as tokens. |
| Let each workflow write `cash_minor` and stage an entry itself | The affordability rules would then be restated wherever money moves, and the first caller to forget the reserved check would create the bug `FIN-10` names. |
