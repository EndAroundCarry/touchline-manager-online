# ADR-0052: Replay-v3 turns a match into one continuous film with a companion highlights reel

- **Status:** Accepted (decisions 2 and 3 superseded by [ADR-0054](0054-replay-v4-constant-pace-film.md))
- **Date:** 2026-10-02
- **Stage:** Engine roadmap, continuous replay milestone (M2 of `engine-v4-continuous-match-replay.md`)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0006](0006-semantic-highlight-keyframes.md), [ADR-0014](0014-matchday-lock-resolution-and-publication.md), [ADR-0051](0051-engine-v4-continuous-passages.md), master plan §9.1–§9.3, game rules `MAT-8`, `MAT-11`, `JSN-1`

## Context

`replay-v2` presented a match as at most two dozen independent chance snippets of ten to twenty-five
seconds, with three-keyframe "bridges" stretched across the gaps. The result had two problems visible on
screen, both verified in code:

1. **The replay cut between chances and never showed a build-up.** The interesting part of a passage of
   play — how a side got from its own half into the final third — happened in the gaps, so a manager could
   not see where a team fell short.
2. **The commentary was sterile.** `CommentaryTokenBuilder.BuildPassage` emitted exactly three generic
   lines per passage, because there were no per-pass, per-carry, or per-tackle facts in the model to
   narrate.

`engine-v4` (ADR-0051) removed the cause: a possession is now a real chain of touches with recorded ball
waypoints, an outcome-appropriate final point, and the participants the simulation actually picked. That
gives the director a real ball path to build a continuous film from, and real touches to narrate.

The product owner confirmed the shape: **one dataset, two viewing modes.** *Full match* (the default) is one
continuous condensed film of the whole match, targeting about ten minutes at 1x with a hard ceiling of
eleven. *Highlights* replays selected chances, each preceded by a genuine lead-in from the play immediately
before it, drawn from the same film. Playing football is not a video, so the film must stay a semantic
keyframe document inside the existing payload budget (ADR-0006).

## Decision

**1. The film is one continuous sequence of passages over the recorded possessions, re-derived from the
frozen snapshot and never stored.**

`ReplayDirector.Build` merges the recorded possessions into film passages of roughly equal playback length
(target `TargetPassageMilliseconds` = 9 s, capped at `MaxPassages` = 75), splitting at substitutions,
half-time, and bookings of personnel so a passage's current eleven is stable and a substitute genuinely
appears. Each passage carries its entity list (the current eleven plus the ball), its tracks, its
synchronized commentary, its match window (`StartMatchSecond`/`EndMatchSecond`), its emitted event
sequences, and an `OutcomeCode` (`goal`, else the shot outcome, else `play`). The director is renamed
`HighlightDirector` → `ReplayDirector` and its version becomes `replay-v3`. The presentation contract
changes cleanly: `Highlights` → `Passages`, a new `Reel`, `Bridges` removed, one `passage` playback segment
per passage.

**2. A deterministic time warp fits the match into a nine-to-eleven-minute film, weighted by what is worth
watching.**

`targetFilmMilliseconds = clamp(totalMatchSeconds * 1_000 / 9, 9:30, 11:00)`. Each passage is weighted —
base 1.0, ×2.0 a goal, ×1.5 a shot, ×1.25 a final-third entry, ×0.8 a middle-third turnover — then
normalised to the target with a ~1.2 s floor (`MinPassageMilliseconds`). Chances are readable; dull spells
fly by. Because the weights are a pure function of the recorded outcome, the film is deterministic and the
hard ceiling holds for any match length.

**3. The eleven's off-ball shape follows the real ball path, and recorded touches overwrite it; boundary
frames are copied exactly, so the film joins rather than cuts.**

Tracks are sampled from `TacticalFormationResolver.ResolvePosition(slot, isHome, hasPossession,
ballPosition, instructions, rules)` over the actual ball path, plus small deterministic jitter, so the
block moves up and down the pitch with the play. Involved players overwrite their shape with their
recorded touch paths — the carrier runs, the receiver meets the pass, the shooter's run, the keeper's
angle. The last frame of one passage is copied as the first frame of the next, which is what makes the
"bridge" concept disappear (`MAT-8`: presentation consumes events and cannot change the outcome).

**4. The reel is a server-side playlist of chance clips over the same film, not a second presentation.**

Goals are always kept; the rest of the reel goes to the best chances by `QualityBasisPoints`
(`MinQualityForShotBasisPoints` = 700, woodwork and direct free kicks included), count-capped at
`MaxReelClips` = 12 with goals excepted. Each clip reaches back over a lead-in of up to
`ReelLeadInMatchSeconds` = 600 match-seconds of film (~65 s), clamped to 25–70 s; overlapping clips are
merged so two chances seconds apart share one stretch. If the reel would exceed `MaxReelMilliseconds` = 12:00
the lead-ins are shortened to their floor first, and only then are the lowest-quality non-goal clips dropped
— goals survive both, because a manager who scored and cannot watch it has been given a worse product than
one whose best save was omitted.

**5. The payload is guarded by a deterministic adaptive compression ladder inside the existing budget.**

A full-match film is close to `match_presentation_payload_budget_kb` = 750 (ADR-0006). The director
recompresses the tracks at widening tolerances (32 → 40 → 48) and sampling intervals (400 → 600 → 800 ms)
until the estimated payload fits, so the same match always lands on the same rung. Presentation is
re-derived, never stored: the ETag becomes `{OutputHash}:{PresentationVersion}` (from `{OutputHash}`), so a
future replay revision invalidates cached payloads without a data migration.

**6. Commentary becomes `commentary-v3`, narrating build-up beats from the recorded touches.**

`CommentaryTokenBuilder` gains the build-up families — `match.build.pass`, `match.build.carry`,
`match.build.dribble`, `match.build.cross`, `match.build.header`, `match.build.tackle`,
`match.build.interception`, `match.build.save`, `match.build.chance` — with three to five deterministic
variants each. The policy emits a beat for every meaningful touch (progressive passes, carries, dribbles,
crosses, tackles, shots, saves) and skips filler square passes, aiming at roughly a row every few seconds of
film. Parameters remain name/value fact pairs — player names, club, clock — and never a hidden value
(`MAT-11`); the full-match log gets the same lines.

## Consequences

**Positive**

- The replay finally shows build-up: one continuous film of the whole match, joined rather than cut, and a
  companion reel that gives each chance its genuine lead-in.
- Event coordinates become a fact the viewer can trust, because the ball path is simulated (ADR-0051)
  rather than invented by the director.
- Commentary is a by-product of the same recorded touches, so the feed is synchronized with the action
  without a second source of truth.
- Two modes over one dataset: there is no empty state and no match a manager cannot watch.
- Presentation is re-derived, so there is no schema change and no stored payload to migrate; the versioned
  ETag is the only cache concern.

**Negative**

- A full-match film sits close to the payload budget. The adaptive ladder is the mechanism; a match that
  could not fit would need a superseding ADR that raises the budget with measured numbers rather than a
  silent trim.
- The film's style (passage length, weights, tolerances) is a presentation decision that will be revised;
  each revision invalidates cached payloads through the versioned ETag.
- A substitute who takes a chance is carried only from the passage in which they entered; the entity list is
  the current eleven at a passage boundary and does not reconstruct a mid-passage change of personnel.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep chance snippets with bridges (`replay-v2`) | The gaps are where the build-up is; bridging them was the fix that never worked. |
| Store the film alongside the result | Puts a replay-only, potentially large document inside a hashed, audited record and needs a schema change; re-deriving it from the frozen snapshot is cheaper and keeps one mode of simulation. |
| Build the highlights reel as a separate presentation pass | A second pass over the same film can disagree with the first; a playlist of windows over one film cannot. |
| Recolour/rebuild tracks per mode client-side | The lead-in window is a server decision (reel selection, overlaps, budgeting); deriving it twice invites drift. |
| Let the payload just grow past 750 KB | The budget exists because a match nobody can download on a phone is a worse product; the measured option is to raise it deliberately (ADR-0006), not to ignore it. |
| Store the ball as a video or image sequence | Defeats the semantic keyframe design (ADR-0006): a few kilobytes and a client that interpolates, not megabytes of frames. |
