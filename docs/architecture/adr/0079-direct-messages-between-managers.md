# ADR-0079: Direct messages between managers

- **Status:** Accepted
- **Date:** 2026-10-21
- **Stage:** Messages plan (`create-a-plan-to-quiet-rain.md`), Milestones 1-4
- **Related:** [ADR-0003](0003-postgresql-durable-jobs.md), [ADR-0047](0047-incident-read-only-mode.md), game-rules `COM-10`…`COM-16`, `FIC-9`, threat model `D-9`, `I-13`

## Context

Managers could open any club's page but not see who ran it, and the only messages they received were the game's own (results, injuries, market news, reminders). A manager who wanted to talk to another had to leave the game. The user asked for a simple way to write to each other that is not real time.

Three written rules said no:

- The master plan listed "social chat, forums, private messages…" as a non-goal (§ non-goals).
- `FIC-9` said "there is no chat, no forum".
- The data classification put the display name in C3 and said C3 is shown to "own data only; never another manager's".

This ADR reverses the first two in part and changes the third on purpose, so the reversal is recorded in one place.

## Decision

**1. Conversations are threads with a subject, between exactly two managers (`COM-10`).** Like email: a subject, then an ordered list of plain-text messages, and either side replies inside the thread. Two managers can have several threads. A thread is addressed to *managers*, not clubs, so it outlives either side's tenure, the same way an inbox message does (`COM-1`).

**2. A thread starts from a club page, with that club's current manager (`COM-11`, `COM-12`).** The club page shows "Manager: {name}", or "AI manager" when no human holds the open tenure. The response carries the name and no manager id. A human-run club that is not your own shows a "Send a message" link. The sender must hold a club; an AI club, your own club, and a manager whose account is closing cannot be written to. Any human manager in the world can be messaged, not only the division. A reply needs no club, but is refused when the other manager's account is closing.

**3. Not real time, not pushed.** A new message reaches the other side through the existing 60-second `/sync` poll, whose response gains `unreadDirectMessageCount`. The shell badge shows the game inbox and the direct count added together. There is no socket, no email notification and no typing indicator.

**4. The Inbox page gets a second tab.** `/inbox` stays the game's messages; `/inbox/managers` lists the conversations, `/inbox/managers/new?club=` composes, and `/inbox/managers/:conversationId` is the thread. The two kinds of message never mix, so a result never looks like something a person wrote.

**5. Plain text, limits, a rate limit and a block switch (`COM-13`, `COM-14`, `COM-15`).** A subject is 1-80 characters and a body 1-1,000 after trimming; control characters other than a line break are removed; the text is stored as written and always drawn as text, never as markup. A manager can send 30 messages an hour (starts and replies together, a per-manager fixed window in the existing rate-limit configuration); the 31st gets 429. A manager can block another and unblock them later; while a block stands in either direction neither side can start a thread or reply, the blocked side is told "this manager is not accepting your messages", and the messages already written stay readable. There is no report queue.

**6. Reading is explicit and per recipient (`COM-16`).** A message is unread until its recipient opens the thread, which marks every message addressed to them in it read through its own `POST`; the `GET` of a thread does not. Messages are never edited or deleted by a manager.

**7. A conversation you are not part of does not exist.** Reading, replying to or marking another pair's thread returns 404 `CONVERSATION_NOT_FOUND`, never 403, so a guessed id learns nothing (threat `I-5`).

**8. Data (`comms` schema, one migration `DirectMessages`).** `comms.conversations` (subject, who started it, the recipient, the club it started from, `last_message_at`, version), `comms.direct_messages` (conversation, sender, recipient, body, `sent_at`, `read_at`) and `comms.manager_blocks` (blocker, blocked, `created_at`). Check constraints refuse a thread or a block between a manager and themselves. Foreign keys are `Restrict`. A partial index on unread messages serves the badge. Comms writes only `comms.*`; display names and tenures are read through the `IClubManagerLookup` port and the direct-message queries, never written.

## What changes in the written rules

- **Master plan.** Private messages between managers leave the non-goals. Public chat, forums, manager associations and public user-generated content stay out.
- **`FIC-9`.** A user text field is still length-limited and sanitized. Chat is allowed *privately between two managers*; there is still no forum, no public user-generated content, no custom club names and no uploads.
- **Data classification.** The display name (C3) becomes visible to other signed-in managers, on the club page and in conversations. It is chosen at sign-up and cannot be changed, and the sign-up form and Settings say so. Message bodies are C3 user content owned by both participants, retained like inbox messages.
- **Threat model.** New rows `D-9` (message abuse: spam, harassment, markup) and `I-13` (the display name and a conversation's contents as personal data).

## Alternatives considered

- **A single running chat per pair of managers.** Rejected by the user: threads with subjects keep topics apart and read like the email managers already know.
- **Messages only within a division, or only between clubs in the same league.** Rejected by the user: any human manager in the world can be reached, which is why the rate limit and the block switch matter.
- **Messages addressed to clubs.** Rejected: a new manager would inherit another person's conversations. Addressing the manager is the same rule as `COM-1`.
- **A report queue for operators.** Offered and left out: the limits and the block switch are the first line, and a queue needs an admin surface that does not exist.
- **Real-time delivery (WebSocket).** Rejected: the game has no socket transport (`CAL-3`) and the 60-second poll already carries the badge.
- **Marking read on `GET`.** Rejected: a prefetched or retried read would clear a badge the manager never saw; reading is an explicit command, the way inbox messages work (`COM-5`).

## Consequences

- A manager's display name is now visible to every signed-in manager. Because it cannot be changed, a name is a permanent handle other managers can see.
- The unread count (`/sync`) now reads two tables. The partial indexes keep both reads to unread rows only.
- Blocking is one-sided to create and two-sided in effect: the blocker is told to unblock before replying, and the blocked manager is told only that the other is not accepting their messages.
- **Not done, and left open:**
  - **Anonymizing a closed account's name.** The account-closing job belongs to the privacy-workflow stage (`DeleteAccount` remarks). Until it runs, a closed manager's old conversations still show the stored display name. The stage must replace it with "Former manager" and decide what happens to message bodies. `GET /me/export` does not include conversations either.
  - **An email for a new message** (a `COM-8` preference and an outbox kind).
  - **A report-message queue** in admin tools.
  - **Archiving or muting a thread.**
  - **A manager profile page.**
