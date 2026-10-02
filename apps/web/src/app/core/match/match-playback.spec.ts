import { Passage, PlaybackSegment, ReelClip } from './match.models';
import { MatchPlayback, PLAYBACK_SPEEDS } from './match-playback';

/**
 * The film playback guarantees (`replay-v3`).
 *
 * The player is pure and timer-free, so every requirement about speed, the film timeline, the reel playlist,
 * skipping, and seeking is asserted here with plain calls rather than by waiting for a browser to animate
 * something.
 */

function passage(
  sequence: number,
  durationMilliseconds = 1_000,
  minute = 10,
  overrides: Partial<Passage> = {},
): Passage {
  return {
    sourceEventSequence: sequence,
    minute,
    stoppageMinute: 0,
    startMatchSecond: minute * 60,
    endMatchSecond: minute * 60 + 1,
    durationMilliseconds,
    outcomeCode: 'play',
    narration: `Passage ${sequence}`,
    homeColour: '#1f4e79',
    awayColour: '#8c2f39',
    eventSequences: sequence === 0 ? [] : [sequence],
    entities: [],
    tracks: [],
    ...overrides,
  };
}

/** A schedule that lays the passages out back to back from zero. */
function schedule(passages: readonly Passage[]): PlaybackSegment[] {
  let cursor = 0;

  return passages.map((item) => {
    const segment: PlaybackSegment = {
      kind: 'passage',
      sourceEventSequence: item.sourceEventSequence,
      startMilliseconds: cursor,
      durationMilliseconds: item.durationMilliseconds,
    };

    cursor += item.durationMilliseconds;

    return segment;
  });
}

function clip(
  sourceEventSequence: number,
  startMilliseconds: number,
  endMilliseconds: number,
): ReelClip {
  return {
    sourceEventSequence,
    outcomeCode: 'goal',
    minute: 10,
    stoppageMinute: 0,
    startMilliseconds,
    endMilliseconds,
  };
}

describe('MatchPlayback', () => {
  it('has nothing to play without passages and refuses to start', () => {
    const playback = new MatchPlayback([]);

    expect(playback.hasPassages).toBe(false);

    playback.play();

    expect(playback.currentState).toBe('idle');
  });

  it('lays the film out end to end and reports the total', () => {
    const playback = new MatchPlayback([passage(1, 1_000), passage(2, 2_000), passage(3, 1_500)]);

    expect(playback.count).toBe(3);
    expect(playback.filmStartOf(0)).toBe(0);
    expect(playback.filmStartOf(1)).toBe(1_000);
    expect(playback.filmStartOf(2)).toBe(3_000);
    expect(playback.totalMilliseconds).toBe(4_500);
  });

  it('prefers the schedule the server sent for the film offsets', () => {
    const items = [passage(1, 1_000), passage(2, 1_000)];

    // The director pads the first passage; the client must honour its clock rather than the durations'.
    const playback = new MatchPlayback(items, [
      {
        kind: 'passage',
        sourceEventSequence: 1,
        startMilliseconds: 0,
        durationMilliseconds: 4_000,
      },
      {
        kind: 'passage',
        sourceEventSequence: 2,
        startMilliseconds: 4_000,
        durationMilliseconds: 1_000,
      },
    ]);

    expect(playback.filmStartOf(1)).toBe(4_000);
    expect(playback.totalMilliseconds).toBe(5_000);
  });

  it('advances the playhead by the real time when playing', () => {
    const playback = new MatchPlayback([passage(1)]);

    expect(playback.advance(1_000)).toBe(false);

    playback.play();

    expect(playback.advance(400)).toBe(false);
    expect(playback.positionMs).toBe(400);
  });

  it('scales the playhead by the speed, so the animation and the clock agree', () => {
    const playback = new MatchPlayback([passage(1)]);

    playback.play();
    playback.setSpeed(4);
    playback.advance(100);

    expect(playback.positionMs).toBe(400);
  });

  it('walks into the next passage when a frame crosses its end, and reports the change', () => {
    const playback = new MatchPlayback([passage(1), passage(2), passage(3)]);

    playback.play();

    expect(playback.advance(1_500)).toBe(true);
    expect(playback.activeIndex).toBe(1);
    expect(playback.positionMs).toBe(1_500);
    expect(playback.passageTimeMs).toBe(500);
    expect(playback.currentState).toBe('playing');
  });

  it('crosses several passages in one long frame and then finishes', () => {
    const playback = new MatchPlayback([passage(1), passage(2), passage(3)]);

    playback.play();

    expect(playback.advance(3_500)).toBe(true);
    expect(playback.currentState).toBe('finished');
    expect(playback.activeIndex).toBe(2);
    expect(playback.positionMs).toBe(3_000);
  });

  it('skips the last passage to the end rather than off the film', () => {
    const playback = new MatchPlayback([passage(1), passage(2)]);

    playback.play();
    playback.skipCurrent();
    playback.skipCurrent();

    expect(playback.currentState).toBe('finished');
    expect(playback.activeIndex).toBe(1);
  });

  it('skips all, and refuses to play once finished, until it is replayed', () => {
    const playback = new MatchPlayback([passage(1), passage(2)]);

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

  it('seeks to the passage that presents an event, including one inside a passage window', () => {
    const playback = new MatchPlayback([
      passage(4),
      passage(9, 1_000, 20, { eventSequences: [9, 11] }),
    ]);

    expect(playback.seekToEvent(9)).toBe(true);
    expect(playback.activeIndex).toBe(1);
    expect(playback.positionMs).toBe(1_000);

    expect(playback.seekToEvent(11)).toBe(true);
    expect(playback.activeIndex).toBe(1);

    expect(playback.seekToEvent(5)).toBe(false);
    expect(playback.activeIndex).toBe(1);
  });

  it('pauses at a chosen passage when seeking backwards from the end', () => {
    const playback = new MatchPlayback([passage(1), passage(2)]);

    playback.skipAll();
    playback.seekTo(0);

    expect(playback.currentState).toBe('paused');
    expect(playback.activeIndex).toBe(0);
  });

  it('pauses only while playing', () => {
    const playback = new MatchPlayback([passage(1)]);

    playback.pause();

    expect(playback.currentState).toBe('idle');

    playback.play();
    playback.pause();

    expect(playback.currentState).toBe('paused');
  });

  it("offers the plan's speeds, half speed included", () => {
    expect(PLAYBACK_SPEEDS).toEqual([0.5, 1, 2, 4, 8]);

    const playback = new MatchPlayback([passage(1, 10_000)]);

    playback.play();
    playback.setSpeed(0.5);
    playback.advance(100);

    expect(playback.positionMs).toBe(50);

    playback.setSpeed(8);
    playback.advance(100);

    expect(playback.positionMs).toBe(850);
  });

  it('plays only the reel windows, and reports their combined length as the total', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000), passage(4, 1_000)];
    const playback = new MatchPlayback(
      items,
      schedule(items),
      [clip(2, 1_000, 2_500), clip(4, 3_000, 4_000)],
      'reel',
    );

    expect(playback.currentMode).toBe('reel');
    expect(playback.totalMilliseconds).toBe(2_500);
    expect(playback.elapsedMilliseconds).toBe(0);
    expect(playback.positionMs).toBe(1_000);
    expect(playback.activeIndex).toBe(1);

    playback.play();
    playback.advance(2_600);

    // The first window ends at 2,500 film ms, so the playhead lands at the second window's start (3,000).
    expect(playback.currentState).toBe('finished');
    expect(playback.elapsedMilliseconds).toBe(2_500);
    expect(playback.positionMs).toBe(4_000);
  });

  it('maps film moments onto the reel clock for the progress markers', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000), passage(4, 1_000)];
    const playback = new MatchPlayback(
      items,
      schedule(items),
      [clip(2, 1_000, 2_500), clip(4, 3_500, 4_000)],
      'reel',
    );

    expect(playback.playlistMillisecondsForFilm(1_000)).toBe(0);
    expect(playback.playlistMillisecondsForFilm(2_000)).toBe(1_000);
    expect(playback.playlistMillisecondsForFilm(3_500)).toBe(1_500);
    expect(playback.playlistMillisecondsForFilm(2_800)).toBeNull();
  });

  it('merges overlapping reel clips, so a shared build-up is not replayed twice', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000)];
    const playback = new MatchPlayback(
      items,
      schedule(items),
      [clip(2, 500, 2_000), clip(3, 1_500, 2_500)],
      'reel',
    );

    expect(playback.totalMilliseconds).toBe(2_000);
    expect(playback.positionMs).toBe(500);
  });

  it('plays the whole film on the highlights playlist when the reel is empty, so no match is unwatchable', () => {
    const playback = new MatchPlayback([passage(1), passage(2)], [], [], 'reel');

    expect(playback.hasReel).toBe(false);
    expect(playback.currentMode).toBe('reel');
    expect(playback.totalMilliseconds).toBe(2_000);
  });

  it('keeps the film moment when switching playlists, and starts the reel when it is not carried', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000), passage(4, 1_000)];
    const playback = new MatchPlayback(items, schedule(items), [
      clip(2, 1_000, 2_000),
      clip(4, 3_000, 4_000),
    ]);

    playback.play();
    playback.advance(1_200);

    expect(playback.positionMs).toBe(1_200);

    // 1,200 film ms is inside the first clip, so the playhead is carried across.
    playback.setMode('reel');

    expect(playback.positionMs).toBe(1_200);
    expect(playback.elapsedMilliseconds).toBe(200);

    // 2,200 film ms is between the clips, so the reel restarts at the first window.
    playback.setMode('full');
    playback.seekTo(2);

    expect(playback.positionMs).toBe(2_000);

    playback.setMode('reel');

    expect(playback.positionMs).toBe(1_000);
  });

  it('seeks within the reel by elapsed time and reports whether a passage is carried', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000), passage(4, 1_000)];
    const playback = new MatchPlayback(
      items,
      schedule(items),
      [clip(2, 1_000, 2_000), clip(4, 3_000, 4_000)],
      'reel',
    );

    playback.seekToMilliseconds(1_500);

    expect(playback.positionMs).toBe(3_500);

    expect(playback.seekTo(0)).toBe(false);
    expect(playback.seekTo(1)).toBe(true);
    expect(playback.seekTo(3)).toBe(true);
  });

  it('walks across a reel window boundary in one long frame', () => {
    const items = [passage(1, 1_000), passage(2, 1_000), passage(3, 1_000), passage(4, 1_000)];
    const playback = new MatchPlayback(
      items,
      schedule(items),
      [clip(2, 1_000, 2_000), clip(4, 3_000, 4_000)],
      'reel',
    );

    playback.play();
    playback.advance(1_500);

    expect(playback.positionMs).toBe(3_500);
    expect(playback.activeIndex).toBe(3);
    expect(playback.currentState).toBe('playing');
  });
});
