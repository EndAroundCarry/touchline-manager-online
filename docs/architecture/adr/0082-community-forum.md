# ADR-0082: A community forum, international and per country

- **Status:** Accepted
- **Date:** 2026-10-22
- **Stage:** Community plan (the Messages menu becomes Community)
- **Related:** [ADR-0079](0079-direct-messages-between-managers.md), game-rules `COM-17`…`COM-20`, `FIC-9`

## Context

Managers could write to each other privately ([ADR-0079](0079-direct-messages-between-managers.md)) but not in the open: a tip about a formation, a bug report or a talk about the local league had nowhere to go. The user asked for a forum with an international part and a part for each country, and for the menu group called Messages to be renamed Community.

Two written rules said no, as they did for ADR-0079: the master plan listed forums among the non-goals, and `FIC-9` said "there is no forum". The help page said the same to managers. This ADR reverses them in part and says so in one place.

## Decision

**1. Two sections (`COM-17`).** The **International** forum has three subforums: General, Development and Bugs, and Announcements. A **country** forum has one subforum, General. The country is the country of the club the manager runs now, so a manager sees one country forum, it changes if they take a club elsewhere, and a manager who runs no club sees the international forum alone. A country thread is never shown, listed or answered for a manager of another country; reading one answers 404, as ADR-0079 does for a conversation, so a guessed id learns nothing, and replying to one answers 403 `FORUM_COUNTRY_MISMATCH`.

**2. Who may start a thread (`COM-18`).** Any verified manager in any subforum they can open, except International Announcements, where only an operator or an admin may (403 `FORUM_ANNOUNCEMENTS_RESTRICTED`). Replying is open to every manager who can open the thread, Announcements included, so a notice can be answered. The web page offers no "Start a thread" in Announcements and says that only staff do, but the server decides.

**3. Posts in the International forum are in English (`COM-19`).** The rule is a notice the server returns with every international section, subforum and thread (`languageNotice`) and the web shows above the thread list, above the new-thread form and above every thread. The game does not detect the language of a post and does not refuse one. A country forum has no such notice, because it is where a country's own language belongs.

**4. Plain text, limits and a rate limit (`COM-20`).** A title is 3-150 characters, an opening post 3-10,000, a reply 1-10,000, after trimming. The text is stored as written and always drawn as text, never as markup. A manager can post 30 threads and replies an hour together (`RateLimiting:ForumPostPermitLimit` and `ForumPostWindowSeconds`; the 31st gets 429). A post carries the author's display name and club as they were when it was written, so an old post does not change when a manager moves.

**5. No editing, no deleting, no moderation tools.** A post is final. A thread is never closed to replies. Pinned and announcement threads are made only by staff, in Announcements. There is no report queue, no ban and no search; these are left open (see below).

**6. Data (`comms` schema, migration `Stage16CommunityForum`).** `comms.forum_threads` (subforum slug, country, title, author name and club as written, pinned, announcement, reply count, last activity, version) and `comms.forum_posts` (thread, post number, author, staff flag, body, `created_at`). A unique index on `(thread_id, post_number)` means two replies at the same instant cannot both take a number: one is refused rather than a number reused. The subforums themselves are not rows: their slugs, names and rules live in `ForumConstants` and the use cases, because they do not change at run time.

**7. Pages (web).** The menu group Messages is renamed **Community** and holds Inbox, News and Forum. `/forum` is the front page (both sections with thread and post counts), `/forum/:subforumId` the thread list, `/forum/:subforumId/new` the form, and `/forum/threads/:threadId` the thread with a reply box. A thread's posts are shown oldest first on one page.

**8. A new world has a welcome announcement.** `SeedWorld` writes a pinned welcome announcement (which carries the English rule), a "Bug Reporting Guidelines" thread and an introduction thread, all by a staff author, so Announcements is never an empty room only an operator can fill. A world that already exists gets none; an operator writes one with the staff account.

## What changes in the written rules

- **Master plan.** Forums leave the non-goals as a text forum. Public chat, manager associations and public user-generated content beyond the forum stay out.
- **`FIC-9`.** A forum post is a second exception to "no public user-generated content", beside the private message: plain text, length-limited and rate-limited. It is public to every manager who can open the subforum, which is the difference from ADR-0079.
- **Help page.** "No in-game messaging" becomes "Messages and the forum".

## Alternatives considered

- **A forum per division or per club.** Not asked for; a country is the largest group that shares a language and a league pyramid.
- **A country chosen by the manager.** Rejected: the country of the club is already the manager's place in the world, and a picker lets anyone post as a local in a forum they do not play in.
- **Refusing posts that are not in English.** Rejected: language detection is unreliable on short football talk, and a refusal on a guess is worse than a stated rule.
- **Editing and deleting one's own post.** Left out with the moderation tools: an edit window needs an "edited" mark and an audit trail, and a delete leaves a hole in a thread.
- **Subforums as rows.** Rejected for now: three fixed international subforums and one per country need no admin screen to create more.

## Consequences

- A display name is now visible in public, not only to a manager who was written to. It cannot be changed, so a name on a post is permanent. Anonymizing a closed account's name in old posts belongs to the same privacy-workflow stage as ADR-0079's open item.
- A post stores a copy of the author's name and club; if the account is renamed by an operator the old posts keep the old name.
- A manager who changes country stops seeing the old country's threads and cannot reply to them, but the threads stay.
- **Not done, and left open:**
  - **Moderation**: a report button and queue, locking or deleting a thread, a mute or ban for abuse.
  - **Search, paging** (a thread list and a thread's posts are read whole), and **unread markers** or a badge for a new reply.
  - **Editing a post**, and **quoting** one.
  - **An email** for a reply to your thread.
  - **Enforcing the English rule.**
