# Content, UI Tone, and Fictional-Data Policy

> **Why this document exists:** the product deliberately uses fully fictional clubs, players,
> competitions, and badges. That is a legal decision and a product-voice decision at the same
> time, so both are specified here rather than left to whoever writes the next string.

---

## 1. Fictional data mandate

| Ref | Rule |
|---|---|
| FIC-1 | Every club, player, competition, division, badge, kit, stadium, and city name is **generated fiction**. No real club, real player, real competition, or licensed mark appears anywhere in the product. |
| FIC-2 | Country display names are used as geography only. League and competition display names are generic or invented; no protected league branding is used (`WORLD-3`). |
| FIC-3 | No real badge, crest, logo, kit design, sponsor mark, mascot, anthem, or trademark is reproduced, approximated, or parodied. Badges are procedurally generated from a recorded seed. |
| FIC-4 | Player names come from curated, versioned name-pool dictionaries (per country locale key). Names are never scraped from real squads or real player registries. |
| FIC-5 | Generated names are checked against a curated blocklist of well-known real football identities and marks before a name pool ships. The check runs in the generator's test suite. |
| FIC-6 | Generated names must not accidentally reproduce a real person's full name. Where a collision is detected, the generator deterministically picks the next candidate for that seed. |
| FIC-7 | Generated content is reproducible: the same generation seed and generator version produce the same logical world (`PYR-14`). |
| FIC-8 | Name pools, badge seeds, and generator versions are versioned artifacts, reviewed like code, with the legal review noted in the change. |
| FIC-9 | Any user-supplied text field is length-limited and sanitized. Public user-generated content is out of scope for the MVP — there is no chat, no forum, no custom club names, and no uploads. |
| FIC-10 | If a real-identity complaint arrives, the response is: the affected name pool entry is blocklisted, the generator version is bumped, and **existing** generated names are left in place unless the complaint specifically requires a rename. Renames are audited because they touch history. |

### 1.1 What is explicitly not in the product (MVP)

Real clubs and players · real league or cup names · badges, kits, or crests · stadium names ·
sponsor names · manager likenesses · licensed statistics · media rights · user-uploaded
images · user-created clubs or names · national teams · continental competitions.

---

## 2. Content and voice

### 2.1 Voice

The product speaks like a calm, competent assistant manager, not like a hype engine.

| Ref | Rule |
|---|---|
| VOI-1 | Language is English (MVP), written to be localization-ready: no concatenated sentences, no idioms that break in translation, no plural forms baked into a string. |
| VOI-2 | Second person, present tense, active voice. "Your team sheet is missing a goalkeeper." Not "A goalkeeper has not been selected." |
| VOI-3 | State the fact first, then the consequence, then the action. Never the reverse. |
| VOI-4 | Deadlines are always shown in the viewer's local time and always stated as an absolute moment ("Locks Tue 20:30"), never as a bare countdown with no anchor. |
| VOI-5 | Money is displayed in the single canonical display currency with consistent formatting and explicit thousands separators. Never abbreviate in a context where the exact amount matters (bids, fees, wages). |
| VOI-6 | No celebration language for losses, no shaming language for inactivity, and no blame framing. A user who returned after three weeks should feel welcomed, not scolded. |
| VOI-7 | Errors never expose internals: no stack traces, no exception type names, no database text, no correlation ID as the primary message. |
| VOI-8 | Every blocked action explains **why** it is blocked and what to do next. This matters most for offline mutations, which are disabled by design (ADR-0007). |
| VOI-9 | Nothing in the product implies real-money value, betting, gambling, or purchasable competitive advantage. |
| VOI-10 | Commentary is event-driven and varied through deterministic template variants selected by event sequence, so a match never reads as the same sentence 40 times (`MAT-8`). |
| VOI-11 | Commentary and UI text never disclose hidden attributes, hidden potential, seeds, internal valuations, or detection thresholds (`MAT-11`). |
| VOI-12 | Numbers are shown with their meaning: an attribute is a number from 1 to 20 **and** a label; colour is never the only signal (see §3). |

### 2.2 Terms to use consistently

Use the glossary terms verbatim. Common traps:

| Use | Not |
|---|---|
| matchday | game week, round day |
| fixture | game, match-up (a *match* is the result/output of a fixture) |
| team sheet | lineup selection (a *lineup* is the frozen snapshot of one) |
| tenure / club | "your club record" when referring to control |
| condition (0–10,000 bp shown as a friendly scale) | "energy", "stamina bar" (stamina is an attribute) |
| snapshot | "saved state" |
| publication | "results went live" |
| rollover | "season reset", "new season update" |

---

## 3. Accessibility-in-content rules

Content decisions that are accessibility requirements, not preferences:

| Ref | Rule |
|---|---|
| ACC-1 | Attribute quality is conveyed by number **and** a short text label, not by colour alone (`ACC` in master plan §11.3). |
| ACC-2 | Teams in the 2D viewer are distinguished by more than colour: shirt numbers, a distinguishing pattern or shape marker, and team labels. |
| ACC-3 | Every canvas highlight has an equivalent text narration outside the canvas, available to screen readers. |
| ACC-4 | Status messages ("Team sheet saved", "Bid placed") are announced through an ARIA live region, not only rendered visually. |
| ACC-5 | `prefers-reduced-motion` is respected, with a text-only mode and a static event diagram offered as first-class alternatives. |
| ACC-6 | Link and button text describes the destination or action out of context ("View match report"), never "click here". |
| ACC-7 | Text selection stays enabled everywhere except drag handles and canvas controls. |

---

## 4. Legal and compliance notes

| Ref | Item |
|---|---|
| LGL-1 | Terms of Service and Privacy Policy are versioned documents. Acceptance is recorded per user with the version, timestamp, and IP hash (`auth.user_consents`). |
| LGL-2 | Users can export their account, profile, and tenure history. |
| LGL-3 | Account deletion first closes the tenure and revokes sessions, then anonymizes identity after a cooling period. Anonymized match, transfer, table, finance, and audit records required for competition integrity are retained. |
| LGL-4 | Retention periods are set and documented for raw email delivery events, security/IP hashes, and support data. |
| LGL-5 | Documented guardrails on data collection: no device fingerprinting beyond a risk hash, no third-party advertising trackers, no sale of personal data, and analytics limited to privacy-safe operational funnels (Stage 13). |
| LGL-6 | Support and operator accounts require MFA before production launch (ADR-0002). |
| LGL-7 | Real money is never used inside the game economy. There is no premium currency, no loot box, no purchasable competitive advantage, and no real-money trading between users. |
| LGL-8 | Age-appropriateness: the game contains no violence, no gambling, and no user-to-user communication in the MVP. This is recorded so that store submissions do not need to reason about it later. |
| LGL-9 | The fictional-data rules in §1 are a legal control, not a stylistic one. A violation is a release blocker. |

---

## 5. Content review gates

| Gate | When | Check |
|---|---|---|
| Name pool review | Before a name pool ships (Stage 3) | Blocklist similarity check, no real identity, deterministic generation test |
| Badge seed review | Stage 3 | Procedural only, no trademark resemblance, palettes contrast-checked |
| Template review | Stages 6, 8, 11 | No hidden-attribute leakage, localization-ready structure, tone rules |
| Commentary review | Stage 5/7 | Variation, no repetition pathology, no leakage |
| Legal page review | Stage 15/16 | Terms, privacy, retention, and fictional-data statement published |
