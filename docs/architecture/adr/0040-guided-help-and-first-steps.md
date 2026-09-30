# ADR-0040: Guided help and the first-steps surface

- **Status:** Accepted
- **Date:** 2026-09-30
- **Stage:** 13
- **Related:** master plan §16 Stage 13, §2.4, §11.1, §11.3, §15.6, `F-53`, `F-52`, `VOI-1`–`VOI-12`, `ACC-1`–`ACC-7`, `MAT-11`, `CAL-4`, test-strategy Layers 7–8.

## Context

Stage 13 promises, at master plan §16, to _"create guided onboarding/help for rules, deadlines, tactics,
market, and season cadence."_ Four of the stage's six deliverables had already landed (the account surface,
ADR-0036; PWA hardening, ADR-0037; responsive layouts, ADR-0038; the accessibility baseline, ADR-0039).
This is the fifth; product analytics is the sixth and remains deferred.

An audit of `apps/web` found no help surface at all: no `help/`, `guide/`, or `faq/` folder, no route, and
no navigation entry. What existed instead was explanatory text embedded where it was needed — the public
`welcome/` landing page, the data-driven `competitions/:divisionId/rules` screen, `not-found/`, and
one-line hints inside individual screens. A manager who wanted to know when their team sheet locked, what a
transfer auction actually did, or how the season ended had to already be on the screen that said so.

Three constraints shaped the decision more than the feature request did.

**The accessibility baseline is enforced, not aspirational.** §11.3 sets WCAG 2.2 AA as the floor and §15.6
asks for automated axe checks over the core routes; ADR-0039 built that gate and put it in CI. A new screen
either joins the gate or the gate is quietly lying about what it covers.

**Player-facing copy is a controlled surface.** `content-and-fictional-data-policy.md` governs it: `VOI-2`
(second person, present, active), `VOI-3` (fact, then consequence, then action), `VOI-4` (deadlines as
absolute moments, never a bare countdown), `ACC-6` (link text that describes its destination), and — the
sharp one — `VOI-11` with `MAT-11`: hidden attributes, hidden potential, seeds, internal valuations and
detection thresholds never reach a manager. Help copy is exactly where a well-meaning author restates a
mechanic and leaks one.

**Deadline display did not match its own contract.** `formatInstant` documented itself as formatting an
instant "with the zone named (`VOI-4`, `CAL-4`)", but it passed `dateStyle`/`timeStyle` and never set
`timeZoneName`, so every deadline on the dashboard, the fixtures list and the prepare screen rendered as a
bare local time. `VOI-4` asks for an absolute moment; the rendered string did not say which zone it was in.

## Decision

**1. The reference surface is static copy authored in the component.** The `/help` screen holds its five
topics as `readonly` data in `features/help/help.ts`, following `welcome/`, rather than reading a server
document. The page cannot render empty, stale, or half-loaded, and it costs no endpoint, no migration, and no
new runtime dependency. Each topic names the rule references it restates in its source, so a reviewer can
diff the sentence against `game-rules.md`.

**2. The guidance is a session-dismissed card on the dashboard, not a tour.** "Guided" is a "Your first
steps" card inside the dashboard's existing claimed-club branch: four steps, each a link to the screen that
owns the work. It is dismissed by an in-memory signal, following `UpdateStore.dismiss()`, and dismissing it
moves focus to the club heading, because removing a card removes the control that had focus and would
otherwise drop the keyboard position onto the document body.

**3. The zone's name belongs to a deadline, not to every instant.** A new `formatDeadline` names the zone;
`formatInstant` is unchanged. This is not cosmetic fastidiousness: `Intl.DateTimeFormat` refuses
`timeZoneName` together with `dateStyle`/`timeStyle`, so naming the zone means naming the date components
explicitly, and applying that to the shared formatter would have put a zone suffix on eighteen call sites
that are timestamps — session issue and last-used times, ledger rows, inbox and news items — where it is
noise. Three call sites render a deadline, and all three now use `formatDeadline`. **No deadline moves:
this changes how a moment is displayed, never when it occurs**, so it is not a change to deadlines in the
sense of the ADR index's rule 2.

**4. The copy states settled rules and stops at the hidden ones.** Each point traces to a rule in
`game-rules.md` — `WORLD-4`, `CAL-1`–`CAL-4`, `TBL-1`, `TBL-11`, `PR-1`–`PR-7`, `OCC-1`–`OCC-3`,
`SQ-2`, `TAC-1`–`TAC-10`, `INS-1`–`INS-10`, `SCT-1`, `SCT-3`, `TRF-2`–`TRF-7`, `TRF-13`, `FIN-10`,
`FIN-15`, `CON-6`, `CON-8`, `CON-10`. Retirement and valuation are stated as facts a manager can observe
(_an older player may announce that the coming season is their last, and the announcement reaches your
inbox_) without the chance, the age curve, or the value formula behind them (`CON-10`, §18
`retirement_*`/`player_valuation_*`, `MAT-11`).

**5. The new screen joins the gate it would otherwise weaken.** `/help` is added to the signed-in route list
in `journeys/accessibility.spec.ts`, so the axe journey scans it at desktop and mobile, and
`journeys/help.spec.ts` proves it is reachable from the shell, carries all five subjects, and leads to the
screen that owns a topic. Two unit guards make the content policy executable: no topic may contain a
forbidden disclosure token (`potential`, `seed`, `valuation`, `%`), and every link must be an absolute
in-app path, so a typo cannot land on the catch-all route.

## Consequences

**Positive**

- A manager who has never played finds the five subjects in one place, each with the rule, the consequence,
  and the screen that acts on it — reachable offline, because it reads nothing.
- The deadline a manager is actually racing now says which zone it falls in, on all three screens that show
  one, which is the promise `VOI-4` made and the code had not kept.
- Both the disclosure boundary and the link targets are asserted in the suite rather than left to review, so
  the two ways this page can silently rot fail the build instead.
- No migration, no server change, no new runtime dependency; the whole milestone is the web client and its
  tests.

**Negative**

- `game-rules.md` now has a second place where its rules are stated in prose. A rule whose *value* changes
  leaves the help sentence stale, and the topic spec cannot detect that — it checks structure, links and
  disclosure, not numbers. The rule references in the source are the mitigation, not a solution.
- The dashboard guidance returns in every new session, because nothing is persisted. A manager who does not
  want it dismisses it again; the alternative was a second client-storage idiom for a hint.
- `formatInstant` and `formatDeadline` are two formatting paths for the same input, chosen by call site.
  Choosing wrongly is silent: it produces a plausible time without the zone, which is exactly the defect
  this ADR fixes.
- `/help` sits behind the shell guards, so it is unreadable while signed out. The player-facing
  rules/privacy/terms/status/support pages the master plan promises at Stage 15 still do not exist, and
  `register.html` asks a manager to accept terms and a privacy policy that have no page.
- The guidance is not personalised. It does not know whether the manager's squad is already legal or their
  tactics already set, so it can point at work already done.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| A coach-mark or spotlight overlay tour | No tour or tooltip library exists in the app, and an overlay that intercepts pointer events, repaints over content and moves the focus order is the hardest thing here to keep WCAG 2.2 AA clean under the axe gate. It also cannot be read as a document by a screen reader, which is what a rules reference has to be. |
| Persist the dismissal in `localStorage` | The client deliberately keeps nothing in browser storage — `session-store.ts` states the access token lives in memory and "not in `localStorage`, not in `sessionStorage`". Introducing a storage idiom for a dismissible hint would be a disproportionate precedent. |
| Derive "new club" from an incomplete default team sheet, so the card disappears on its own | Better novelty detection, but it costs the dashboard a third read and couples it to tactics state to decide whether to show a hint. The dashboard already documents keeping its reads separate. |
| Put `timeZoneName` on `formatInstant` | `Intl` throws when `timeZoneName` is combined with `dateStyle`/`timeStyle`, so it would have meant reshaping the shared formatter; and eighteen of its call sites are timestamps, not deadlines, where a zone suffix is noise rather than clarity. |
| Reuse an existing `F-NN` row instead of adding `F-53` | Help maps to no single requirement in master plan §20, and `F-52` set the precedent: a cross-cutting UX requirement traced to a plan section gets its own row and its own evidence rather than being bent onto a neighbouring one. The mapping is recorded in `mvp-traceability.md` §2. |
| Serve the help as a server document | Puts player-facing rules copy behind an endpoint, a read, and a migration for content that changes only when a rule does — and gives the page failure modes (loading, empty, stale) that a reference has no need for. |
| Non-production analytics for which topic is read | The stage's analytics deliverable is explicitly deferred to its own milestone, and instrumenting a help page before the privacy-safe funnel decisions exist would prejudge them. |
