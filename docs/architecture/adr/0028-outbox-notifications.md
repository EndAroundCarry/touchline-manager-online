# ADR-0028: Outbound notifications are written to an outbox and dispatched asynchronously

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 11
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0008](0008-deployment-topology.md), [ADR-0029](0029-news-and-notification-preferences.md), master plan §6.9, §16 Stage 11, game rules `MOD-4`, `COM-4`

## Context

Stage 8's inbox writes its messages inside the transaction that produces the event, so a result and the news of
it become public together. A notification **email** cannot be written that way: sending is an external call that
can fail, and doing it inside a domain transaction would either hold the transaction open on a slow or dead SMTP
server, or lose the intention to send when the send failed after the domain change had committed.

`MOD-4` (master plan §6.9) names the answer: an outbox. A workflow records the intention to reach outside the
database in the same transaction as the change that caused it, and a dispatcher delivers it later. Four
decisions were open: where the row is written, how it is dispatched, how a retry avoids a duplicate, and what
the business key is when the row is not derived from a domain aggregate.

## Decision

**1. The intention is written in the caller's transaction.** `OutboxWriter` (behind `IOutboxWriter`) fills in
the identity, the correlation id, and the attempt budget and stages an `OutboxMessage` through
`IOutboxRepository`. It never saves, so the row commits with the change that produced it — the same rule the
inbox messages follow (`COM-4`). A payload is a versioned document; the email payload is one
(`EmailOutbox`), so the dispatcher reconstructs the message with no second read.

**2. One dispatcher drains due rows.** `DispatchOutbox` loads the pending rows whose `due_at` has passed, in
order, and dispatches each through its transport (`IEmailSender` for the `email` type). It runs from
`ops.dispatch-outbox`, materialised every minute by `OutboxScheduler` — a worker-only `BackgroundService`.
The dispatcher sends and marks the row published in one transaction.

**3. Delivery is at-least-once, and that is the right trade for notifications.** A worker that dies after the
transport accepted the message but before the commit resends on the next pass rather than losing the mail.
The transport is expected to tolerate that; the outbox is used for notifications, never for money.

**4. A transport fault reschedules; a defect dead-letters.** A transient send failure reschedules through
`JobRetryPolicy` the same way the job queue does; an unknown message type or an unreadable payload dead-letters
at once, because retrying it would only delay the messages behind it. A message that spends its attempt budget
dead-letters too, and the dispatcher logs it as an operations alert rather than dropping it silently.

**5. The business key is a minute bucket.** A completed job row is terminal, so the dispatcher's job cannot key
on an aggregate identity the way the other jobs do — it would never re-enqueue. `ops.dispatch-outbox:{yyyyMMddHHmm}`
makes each minute a new row, which is the one place an outbox business key is a time rather than a domain
identity.

**6. Auth transactional email stays inline for now.** Verification and reset emails are sent on the request path
today; moving them onto the outbox would widen this change into the account lifecycle. That migration is
recorded as deferred rather than half-done.

## Consequences

**Positive**

- A domain change and the intention to email about it commit together, so a notification is never lost because
  the send failed and never sent for a change that rolled back (`COM-4`).
- The dispatcher is worker-only, so an API process can never send mail (`ADR-0001`, `ADR-0008`).
- The next notification type — an inactivity warning, a deadline reminder — is a new payload and a branch,
  not a new delivery mechanism.

**Negative**

- At-least-once means a rare duplicate email after a crash between send and commit. Accepted for notifications.
- Dispatch is eventually consistent: a message is sent on the next pass after it is due, not the instant it is
  written.
- Sending inside the dispatch transaction holds that transaction open across the SMTP call. The batch is
  bounded (`Outbox:BatchSize`) so the window is small.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Send inside the domain transaction | Holds the transaction open on an external, failure-prone call and loses the intention when the send fails after the change commits. |
| Send from an in-process queue with no durable row | A crash between commit and send loses the notification, and there is no record that it was owed. |
| A per-aggregate business key | A completed dispatch job is terminal, so the next pass could never re-enqueue under the same key; the key must advance, hence the minute bucket. |
| Migrate the auth emails onto the outbox in the same change | Widens a comms change into the account lifecycle, which the plan deliberately keeps stable; recorded as deferred. |
| Dead-letter a transport fault immediately | A transient SMTP outage would lose every message instead of the one that finally exhausted its budget. |
