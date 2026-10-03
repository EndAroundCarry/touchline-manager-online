# ADR-0054: Replay-v4 plays the whole match as one constant-pace film on one continuous timeline

- **Status:** Accepted
- **Date:** 2026-10-03
- **Stage:** Engine roadmap, fluid match film milestone (M2–M4 of `engine-v5-fluid-match-film.md`)
- **Related:** [ADR-0004](0004-deterministic-match-engine.md), [ADR-0006](0006-semantic-highlight-keyframes.md), [ADR-0051](0051-engine-v4-continuous-passages.md), [ADR-0052](0052-replay-v3-film-and-reel.md) (decisions 2 and 3 superseded), [ADR-0053](0053-engine-v5-half-time-clock-and-restart-ownership.md), game rules `MAT-8`, `MAT-11`, `MAT-12`

## Context

`replay-v3` (ADR-0052) had the right shape — one film, one reel — but a viewer watching a full match saw the
dots move very fast for a moment and then crawl or stand still. The cause was in the film and not in the
engine, and four parts of it were verified in code:

1. **The time warp.** Every passage was weighted (×2.0 a goal … ×0.8 a turnover) and the film's length was
   shared out by weight, so film-seconds per match-second varied about four-fold from passage to passage, and
   the ball's speed with it. Waypoints were spaced by touch count and not by distance, so the ball's speed
   varied about tenfold inside a passage.
2. **Anchor tracks.** Each player had three formation anchors per passage plus the recorded touches. A touch
   close in time to an anchor needed a teleport to reach, so players zipped to the ball and then drifted. The
   resolver was given neutral instructions, one possession side served a whole passage, and the block moved
   12% with the ball.
3. **The commentary and the event beats** were placed at `(minute + stoppage) × 60`, which is seconds away from
   where the ball was.
4. **The viewer** treated each passage as its own replay: it rebuilt its renderer at every boundary and drew the
   old one at the new passage's first moment, which flickered. Players followed a uniform Catmull-Rom spline
   through keyframes that were not evenly spaced in time, which overshot and wobbled.

`engine-v5` (ADR-0053) removed the facts that made a true film impossible — a possession now has a length, the
second half has a clock, every dead ball has an owner, and a shot reaches the goal — so the presentation can be
built on what the match actually did.

The product owner confirmed the targets: the **whole match** in about **ten minutes at 1x** (eleven at most,
with stoppage, never longer), **fluid** motion, the **build-up** visible (passes, carries toward goal, crosses
into the box), and a Highlights mode in which each chance comes with roughly the **previous ten match-minutes**.

## Decision

**1. The film is a script of beats played at one global pace.** `FilmScript` turns each recorded possession
into beats — carry, pass, lofted pass, cross, header, shot, clearance, duel, save, placement, and dead-ball
holds — from the recorder's `Outcome`, `Restart`, waypoints, touches and events. An intermediate pass goes to
the teammate who can reach the reception point soonest in the current shape; participants the engine named
take precedence at their beats; a ground move of 12 m or more becomes *receive → carry 3–10 m → pass*; a cross
is only drawn from a wide final-third position into the box. A possession that starts away from where the last
one ended, without a restart, gets a transition beat at physical speed.

`FilmTiming` gives every move its natural duration — its distance at the speed a ball of that kind travels,
plus the time to control it — and every dead-ball hold a fixed film duration, then solves **one pace**,
`p = motion / (target − holds)`, for the whole film. The warp is gone: no stretch of play is rushed to make
room for another.

**2. The pace is bounded, and quiet play gives way before it does.** The band is 1.8–2.9×. When the pace would
leave it, the *quietest* possessions are condensed first — those with no event, ending outside the final third,
and not within the two possessions before a chance — by merging consecutive ground moves by one side into one.
Only then is the pace allowed up to a 3.0× ceiling, and only then are the holds shortened. If the pace would
fall below the band's floor it stops there and the holds lengthen toward the target. **The film can never
exceed 11:00**, and a test enforces it: a pathological match raises the pace before it lengthens the film.

**3. The film's length is `clamp(playedSeconds / 10, 9:30, 11:00)`.** The first plan divided by nine, which was
sized for a 90-minute match. A match is about 101 clock minutes once its stoppage is played (ADR-0053), so
dividing by nine put almost every film at the 11:00 ceiling. Dividing by ten makes the median match **10:07** and
leaves the ceiling for the matches that really do run long.

**4. Motion is simulated, not interpolated.** `FilmMotion` moves the ball along the beats (ground passes ease
out; lofted passes, crosses and clearances get a parabolic height; a shot's height depends on its outcome) and
simulates each player in real-time units at a 0.1 s step under a speed cap (5.5 m/s to a shape, 8 m/s to a
ball, a keeper's dive 10 m/s) and an acceleration cap (4.5 m/s²). `FilmShape` gives each player a target from
the side's **real instructions**, shifted toward the ball (about 40% along the pitch and 30% across), compact
out of possession, with one or two pressers on the carrier and the keeper on the line between the ball and the
goal. Hard constraints override the shape — the carrier is at the ball, the receiver at the reception point when
the ball arrives, the shooter and header pair at their touches, the keeper at the save point — and **if a
constrained player cannot arrive in time the preceding beat is lengthened; the speed cap is never exceeded.**
The only discontinuities are explicit **cuts**: the kick-off after a goal and the half-time reset, listed in the
presentation and played as a 300 ms crossfade.

**5. Chunking and the contract.** The film is cut into passages of about 8–12 s of film (at most 75), split at
personnel changes and at half-time, with boundary keyframes copied exactly. `PassageV1` and `PassageResponse`
gain `Period`, `Clock` (film-time → match-second keyframes on each half's own clock, so the displayed minute at
an event is the event's stamped minute) and `Cuts`; the presentation gains `PaceMilli`. Players are sampled every
200 ms and the ball every 100 ms while it is in the air, and the payload ladder compresses in five rungs (players'
tolerance 50 → 70 → 90 → 120 → 160, sampling 200 → 600 ms). The plan's three rungs reached about 783 KB, so there
are five.

**6. Commentary and the reel follow the film.** A line is read when its beat happens, the outcome line 0.6 s
after the strike, at most about one build-up line per 2.5 s (events always get theirs). The reel's lead-in is
measured in **match time** — about 600 match-seconds, never across half-time — and clamped to 25–70 s of film; a
goal's clip runs through its celebration.

**7. The viewer plays one timeline.** `FilmTimeline` builds a presentation's passages into one continuous track
per entity, roster stints (a substitution switches the token's label; a sent-off player stops being drawn), a
half-aware clock (`45+2'`, `HT`, `46'`, `90+N'`), and the feed, cards, markers and cuts. `MatchViewer` makes one
renderer for the whole presentation and draws global film time; the clock, feed, cards and panels are read from
film time. Players follow a monotone cubic Hermite spline (Fritsch–Carlson) that knows the time between its
keyframes, so it cannot overshoot; the ball moves in straight lines because the server pre-samples its flights.
A frame is never reported as longer than 100 ms, and the pitch is drawn once onto an offscreen layer.

**8. Versions.** `replay-v4`; `commentary-v3` is unchanged (the templates did not change). The ETag
`{OutputHash}:{PresentationVersion}` is unchanged, so every cached `replay-v3` payload is invalidated without a
data migration. The presentation is still re-derived, never stored.

## Consequences

**Positive**

- One speed from the first whistle to the last. Measured over 2,000 matches: **0 teleports** outside the cuts
  (3.8 cuts a match), the ball standing still for **2.5%** of the film outside holds, no player faster than the
  sprint cap times the pace.
- The film runs **10:07** at the median (p05 9:54, p95 10:25, max 10:44) and none exceeds 11:00; the reel runs a
  median 6:57 (max 9:07).
- The build-up is shown: passes go player to player, carries and crosses appear, shots reach the goal or the
  keeper, and goals end in the net.
- The viewer cannot flicker at a passage boundary, because there is no boundary in what it draws.
- Replacing the warp and the anchors removes the largest sources of presentation state, so the director is now
  five focused files instead of one.

**Negative**

- **Ten minutes needs a fast pace.** A match's moves add up to about 26 minutes of real time (p50; p05 24, p95
  28) and its holds to about 1.1 minutes. Fitting that into ten minutes is a pace of 2.9× (p50) before anything is
  condensed, and the first plan's expectation of about 2.2× at ten minutes did not survive the measurement.
  Condensing the quietest possessions (42% of them at the median) takes the moves down to 22.5 minutes and the
  median pace to **2.53×** (p05 2.28, p95 2.83, max 3.0), which is why the band's top is 2.9× and not the 2.6× first
  written. Holding 2.2× would be an 11:26 film even with that condensing, over the ceiling. The choice between a
  slower film and a faster one is `FilmMatchSecondsPerFilmSecond` and `CondensePaceMilli`, both in
  `HighlightOptionsV1`; the ball's speed on screen (a pass at a median 23 m/s of film, a shot 44) is the number
  to judge it by.
- **The payload has almost no headroom.** The estimate is 722 KB at the median and reaches the 750 KB budget at
  the maximum, on the third or fourth rung of the ladder. The real JSON is about 2.1 MB and nothing compresses it
  yet (see the alternatives).
- `Highlights` can still merge neighbouring chances into one long window.
- Every `replay-v3` payload cached by a client is refetched once.

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Keep the time warp and fix only the zero-length possessions | Removes the burst-then-crawl of a passage but not the four-fold pace variation between passages, or the tenfold ball speed inside one. |
| Divide by nine, as first planned | Puts almost every film at the 11:00 ceiling (a median of 10:59), so the "about ten minutes" the product owner asked for is never delivered. |
| Pace 2.2× | The film is then 11:26 even with the quiet play condensed, over the hard ceiling. |
| Condense far more play to reach 2.2× at ten minutes | Needs about 3 minutes more of the moves removed. The next candidates are the possessions within two of a chance, which are the build-up a manager came to see. |
| Interpolate players between anchors with a better spline | The anchors are the defect: a touch close in time to an anchor needs teleport speed whatever the curve. Simulating the players under a speed cap removes it. |
| Brotli/Gzip on `GET /matches/{id}/presentation` | Safe (it is not an auth response, so BREACH does not apply) and the JSON would shrink a great deal, but it is deployment configuration and not a presentation decision; left as an open item. |
| Raise the payload budget | ADR-0006's 750 KB is a phone-download decision; the ladder fits inside it. |
