# ADR-0006: Semantic keyframe highlights with client interpolation

- **Status:** Accepted
- **Date:** 2026-09-23
- **Stage:** 0 (implemented in Stages 5 and 7)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0007](0007-pwa-first-delivery.md)

## Context

The product needs lightweight 2D match highlights on a web client, on mobile networks, for
three matchdays a week across six countries. A matchday is 9 fixtures per division; a full
world matchday could be 54 matches. Match data is retained permanently for competition
history.

Two obvious approaches are both wrong for this product:

- **Full frame streams** (22 players + ball, per frame, at 30–60 fps): megabytes per match,
  multiplied by permanent retention and by every viewer's egress. Also forces the server to
  commit to a frame rate.
- **Server-rendered video**: expensive, opaque (cannot be re-styled or made accessible), does
  not scale with retention, and destroys the ability to pause/seek/skip cheaply.

The highlight must also be **deterministic** like the rest of the match output, so a replay
shows the same thing forever.

## Decision

Store **compact semantic keyframes**, and let the client interpolate.

**Content per highlight**

- A presentation schema version and the source match event ID (one canonical authority:
  the event).
- Match minute and event sequence, so ordering — including several highlights in the same
  minute — is unambiguous.
- A duration target, normally 5–8 seconds.
- Pitch orientation and team colours drawn from safe generated palettes.
- 22 player entities with stable match participant IDs, side, shirt number, and role, plus one
  ball entity.
- Normalized coordinates (integer 0–10,000) so geometry is resolution-independent.
- Entity keyframes: `{ t, x, y, optional easing/facing/state }`. Involved entities get many
  keyframes; uninvolved players get a small number anchored to their formation position.
- Outcome metadata (for example "saved", "goal", "off target") for accessibility narration.
- No camera hint is required for MVP; the whole pitch is shown.

**Selection**

Every goal is always retained. Penalties, high-quality shots, notable saves above configured
thresholds, and optionally woodwork events are added up to a per-match cap. When the cap is
reached, lower-importance chances remain commentary-only. All goals always remain.

**Budgets**

Target compressed match-presentation payload below 750 KB and each individual highlight
below 75 KB. Instrumentation measures real payload sizes, and pathological payloads are
rejected rather than silently shipped.

**Rendering**

A framework-neutral TypeScript renderer under
`apps/web/src/app/features/match-viewer/renderer/` draws the pitch, teams, ball, and numbers,
scales to CSS pixels and device pixel ratio without changing geometry, interpolates at the
display refresh rate with `requestAnimationFrame`, and stays independent of Angular change
detection. The loop stops and disposes on pause, route change, tab hidden, and component
destruction.

**Accessibility is a first-class requirement, not a fallback**

- Keyboard controls for play/pause, seek, skip, and replay.
- Screen-reader event narration rendered outside the canvas.
- `prefers-reduced-motion` support, plus a text-only mode and a static event diagram.
- Teams are never distinguished by colour alone.

**Playback contract**

Highlights queue in event order (including the same minute). Supported operations: 1x/2x/4x,
pause, skip current, skip all, replay, seek to event. Presentation clock pauses while a
normal-speed highlight plays, and speed settings affect commentary and animation
consistently.

## Consequences

**Positive**

- Payloads are small enough for mobile and for permanent retention.
- Animation quality scales with the viewer's device rather than the server's choice of frame
  rate.
- Highlights are deterministic, immutable, and cacheable with a strong ETag after publication.
- Accessibility and reduced-motion modes fall out of the data model instead of being bolted on:
  keyframes plus narration metadata are enough to render both an animation and a static
  diagram.

**Negative**

- The client now owns motion quality; a buggy interpolator produces a bad replay. Mitigation:
  interpolation bounds tests, per-highlight determinism checks, and frame-pacing metrics.
- Keyframe authoring is a real design problem: too few keyframes and the highlight looks
  robotic, too many and the budget fails. Mitigation: the director emits dense keyframes only
  for involved entities.
- Two schema versions must be versioned and golden-tested (events and presentation).
- An event with no highlight is invisible to a viewer who only watches animation; mitigated
  by requiring commentary for every event and by always keeping all goals.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Per-frame entity stream at fixed fps | 10–100× the storage and egress; couples server to a frame rate; permanent retention makes it worse. |
| Server-rendered video (ffmpeg) | Expensive CPU per match, opaque to accessibility and styling, cannot be cheaply seeked, and adds a heavy media pipeline. |
| Animated GIF/WebP | Fixed resolution and frame rate, no accessibility, large for 22 entities, no deterministic replay semantics. |
| Client-side re-simulation from the seed | Would ship the seed and the simulation to the client, breaking competitive integrity, and would still require commentary authoring on the client. |
| Commentary only, no visuals | Fails the product goal; the plan requires 2D highlights. |
