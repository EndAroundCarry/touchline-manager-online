# ADR-0038: Responsive shell, touch-target baseline, and the breakpoint test matrix

- **Status:** Accepted
- **Date:** 2026-09-29
- **Stage:** 13
- **Related:** [ADR-0007](0007-pwa-first-delivery.md), master plan §11.1–§11.4, §16 Stage 13, `F-44`, test-strategy Layers 7–8.

## Context

ADR-0007 made the web client a responsive, installable PWA, and Stage 1 built a shell to it: a
`max-w-7xl` container that switches from a column to a row at `md`, with the navigation as a vertical
sidebar from `md` up and a horizontally-scrolling strip below it. Stage 13 audited the result and found
that "responsive" had been asserted more than implemented:

- **The navigation did not adapt.** Below `md`, fourteen destinations were a single sideways-scrolling
  strip on a 375px screen, with no menu button, no drawer, and no bottom bar. Items scrolled out of view
  with no affordance that more existed. `nav-items.ts` described a "mobile bar" that was never built.
- **Touch targets were uniformly too small.** Every button came from `control-styles.ts` at ~36px, inputs
  at ~40px, checkboxes at 16px, and many row actions were bare underline links at ~20px. Only one control
  in the app (a tactics slot marker) met the 44px guideline.
- **Dense tables were desktop-only.** Several screens rendered wide tables with **no scroll container at
  all** (squad, training, tactics' assignment table, the onboarding club list), and others buried the
  primary action — a transfer bid lived in the seventh column — behind a horizontal scroll.
- **There was no breakpoint proof.** All four Playwright configs declared one project,
  `devices['Desktop Chrome']`. No mobile or tablet project, no responsive assertion, and no gate in
  `test-strategy.md`. `F-44`'s stated evidence, a "Breakpoint E2E suite", did not exist.

## Decision

**1. The breakpoints are Tailwind's defaults, and `md` (768px) is the shell's single boundary.** No
`tailwind.config.*` and no `@theme` block is added; `md` is where the layout becomes a desktop, and the
tablet project asserts that boundary rather than inventing a third layout.

**2. Mobile navigation is a header disclosure, not a second navigation model.** A `md:hidden` button with
`aria-expanded`/`aria-controls` toggles a vertical panel listing the same `NAV_ITEMS`; the desktop sidebar
is hidden below `md`. It is a disclosure rather than a drag drawer so it needs no focus trap, closes on
`Escape`, closes when a destination is chosen, and cannot drift from the desktop list because both iterate
one model.

**3. The touch target is 44px, applied unconditionally through the shared control constants.** `TOUCH_TARGET
= 'min-h-11'` is composed into `TEXT_INPUT`, `SELECT_INPUT`, and the three buttons; checkboxes keep a small
box but gain a `CHECKBOX_ROW` label target; links used as actions get `LINK_ACTION` while inline prose keeps
the bare `LINK`. It is **not** behind `@media (pointer: coarse)`: a control should be the same size
everywhere, and the breakpoint suite measures it.

**4. A dense screen shows a card list below `md` and its table from `md` up.** Where a table's identity or
primary action is buried on a phone — squad, contracts, training focus, tactics assignment, the onboarding
club list, scouting results, transfer listings — the screen renders the table `hidden md:block` beside a
`md:hidden` card list built from the same bound data. Tables that are only read (finances ledger, a
division's statistics and table, discipline, a career) keep their table and gain a proper
`overflow-x-auto` wrapper **where one was missing**, so the table scrolls inside its own box instead of
pushing the page sideways. Each converted pair carries `data-testid` hooks so the suite can assert which
layout is presented.

**5. The core suite runs at two breakpoints; a tablet project runs the responsive spec.** The core
`playwright.config.ts` declares `desktop` (all journeys) and `mobile` (all journeys, `devices['Pixel 5']`),
and `tablet` (834×1112) which `grep`s the `@responsive` spec. A shared `support/navigation.ts` opens the
mobile disclosure when it is present, so one journey runs unchanged at both widths.

## Consequences

**Positive**

- A phone gets navigation it can use, cards instead of sideways-scrolling tables, and controls a thumb can
  hit — the §11.3 promise, met rather than claimed.
- One nav model still feeds both layouts, so a destination cannot exist on one and be missing from the
  other.
- The touch baseline is one edit in `control-styles.ts` rather than a hunt, and it applies to screens added
  later without their author thinking about it.
- The breakpoint suite proves the layouts differ and that a screen does not overflow the page, so a future
  regression is caught in CI rather than on a device.

**Negative**

- The card lists are a second rendering of the same data per screen, so a change to a squad row must be made
  in both the table and the card. The `data-testid` pair and the breakpoint spec make the divergence visible
  rather than silent, but it is duplication the table-only design did not have.
- Both layouts are in the DOM at once (one hidden by `display:none`). Hidden controls are inert for a user,
  but they enlarge the response and can confuse a DOM-based test locator; the suite scopes to the visible
  layout instead.
- Running every journey twice (desktop and mobile) lengthens the end-to-end step. The tablet project runs
  only the responsive spec to avoid a third full pass.
- The 44px floor makes app-wide buttons slightly taller on desktop, where density matters more. This is
  accepted as the cost of one consistent target size.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| `@media (pointer: coarse)` to enlarge targets only on touch | Makes desktop and phone controls differ, is invisible to the breakpoint suite, and leaves a hybrid device on the wrong side. The unconditional floor is deterministic and testable. |
| A bottom tab bar for the primary destinations | Fourteen destinations do not fit five tabs; a "More" sheet is a second navigation model to keep in step with the sidebar, for no gain over a disclosure. |
| A separate `MobileNav` component | A second list to synchronise, which is exactly the drift the one-model shell avoids. |
| Swapping layouts in the DOM by a JS `matchMedia` signal instead of CSS | Removes the hidden-duplicate controls but adds a breakpoint listener and re-render to every converted screen; CSS `display` is the smaller, declarative mechanism. |
| Converting every wide table to cards | Read-only tables (a division's statistics, the ledger) are fine scrolled on a phone; cards there are churn without a usability problem. |
| A full tablet-specific layout | The product has no tablet information architecture distinct from desktop; a third layout would be invented complexity, so the tablet project only asserts the `md` boundary. |
