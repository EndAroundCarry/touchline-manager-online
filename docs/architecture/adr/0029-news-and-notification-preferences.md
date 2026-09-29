# ADR-0029: The division news feed and notification preferences are comms-module data, with the feed public and the preferences per manager

- **Status:** Accepted
- **Date:** 2026-09-28
- **Stage:** 11
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0027](0027-inactivity-ladder.md), [ADR-0028](0028-outbox-notifications.md), master plan §6.9, §10.7, §16 Stage 11, game rules `COM-1`, `COM-2`, `COM-3`, `COM-4`

## Context

Stage 8 shipped the inbox and deferred two halves of the comms scope: the **news feed** (a
world/country/division-scoped stream of public events, `news_items` in master plan §6.9) and the **deadline
reminder**, and it recorded both as belonging to Stage 11 "alongside the notification preferences it needs to
know whether a manager wants one." Stage 11 therefore has to decide what news is, what a preference is, and how
a reminder is materialised.

## Decision

**1. News is the comms module's own table, and the feed is public.** `NewsItem` (`comms.news_items`) carries a
world, an optional country and division, a category, a stable template key, and a parameter document — the same
durable-token, derived-prose contract the inbox uses (`COM-2`, ADR-0001). `PostNews` stages an item in the
caller's transaction; `GetNews` reads a keyset page, optionally filtered by division or country. The feed needs
no manager profile, because it is public game data like the table and the calendar.

**2. News is posted where the event happens, in its transaction.** A tier activation posts
`news.division.provisioned` inside `ProvisionDivision`; a completed transfer posts `news.transfer.completed`
inside the market's notification composer; a published round posts `news.result.published` per fixture inside
`MatchdayNotifications`. Written in the event's own transaction, the feed can never report an event that rolled
back or miss one that committed.

**3. Notification preferences are one row per manager, absent meaning the defaults.**
`NotificationPreferences` (`comms.notification_preferences`) carries four switches — deadline reminders,
inactivity warnings, market messages, and the news digest — all defaulting on. A manager who has never set one
has no row, and every reader treats absence as all-on, so a fresh manager is reachable without a row existing
first. The preferences read and change use `ETag`/`If-Match` (`CONC-1`), with version zero authorising the first
save.

**4. The reminder is materialised per round and sent once.** `ReminderScheduler` (worker-only) enqueues
`comms.deadline-reminder` for every unplayed round whose team sheet locks within
`WorldRuleSet.DeadlineReminderLead` (24 hours). The business key is the round, so a round is reminded once.
`SendDeadlineReminders` writes an inbox reminder and, unless the manager has opted out, an outbox email for every
club a human holds in that round.

**5. Consultation is explicit at each writer.** Every writer that would send an email checks the manager's
preferences first: the inactivity ladder and the reminder job both load the affected managers' preferences in
one query and skip the outbox write when the switch is off. The inbox message is never suppressed — a
preference governs email, not the game's own record.

## Consequences

**Positive**

- The feed is a view of the world: an item exists exactly when its event committed, and re-rendering it in
  another language needs no migration.
- A manager who never opens the settings screen still gets the notifications the game means to send.
- The reminder reuses the whole notification stack — inbox, outbox, preferences — rather than adding a second
  one.

**Negative**

- A preference read accompanies every email-writing batch, and a preference change creates a row lazily. Both
  are small and bounded.
- A round's reminder is enqueued at the first scheduler pass inside the 24-hour horizon; a worker that was down
  across the whole horizon materialises it late (or, if the round already locked, sends nothing). That is
  consistent with the "late rather than skipped" rule the other schedulers follow (ADR-0003).
- The feed has no retention or digest job yet; `ExpiresAt` exists but nothing sets it, and the news digest
  preference is recorded without a digest sender.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Store the rendered prose on the news row | Defeats the durable-token contract: wording changes would be migrations and re-localization impossible (`COM-2`). |
| Require a preferences row for every manager at profile creation | A row nothing has asked for, and a second write on the profile path; absence-as-defaults is simpler and equivalent. |
| Make the feed require a manager profile | News is public game data; gating it on a profile would make a signed-in reader without a club unable to see it. |
| One job per club per reminder | The round is the unit whose deadline matters; one job per round resolves every club's reminder in a single pass. |
| Suppress the inbox message too when the preference is off | The preference governs email. The inbox is the game's own record and the shell's badge; hiding it would break the badge. |
