import { Highlight } from './match.models';
import { MatchPlayback } from './match-playback';

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
});
