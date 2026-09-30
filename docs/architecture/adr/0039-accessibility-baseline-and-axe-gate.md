# ADR-0039: Accessibility baseline and the axe gate

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 13
- **Related:** [ADR-0007](0007-pwa-first-delivery.md), master plan §11.3, §15.6, §16 Stage 13, `F-44`, `F-52`, test-strategy Layer 8.

## Context

§11.3 sets the bar at **WCAG 2.2 AA** for keyboard navigation, focus visibility, contrast, labels,
dialogs, tables, reduced motion, and screen-reader status messages, and §15.6 asks for **automated axe
checks plus manual keyboard/screen-reader checks for core routes**. Stage 13 had shipped the responsive
shell, the PWA boundary, and the account surface, but nothing enforced that bar: there was no axe, no
pa11y, no ESLint, and the e2e job ran no accessibility check at all. An audit of the client found the
promise mostly kept and four real gaps:

- **The squad roster was the one unnamed table.** PrimeNG's `p-table` renders its own
  `<table role="table">` and exposes no accessible-name input, so the only table in the app that is not a
  hand-written `<table>` with a `<caption>` had no name.
- **A tactics slot could only be moved by dragging.** Assigning a *player* and a *role* already had the
  keyboard-operable assignment table, but the slot's own *position* was reached only through `drop`
  (`§11.3` requires a non-drag alternative for player, slot, and role).
- **A slot's state was invisible to assistive technology.** The words "out of position" and "unavailable"
  were rendered `aria-hidden`, and the marker's accessible name carried neither them nor the slot's
  position.
- **Focus did not move on navigation.** A single-page app swaps the view without moving focus, so a
  keyboard manager was left on the link they pressed while the page beneath them changed (WCAG 2.4.3).
  The router already set `document.title`; only focus was missing.

A first axe pass also found two contrast/mechanism issues that no static review had caught: the support
reference `<code>` fell below 4.5:1 against its own background, and an `overflow-x-auto` table wrapper
that scrolls on a phone had no keyboard access (WCAG 2.1.1).

There are **no dialogs or modals** in the app — every flow is an inline section — so nothing needed focus
trapping or `aria-modal`.

## Decision

**1. The gate is runtime axe, not a new lint toolchain.** `@axe-core/playwright` is added to the e2e
package and an `@a11y` journey scans the core routes. No ESLint/`angular-eslint` is introduced; the repo
deliberately lints with Prettier only, and runtime axe checks the rendered result — including contrast and
layout, which a template linter cannot.

**2. The scan is filtered to WCAG 2.2 A/AA tags.** `withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa',
'wcag22aa'])` keeps the gate aligned with the standard the product claims and excludes best-practice-only
rules. The target is zero violations and zero rule exclusions; an exclusion would be recorded here.

**3. The gate rides the existing e2e job, at both core breakpoints.** The `@a11y` journey lives in
`journeys/`, so the desktop and mobile projects run it beside every other journey — on a phone the card
layouts are scanned, on a desktop the tables — and CI needs no new step. The Canvas viewer is scanned in
the matchday stack, because a real replay only exists there. The journey collects each screen's violations
and fails once at the end, so a single run names every offending page.

**4. Each gap is fixed, not hidden.** The roster is named through PrimeNG pass-through
(`[pt]="{ table: { 'aria-label': … } }"`); a focused slot is nudged with the arrow keys through the same
`moveSlot` the drop uses; the slot's state and position are in its accessible name and the selection status
line; focus moves to `<main>` on every navigation after the first; the training card's focus select is
single-sourced through `aria-label` with a plain visible label, instead of a wrapping `<label>` and an
overriding `aria-label`.

**5. Every horizontal-scroll table wrapper is a focusable, named region.** `tabindex="0"` with
`role="group"` and an `aria-label` makes a table that scrolls on a narrow screen reachable and scrollable
by keyboard (WCAG 2.1.1), rather than a region only a pointer can move.

**6. The shared focus ring is a guarded contract.** The control constants keep `focus:outline-none` and the
ring survives only because the global `:focus-visible` rule is unlayered; the journey asserts a focused
text control shows a ≥2px outline, so neither side can silently remove it.

## Consequences

**Positive**

- The last unmet Stage 13 exit criterion — "manual accessibility review has no critical/high issue" — is
  backed by an automated gate over the core routes, in addition to the manual keyboard and screen-reader
  checks.
- The scans run at desktop and mobile, so the responsive card layouts are audited too, not only the tables
  a developer sees at 1280px.
- The fixes are general: a focusable scroll region, a route-change focus move, and a named table protect
  screens added later, not only the ones audited here.
- A failure names the offending element and rule, so the fixed expectations are cheap to keep green.

**Negative**

- The e2e job gains two axe scans (desktop and mobile) plus one on the matchday stack, lengthening it
  slightly.
- Axe cannot see a service-worker cache hit, reduced-motion behaviour, or a screen reader's experience;
  those remain the documented manual checks, and axe is a floor rather than a proof of AA.
- Adding `tabindex="0"` to a scroll wrapper that turns out not to overflow at the current width adds a
  tab stop that does nothing; it is accepted so no width is left without keyboard access.
- `role="group"` with a name on each wrapper adds a named group to the accessibility tree; it is chosen
  over `role="region"` so the page is not littered with landmarks.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Add ESLint with `angular-eslint` template accessibility rules | Static template lint is useful but blind to contrast, computed layout, and the rendered tree, and the repo has deliberately kept its lint to Prettier. Runtime axe covers the rendered result; a template lint can be added later without replacing it. |
| Run axe in the Vitest/jsdom unit suite | jsdom has no layout, so contrast and the scrollable-region rules cannot run; axe's value here is exactly what a DOM without layout cannot see. |
| A third dedicated `a11y` Playwright project | A third full project multiplies time; the core desktop and mobile projects already give the two layouts the gate needs, and the tablet project's `@responsive` grep is unrelated. |
| Disable the `color-contrast` rule | That would exclude the most common real defect and contradict the standard; the two failures it found were fixed instead. |
| `@axe-core/playwright` on every page load in a full crawl | Scanning a curated route set is deterministic and fast; a crawl would scan states that are not meaningful and would be flaky against data. |
| `role="region"` for the scroll wrappers | A named region is a landmark; naming every table wrapper would clutter the landmark list. A named `group` is reachable and named without becoming a landmark. |
