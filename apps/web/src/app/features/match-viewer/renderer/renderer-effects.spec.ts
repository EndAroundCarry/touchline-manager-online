import { Passage } from '../../../core/match/match.models';
import {
  celebrationStartMilliseconds,
  duelClashes,
  isDiveAction,
  isStrikeAction,
  isTackleAction,
  isTrailAction,
  nearestPlayerToBall,
  trailStrength,
  wantsTrail,
} from './renderer-effects';
import { FrameEntity, MatchSide } from './renderer.models';

/** The action effects' guarantees (Stage 6). */

function player(
  id: string,
  side: MatchSide,
  x: number,
  y: number,
  action: string | null = null,
): FrameEntity {
  return {
    entity: {
      id,
      isBall: false,
      side,
      participantId: id,
      shirtNumber: 9,
      family: 'attack',
      name: id,
      anchor: { x, y },
    },
    position: { x, y },
    z: 0,
    speed: 0,
    action,
  };
}

function ball(x: number, y: number): FrameEntity {
  return {
    entity: {
      id: 'ball',
      isBall: true,
      side: null,
      participantId: null,
      shirtNumber: 0,
      family: null,
      name: null,
      anchor: { x, y },
    },
    position: { x, y },
    z: 0,
    speed: 0,
    action: null,
  };
}

function highlight(overrides: Partial<Passage> = {}): Passage {
  return {
    sourceEventSequence: 7,
    minute: 36,
    stoppageMinute: 0,
    startMatchSecond: 2_160,
    endMatchSecond: 2_220,
    durationMilliseconds: 20_000,
    outcomeCode: 'goal',
    narration: 'Goal.',
    homeColour: '#1f4e79',
    awayColour: '#8c2f39',
    eventSequences: [7],
    entities: [],
    tracks: [],
    commentary: null,
    ...overrides,
  };
}

describe('action tags', () => {
  it('knows a strike from a run and a pass', () => {
    expect(isStrikeAction('shot')).toBe(true);
    expect(isStrikeAction('penalty')).toBe(true);
    expect(isStrikeAction('free_kick')).toBe(true);
    expect(isStrikeAction('run')).toBe(false);
    expect(isStrikeAction(null)).toBe(false);
    expect(isStrikeAction(undefined)).toBe(false);
  });

  it('knows a duel tag', () => {
    expect(isTackleAction('tackle')).toBe(true);
    expect(isTackleAction('duel')).toBe(true);
    expect(isTackleAction('interception')).toBe(true);
    expect(isTackleAction('pass')).toBe(false);
  });

  it('knows the ball actions that earn a trail and the keeper actions that dive', () => {
    expect(isTrailAction('shot')).toBe(true);
    expect(isTrailAction('cross')).toBe(true);
    expect(isTrailAction('pass')).toBe(false);
    expect(isDiveAction('save')).toBe(true);
    expect(isDiveAction('dive')).toBe(true);
    expect(isDiveAction('run')).toBe(false);
  });
});

describe('wantsTrail', () => {
  it('leaves a short ground pass with no trail, whatever its speed', () => {
    expect(wantsTrail('pass', 0, 4_000)).toBe(false);
  });

  it('streaks a struck ball and a crossed ball', () => {
    expect(wantsTrail('shot', 0, 900)).toBe(true);
    expect(wantsTrail('cross', 70, 300)).toBe(true);
  });

  it('streaks a genuinely airborne ball even without a tag', () => {
    expect(wantsTrail(null, 60, 200)).toBe(true);
    expect(wantsTrail(null, 4, 200)).toBe(false);
  });
});

describe('duelClashes', () => {
  it('marks an opposing pair contesting the ball, between them', () => {
    const frame = [
      player('H9', 'home', 5_000, 5_000),
      player('A5', 'away', 5_200, 5_000),
      ball(5_100, 5_000),
    ];

    const clashes = duelClashes(frame);

    expect(clashes).toHaveLength(1);
    expect(clashes[0].x).toBeCloseTo(5_100, 5);
    expect(clashes[0].y).toBeCloseTo(5_000, 5);
  });

  it('ignores two team-mates standing together', () => {
    const frame = [
      player('H9', 'home', 5_000, 5_000),
      player('H10', 'home', 5_100, 5_000),
      ball(5_050, 5_000),
    ];

    expect(duelClashes(frame)).toHaveLength(0);
  });

  it('ignores a contest away from the ball', () => {
    const frame = [
      player('H9', 'home', 1_000, 1_000),
      player('A5', 'away', 1_200, 1_000),
      ball(9_000, 9_000),
    ];

    expect(duelClashes(frame)).toHaveLength(0);
  });

  it('draws a tagged duel even when the players are a stride apart', () => {
    const frame = [
      player('H9', 'home', 5_000, 5_000, 'tackle'),
      player('A5', 'away', 5_900, 5_000),
      ball(5_500, 5_000),
    ];

    expect(duelClashes(frame)).toHaveLength(1);
  });
});

describe('nearestPlayerToBall', () => {
  it('picks the closest player when the ball is at one', () => {
    const frame = [
      player('H9', 'home', 5_300, 5_000),
      player('A4', 'away', 5_100, 5_000),
      player('H7', 'home', 9_000, 9_000),
      ball(5_000, 5_000),
    ];

    expect(nearestPlayerToBall(frame)?.entity.id).toBe('A4');
  });

  it('shows nobody when the ball is alone', () => {
    const frame = [player('H9', 'home', 1_000, 1_000), ball(5_000, 5_000)];

    expect(nearestPlayerToBall(frame)).toBeNull();
  });

  it('shows nobody when there is no ball', () => {
    expect(nearestPlayerToBall([player('H9', 'home', 1_000, 1_000)])).toBeNull();
  });
});

describe('trailStrength', () => {
  it('is nothing for a ball rolling to a stop', () => {
    expect(trailStrength(0, 200)).toBeLessThan(0.15);
  });

  it('reads a struck ball as a streak', () => {
    expect(trailStrength(0, 3_000)).toBeCloseTo(1, 5);
  });

  it('counts altitude too, so a lofted cross is not mistaken for a slow pass', () => {
    expect(trailStrength(60, 0)).toBeCloseTo(1, 5);
  });
});

describe('celebrationStartMilliseconds', () => {
  it('is nothing for a chance that was not a goal', () => {
    expect(celebrationStartMilliseconds(highlight({ outcomeCode: 'saved' }))).toBeNull();
  });

  it('starts at the beat the synchronized commentary reports the goal', () => {
    const start = celebrationStartMilliseconds(
      highlight({
        commentary: [
          {
            timeMilliseconds: 0,
            templateKey: 'match.passage.build_up',
            variantKey: 'match.passage.build_up.v1',
            parameters: [],
            text: 'Build-up.',
          },
          {
            timeMilliseconds: 14_600,
            templateKey: 'match.goal',
            variantKey: 'match.goal.v1',
            parameters: [],
            text: 'Goal!',
          },
        ],
      }),
    );

    expect(start).toBe(14_600);
  });

  it('falls back to the back end of the passage when there is no synchronized commentary', () => {
    expect(celebrationStartMilliseconds(highlight({ commentary: null }))).toBe(14_000);
  });
});
