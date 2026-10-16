# ADR-0069: Commentary narrates every action at its start

- **Status:** Accepted
- **Date:** 2026-10-16
- **Stage:** Engine roadmap, `create-a-plan-for-hidden-forest.md` Milestones 1–5
- **Related:** [ADR-0054](0054-replay-v4-constant-pace-film.md) (decision 6), [ADR-0066](0066-engine-v12-discrete-tick-match-engine.md), [ADR-0067](0067-tick-film-whole-moves-fluid-motion-and-smarter-play.md), [ADR-0004](0004-deterministic-match-engine.md)

## Context

The 2D viewer shows one commentary banner, as Football Manager does: a line for what is happening at the playhead. Played at the tick film's twice-pace, the banner ran two or three actions behind the ball. The client was not the cause; it adds no queue, delay or minimum display time. Three causes sat on the server:

1. **Thinning.** `TickReplaySynthesizer.Commentary` kept a build-up beat only if it was `CommentaryGapMilliseconds` = 2,500 ms of film from any other beat ([ADR-0054](0054-replay-v4-constant-pace-film.md), decision 6). At twice the pace that is about five seconds of play, so most touches had no line and the banner kept the last kept one.
2. **Intent was not recorded.** `TickActionStamp` held a frame, an entity and an action. The intended receiver of a pass was never written down, so the synthesizer guessed it by looking ahead for the next `Receive` and dropped the pass when it found none. Starting a dribble wrote no stamp. A through ball, long ball, clearance and recycle were all a plain `Pass`; a catch, a parry and a tip were all a `Save`.
3. **Some outcomes were stamped late.** `Bypassed` came about a second after the defender was passed, and `ShotOffTarget` only when the shot's lifetime ran out, not when the ball went out.

## Decision

**1. The stamp says whom and how.** `TickActionStamp(Frame, Entity, Action, Target = -1, Detail = None)`. `Target` is a slot entity index, the same encoding as `Entity`: the intended receiver of a pass or cross, the defender a dribble is aimed at, the carrier a tackle was made on, the shooter of a save, block or interception. `TickActionDetail` names the kind: `ThroughBall`, `LongBall`, `Clear`, `Recycle`, `TackleWon`, `PokedLoose`, `Caught`, `Parried`, `TippedAround`, `Smothered`, `Spilled`, `Blocked`, `LooseBall`, `LongRange`. Two new `PassageAction` values, `TakeOn` and `Break`, are stamped when a carrier's new decision becomes a dribble at a defender within a take-on radius, or into open space past halfway. The loop remembers the last dribble it stamped, so a re-decision every `DecisionInterval` does not repeat the line. None of this feeds the simulation, the result or the output hash: the stamps are written to the recording only, and neither new action is a keyframe tag.

**2. Every action gets a line at its start.** The synthesizer takes the second name from `Target` and falls back to the old look-ahead only when it is `-1`. A pass with no known receiver is narrated with a no-name variant, not dropped. A completed pass has no line of its own: the receiver's next action is the next line. A pass that fails does: a cut-out names the interceptor, one that runs out of play says so. Outcomes in the same frame are one line (a tackle and the dispossession it caused; an interception and the misplaced pass); an event beat in the same frame as an action beat wins, except that a save or block with a detail replaces the generic shot-saved or shot-blocked event line.

**3. The 2.5 s gap is replaced by two rules on the tick path.**

- **Key dwell** (`CommentaryKeyDwellMilliseconds` = 1,200). A key beat is a shot, save, header, cross or any event. After one, routine beats in the next 1.2 s of film are dropped; another key beat still shows.
- **Collision** (`CommentaryMinGapMilliseconds` = 250). Of two routine beats within 250 ms, the higher priority is kept, or the later on a tie, so a line is never on screen for a single frame.

`CommentaryGapMilliseconds` stays for `FilmAssembler`, the possession engine's path.

**4. Late stamps are repaired in the film, not in the engine.** `ShotOffTarget` is placed at the first recorded frame after the strike where the ball is outside the pitch. A shot-outcome line within `CommentaryStrikeHoldMilliseconds` = 300 ms of the shot line is pushed to strike + 300 ms, so "shoots!" can be read. A passage with no beat in its first `CommentaryOpenerWithinMilliseconds` = 400 ms gets an opener at time 0 (the restart, or "{club} have the ball"), so a cut never leaves the previous passage's line on screen.

**5. The banner is one fixed line and only key lines are announced.** The viewer keeps one line at a fixed height, truncated, so a new line never moves the pitch; key lines (shot, save, goal, card, foul) are bold. The text switches at once and only the background fades, because animating the text would bring the lag back. The visible banner has no `aria-live`; a hidden live region follows the key lines only, since announcing every pass at twice the pace would drown the match for a screen reader.

**6. The pace stays at twice.** The tick film is a selection at twice the pace ([ADR-0067](0067-tick-film-whole-moves-fluid-motion-and-smarter-play.md)); the lag was thinning and a missing intent, not speed. Slowing the film would lengthen every film and change how fast the ball looks on screen, and it would not remove the thinning. The product owner chose to keep the pace and to make the lines fit it: they are short (about eight words at most) so they can be read at the pace the film plays.

**7. Versions.** `TickReplaySynthesizer.Version` is `tick-replay-v4`, which the presentation ETag is keyed on, so a cached film is refetched once. `CommentaryTokenBuilder.Version` is `commentary-v6`. The Report tab's full-match templates are unchanged except for two goal variants that name the beaten keeper.

## Consequences

- The banner changes as each pass is struck, a failed pass shows who cut it out, and the keeper's catch, parry or tip is named.
- More lines are sent. On one 14-v-14 match (seed 4242) the film carried 61 lines a film minute, 1,015 in all, and the commentary was about 373 KB of a 2,340 KB estimate, against a 2,750 KB budget. Each line carries `clubId`, `playerId` and `secondId` parameters (about 370 bytes a line), so any growth in payload is cut by shortening variants and parameters, not by dropping beats. A coverage and payload probe over many matches is still to be built (plan, Verification step 2).
- `Break` is noisy: 69 lines against 27 take-ons in that match. It is an M1 stamping threshold and can be tightened without touching the synthesizer.
- The gaps between lines inside a passage had a median 2.25 s, p95 3.5 s and a maximum of 5 s in that match; the aim of 3 s with the ball in play is not yet measured by a real probe.
- Results, hashes and determinism are unchanged, and the golden hash test and `DeterminismTests` pin it.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Lower `CommentaryGapMilliseconds` to about 1 s | Removes some of the lag but keeps the guessed receiver and the missing dribble, tackle and save detail, so the lines that are shown are still the wrong ones. |
| Slow the film to 1× | Changes every film's length and the ball's apparent speed to fix a lag that was thinning. |
| Queue lines and show each for a minimum time | Brings the lag back: the banner would fall behind by the sum of the minimum times. Only key lines are held, by the server's dwell rule. |
| Announce every line to screen readers | At twice the pace a pass is struck every second or two. |
| Infer the receiver and the outcome in the synthesizer, as before | The synthesizer saw only the frames after the fact; the engine knew the intent at the decision. |
