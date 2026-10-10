import {
  BLAME_MILLISECONDS,
  blameStrength,
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
    },
    position: { x, y },
    z: 0,
    speed: 0,
    action: null,
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

describe('blameStrength', () => {
  it('is full at the moment of the error and for a moment after, so it can be seen', () => {
    expect(blameStrength(0)).toBe(1);
    expect(blameStrength(BLAME_MILLISECONDS * 0.25)).toBe(1);
  });

  it('fades to nothing over the rest of the window', () => {
    expect(blameStrength(BLAME_MILLISECONDS * 0.625)).toBeCloseTo(0.5, 5);
    expect(blameStrength(BLAME_MILLISECONDS - 1)).toBeGreaterThan(0);
    expect(blameStrength(BLAME_MILLISECONDS - 1)).toBeLessThan(0.01);
    expect(blameStrength(BLAME_MILLISECONDS)).toBe(0);
  });

  it('is nothing before the error, so a seek never shows a mistake that has not happened', () => {
    expect(blameStrength(-1)).toBe(0);
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
