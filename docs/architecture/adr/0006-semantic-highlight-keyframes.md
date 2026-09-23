# ADR-0006: Semantic Keyframe Highlights over Video/Frame Streams

- **Status:** Accepted
- **Date:** 2026-09-22
- **Stage:** 0
- **Plan reference:** §9.1–9.5, §6.7 (`match.highlights`), plan §16 Stage 7

## Context

The match viewer must replay goals and key chances deterministically on any device and refresh rate, stay within tight storage/network budgets, remain accessible (text narration, reduced motion), and never require server-side rendering or video encoding.

## Decision

- Store and transmit **compact semantic keyframes**, not full frames: `HighlightPresentationV1` contains 22 player entities + one ball with normalized coordinates (integer 0–10,000), per-entity keyframes (timestamp, X/Y, easing/facing), team colors from safe generated palettes, and outcome metadata for narration.
- The client interpolates between keyframes with `requestAnimationFrame` in a framework-neutral TypeScript renderer (`apps/web/src/app/features/match-viewer/renderer/`): canvas renderer, keyframe interpolator, pitch layout, render loop, models.
- Highlight selection: every goal and penalty always; notable shots/saves/woodwork above configured thresholds; per-match caps with all-goals-always-kept rule.
- Payload budgets: whole compressed match presentation < 750 KB; each highlight < 75 KB; instrument and reject pathological payloads.
- Durations normally 5–8 s; queueing supports multiple highlights in the same minute; playback supports 1x/2x/4x, pause, seek, skip, replay.
- Renderer must scale for DPR without changing normalized geometry, run outside Angular change detection, and stop on pause/route change/tab hidden/destroy.
- Accessibility: text-only mode, static event diagram, screen-reader narration outside the canvas, `prefers-reduced-motion` support, and team distinction that never relies on color alone.
- API split: `GET /matches/{id}` (summary/stats) vs `GET /matches/{id}/presentation` (immutable commentary + highlights, strong ETag caching after publication).

## Consequences

- Storage is tiny and replays are bit-stable forever; refresh-rate independence comes free.
- Rendering complexity moves to the client — covered by interpolation-bounds unit tests and playback disposal tests.
- Visual fidelity is deliberately modest (whole-pitch MVP view); fancier cameras are post-MVP.

## Alternatives considered

- **Server-rendered video:** rejected — encoding cost, storage, no scrub/seek fidelity, breaks deterministic-replay budget.
- **100 full frames of 22 players + ball:** rejected explicitly by plan §9.1 — bandwidth/storage waste, tied to fixed frame rate.
- **WebSockets/Live animation from engine:** rejected — matches are asynchronous; MVP has no live streaming requirement (plan §2.3).
