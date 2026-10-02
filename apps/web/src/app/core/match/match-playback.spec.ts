import { Bridge, Highlight, HighlightEntity } from './match.models';
import { MatchPlayback, PLAYBACK_SPEEDS } from './match-playback';

/**
 * The replay's playback guarantees (`§9.4`).
 *
 * The player is pure and timer-free, so every requirement about speed, queueing, skipping, and seeking is
 * asserted here with plain calls rather than by waiting for a browser to animate something.
 */

function highlight(sequence: number, durationMilliseconds = 1_000, minute = 10): Highlight {
  return {
    sourceEventSequence: sequence,
    minute,
    stoppageMinute: 0,
    durationMilliseconds,
    outcomeCode: 'goal',
    narration: `Event ${sequence}`,
    homeColour: '#1f4e79',
    awayColour: '#8c2f39',
    entities: [],
    tracks: [],
  };
}

function bridge(afterEventSequence: number, durationMilliseconds = 2_000): Bridge {
  return { afterEventSequence, durationMilliseconds, tracks: [] };
}

function entity(entityId: string): HighlightEntity {
  return {
    entityId,
    isBall: false,
    side: entityId.startsWith('H') ? 'home' : 'away',
    participantId: null,
    shirtNumber: 0,
    family: null,
    x: 5_000,
    y: 3_500,
  };
}

describe('MatchPlayback', () => {
  it('has nothing to play without highlights and refuses to start', () => {
    const playback = new MatchPlayback([]);

    expect(playback.hasHighlights).toBe(false);

    playback.play();

    expect(playback.currentState).toBe('idle');
  });

  it('advances the playhead by the real time when playing', () => {
    const playback = new MatchPlayback([highlight(1)]);

    expect(playback.advance(1_000)).toBe(false);

    playback.play();

    expect(playback.advance(400)).toBe(false);
    expect(playback.positionMs).toBe(400);
  });

  it('scales the playhead by the speed, so the animation and the clock agree', () => {
    const playback = new MatchPlayback([highlight(1)]);

    playback.play();
    playback.setSpeed(4);
    playback.advance(100);

    expect(playback.positionMs).toBe(400);
  });

  it('walks into the next highlight when a frame crosses its end', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2), highlight(3)]);

    playback.play();

    expect(playback.advance(1_500)).toBe(true);
    expect(playback.activeIndex).toBe(1);
    expect(playback.positionMs).toBe(500);
    expect(playback.currentState).toBe('playing');
  });

  it('crosses several highlights in one long frame and then finishes', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2), highlight(3)]);

    playback.play();

    expect(playback.advance(3_500)).toBe(true);
    expect(playback.currentState).toBe('finished');
    expect(playback.activeIndex).toBe(2);
    expect(playback.positionMs).toBe(1_000);
  });

  it('keeps two chances in the same minute as two queued highlights', () => {
    const playback = new MatchPlayback([highlight(1, 1_000, 30), highlight(2, 1_000, 30)]);

    expect(playback.count).toBe(2);
    expect(playback.active?.sourceEventSequence).toBe(1);

    playback.skipCurrent();

    expect(playback.active?.sourceEventSequence).toBe(2);
    expect(playback.currentState).toBe('playing');
  });

  it('skips the last highlight to the end rather than off the list', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)]);

    playback.play();
    playback.skipCurrent();
    playback.skipCurrent();

    expect(playback.currentState).toBe('finished');
    expect(playback.activeIndex).toBe(1);
  });

  it('skips all, and refuses to play once finished, until it is replayed', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)]);

    playback.skipAll();

    expect(playback.currentState).toBe('finished');
    expect(playback.activeIndex).toBe(1);

    playback.play();

    expect(playback.currentState).toBe('finished');

    playback.replay();

    expect(playback.currentState).toBe('playing');
    expect(playback.activeIndex).toBe(0);
    expect(playback.positionMs).toBe(0);
  });

  it('seeks to the highlight that presents an event, and refuses one it does not hold', () => {
    const playback = new MatchPlayback([highlight(4), highlight(9)]);

    expect(playback.seekToEvent(9)).toBe(true);
    expect(playback.activeIndex).toBe(1);
    expect(playback.positionMs).toBe(0);

    expect(playback.seekToEvent(5)).toBe(false);
    expect(playback.activeIndex).toBe(1);
  });

  it('pauses at a chosen highlight when seeking backwards from the end', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)]);

    playback.skipAll();
    playback.seekTo(0);

    expect(playback.currentState).toBe('paused');
    expect(playback.activeIndex).toBe(0);
  });

  it('pauses only while playing', () => {
    const playback = new MatchPlayback([highlight(1)]);

    playback.pause();

    expect(playback.currentState).toBe('idle');

    playback.play();
    playback.pause();

    expect(playback.currentState).toBe('paused');
  });

  it('plays the recycling passage before the highlight it leads into, and nothing before the first', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)], [bridge(2, 2_000)]);

    expect(playback.count).toBe(3);
    expect(playback.highlightCount).toBe(2);
    expect(playback.activePassageKind).toBe('highlight');

    playback.play();

    // The first highlight runs for a second, then the bridge is the active passage — still counted as
    // leading into the second highlight, which is the index the timeline and panels speak.
    expect(playback.advance(1_000)).toBe(true);
    expect(playback.activePassageKind).toBe('bridge');
    expect(playback.activeHighlightIndex).toBe(1);

    expect(playback.advance(2_000)).toBe(true);
    expect(playback.activePassageKind).toBe('highlight');
    expect(playback.active?.sourceEventSequence).toBe(2);
    expect(playback.activeHighlightIndex).toBe(1);
  });

  it('gives a bridge the highlight-shaped body the renderer draws, filled in from its neighbours', () => {
    const first = { ...highlight(1), entities: [entity('H9')] };
    const second = { ...highlight(2), entities: [entity('H9'), entity('A4')] };
    const crossing: Bridge = {
      afterEventSequence: 2,
      durationMilliseconds: 2_000,
      tracks: [
        {
          entityId: 'H9',
          keyframes: [
            { timeMilliseconds: 0, x: 1_000, y: 2_000 },
            { timeMilliseconds: 2_000, x: 3_000, y: 4_000 },
          ],
        },
      ],
    };

    const playback = new MatchPlayback([first, second], [crossing]);

    playback.play();
    playback.advance(1_000);

    const body = playback.active!;

    expect(playback.activePassageKind).toBe('bridge');
    expect(body.outcomeCode).toBe('bridge');
    expect(body.durationMilliseconds).toBe(2_000);
    expect(body.tracks).toHaveLength(1);
    expect(body.entities.map((item) => item.entityId)).toEqual(['H9', 'A4']);
  });

  it('skips over the recycling to the next highlight', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)], [bridge(2)]);

    playback.play();
    playback.skipCurrent();

    expect(playback.activePassageKind).toBe('highlight');
    expect(playback.active?.sourceEventSequence).toBe(2);
  });

  it('seeks to a highlight rather than to the bridge that leads into it', () => {
    const playback = new MatchPlayback([highlight(1), highlight(2)], [bridge(2)]);

    playback.seekTo(1);

    expect(playback.activePassageKind).toBe('highlight');
    expect(playback.active?.sourceEventSequence).toBe(2);

    expect(playback.seekToEvent(2)).toBe(true);
    expect(playback.activePassageKind).toBe('highlight');
  });

  it("offers the plan's speeds and scales the playhead at eight times", () => {
    expect(PLAYBACK_SPEEDS).toEqual([1, 2, 4, 8]);

    const playback = new MatchPlayback([highlight(1, 10_000)]);

    playback.play();
    playback.setSpeed(8);
    playback.advance(100);

    expect(playback.positionMs).toBe(800);
  });
});
