# Content Tone and Fictional-Data Rules

Stage 0 deliverable: English UI/content tone + fictional-data and legal rules (plan §16 Stage 0). Applies to all UI copy, commentary templates, inbox/news, emails, and docs.

## English UI/content tone

- **Voice:** direct, calm, sporty-but-neutral. Second person for instructions ("Set your team sheet"), third person for state ("Miller is suspended for 1 match").
- **Tense/length:** present tense for status, short sentences, one idea per line in alerts. No hype, no exclamation marks, no emojis unless a feature explicitly requests them.
- **Terminology:** use the [glossary](glossary.md) terms exactly — *matchday*, *fixture*, *team sheet*, *tenure*, *division-season*. Never mix synonyms (no "round"/"gameweek"/"matchday" in the same surface).
- **Deadlines:** always absolute, in the viewer's local timezone, with UTC retained from the server payload. Relative time ("locks in 42 min") may supplement, never replace, the absolute value. Server time is authoritative (clock drift mitigation, plan §10.9).
- **Errors:** RFC 9457 Problem Details with a stable machine `code`, a plain-English sentence, and the next action. Never blame the player, never expose internals/stack traces/SQL. Conflict errors (`412`, `409`) say what changed and what the manager must re-decide.
- **Commands:** every mutating screen states pending / success / validation / conflict / timeout outcomes; offline mutations are disabled with an explanation, not silently queued.
- **Numbers:** attributes display 1–20 as **numbers first**, with green/yellow/red only as a redundant cue (WCAG 2.2 AA, plan §11.3). Money shows the canonical display currency formatted from minor units — never floating point artifacts.
- **Commentary:** store `templateKey` + parameters (plan §8.6); English templates use deterministic variants keyed by event sequence to avoid repetition; commentary must never reveal hidden attributes or the seed.
- **Narration:** every highlight and canvas interaction has a text equivalent for screen readers; status changes announce via ARIA live regions.

## Fictional-data rules

- All clubs, players, competitions, badges, kits, cities, and names are **fully fictional**. No real clubs, real player names, protected league branding, or licensed marks — ever (plan §2.3).
- Country and league display names are fictionalized or generic; division names are stable per tier (e.g. country + "Tier 1"), never derived from real competition names.
- Names come from deterministic per-country name pools keyed by `locale/name-pool-key` with recorded `generation_seed` + `generator_version` (`world.generation_runs`) so any identity set is reproducible (plan §17.9).
- Badges are procedural seeds; artwork must not imitate real club or league marks. Legal review note: keep the badge generator's shape/symbol library documented for review before public launch.
- Generated identities must pass uniqueness validation (name/slug unique within world) and a profanity/collision screen before seeding.
- Never use production personal data in local/test fixtures (plan §17.17).

## Legal and privacy rules

- **Consent:** publish versioned Terms and Privacy notices; record acceptance in `auth.user_consents` (version + timestamp + IP hash).
- **Portability:** account/profile/tenure history export must be available (plan §12.4).
- **Deletion:** deletion flow = close tenure → revoke sessions → anonymize identity after a configured cooling period. Preserve anonymized match, transfer, table, finance, and audit records required for competition integrity.
- **Retention:** documented retention periods for raw email delivery events, security/IP hashes, and support data; recorded in `docs/operations/runbook.md` when implemented.
- **Licensing:** CI dependency/license scanning (plan §14.4); no copyleft-incompatible deps silently added.
- **Content boundaries:** no real-world gambling prompts, no real-money trading, no purchasable competitive advantage (plan §12.3).
