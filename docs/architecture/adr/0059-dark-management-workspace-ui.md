# ADR-0059: The web client is a dark management workspace built on role tokens

- **Status:** Accepted
- **Date:** 2026-10-04
- **Stage:** Stage 13 follow-up, web client look and feel
- **Related:** [ADR-0007](0007-pwa-first-delivery.md), [ADR-0039](0039-accessibility-baseline-and-axe-gate.md), master plan §11.1, §11.3

## Context

The web client was a light, centred, card-per-screen layout with its palette written into every template
(`bg-white`, `text-slate-500`, `border-slate-200`, …, about 900 occurrences across 40 files). It worked, but it did
not look or feel like the thing it is: a management simulation, where a manager compares many numbers, jumps
between a dozen areas, and spends most of a session inside tables. The layout also wasted the screen (a
`max-w-7xl` column on a wide monitor) and nothing in the chrome said where the manager was or what to do next.

## Decision

**1. The client is a dark workspace with one accent.** A near-black ground, slightly lighter panels, thin borders,
chalk text, and a single periwinkle accent for everything interactive. Colour on screen is reserved for data (the
attribute bands, the condition bars, the result chips), because the chrome around it is neutral. The headings are
the condensed display face (Barlow Semi Condensed) and the body and data are IBM Plex Sans with tabular figures, so a
column of numbers lines up. Both are self-hosted from `@fontsource` packages: no request leaves for a font CDN, and
the service worker caches them with the shell.

**2. Templates name a role, not a palette step.** `styles.css` defines the tokens once in a Tailwind `@theme`
block: `ground`, `rail`, `panel`, `raised`, `line`, `line-soft`, `line-strong`, `ink`, `ink-2`, `muted`, `accent`,
`accent-strong`, `on-accent`. Templates use `bg-panel`, `text-muted`, `border-line` and so on, and the shared
control constants in `control-styles.ts` follow suit. Retuning the look is one edit; a new screen cannot drift back
to a light page. Status colours keep their Tailwind hues but use the dark-surface shades (`text-red-300`,
`bg-amber-500/10`, `border-emerald-500/40`).

**3. PrimeNG follows the same tokens.** The Aura preset is extended with `definePreset`: the primary ramp is the
accent, and the dark surface ramp is the same ramp as the Tailwind tokens, so a `p-table` and a hand-written panel are
the same colours. `<html>` carries `app-dark`, the class `darkModeSelector` already named.

**4. A new `app` CSS layer sits between PrimeNG and the utilities.** The layer order is now
`tailwind-base, primeng, app, tailwind-utilities`, and `theme.options.cssLayer.order` in `app.config.ts` says the
same. The `app` layer holds the heading faces, the dense-table treatment (36px rows, 13px type, an uppercase header
strip), and the `.panel` / `.panel-title` pair every overview screen is built from. The focus ring stays outside
every layer on purpose: an unlayered rule beats a layered one, which is what stops a control's own
`focus:outline-none` from removing it (the accessibility gate asserts the ring).

**5. The shell is a management shell.** A top bar (history arrows, crest, the next-fixture call to action, the
manager's name, sign out), a persistent left rail with the fourteen destinations in six labelled groups (Club, Team,
Season, Market, Messages, Game), and a full-width content area. The rail is sticky on `md` and wider. The big button
is **Prepare**, not "Continue": Touchline is played on fixed matchdays, so nothing is advanced by the manager, and
the one thing they can still change is the next side. It links to that fixture's team sheet and shows the lock
countdown. The shell reads the manager's fixture list once if no screen has.

**6. The dashboard is a grid of widgets.** Next match (with a calendar of matches, the team-sheet deadline and auctions
closing), league table, club record, inbox, squad status, pending transfers and player stats, each a `.panel` that reads
its own store and links to the screen it summarises. The shaping of each widget's data is pure and lives in
`dashboard-presentation.ts`.

**7. The match viewer is unchanged.** It was already a dark surface with its own tokens and a canvas renderer; it
now sits on a dark page rather than inside a light one.

## Consequences

- Every template changed, but only by class substitution and by the dashboard, the shell, and the tactics board
  gaining their panel structure. No component logic changed except the shell (the rail groups, the next fixture, the
  history arrows) and the colour constants in the presentation helpers.
- The training heat-map tints changed from pale sky blues to translucent sky over the panel (`bg-sky-500/15`, `/30`,
  `/45`) with the 200 shade of the band colour as text; the chart series colours were lifted to read on a dark
  panel. The specs that assert those class names were updated with them.
- **Light theme.** A toggle in the top bar switches between dark (the default) and light. The choice is a preference of
  the device, kept in `localStorage` under `touchline.theme` and applied as `app-dark` or `app-light` on `<html>`; an
  inline script in `index.html` applies it before first paint. The light theme is a second set of values for the same
  tokens plus darker values for the status text shades, in `styles.css`; PrimeNG gets a light scheme in the preset. The
  match viewer and the tactics pitch are dark in both themes (`.theme-dark` restores the dark tokens inside them).
  Chart.js reads its ink from the tokens and is rebuilt when the theme changes; the training chart's series colours are
  mid-tones that hold 3:1 on both panels.
- The accessibility gate (`@a11y`, axe at WCAG 2.2 AA, including contrast) passes on the new palette, and a light-theme
  journey in the same file runs the same scan with the light theme on.
- A design canvas of every screen, drawn with the same tokens, accompanies this change.
