import { BALL_ENTITY_ID, SLOT_IDS, buildFilmTimeline } from './film-timeline';
import {
  HighlightCommentary,
  HighlightEntity,
  HighlightKeyframe,
  MatchPresentation,
  Passage,
  PassageCut,
} from './match.models';

/**
 * The film timeline's guarantees (`replay-v4`).
 *
 * The server sends a film as passages of about ten seconds; the timeline is what makes them one match. These
 * tests build small films from a few passages and ask about film moments across the seams — a boundary frame
 * kept once, a cut kept as a step, a substitution on the same token, a player sent off, the clock through
 * both halves — because those seams are exactly where a viewer that rebuilt itself per passage used to flicker.
 */

function keyframe(
  timeMilliseconds: number,
  x: number,
  y: number,
  extra: Partial<HighlightKeyframe> = {},
): HighlightKeyframe {
  return { timeMilliseconds, x, y, ...extra };
}

function player(
  entityId: string,
  participantId: string,
  shirtNumber: number,
  overrides: Partial<HighlightEntity> = {},
): HighlightEntity {
  return {
    entityId,
    isBall: false,
    side: entityId.startsWith('H') ? 'home' : 'away',
    participantId,
    shirtNumber,
    family: 'attack',
    x: 5_000,
    y: 5_000,
    name: `Player ${shirtNumber}`,
    position: 'ST',
    ...overrides,
  };
}

const BALL: HighlightEntity = {
  entityId: BALL_ENTITY_ID,
  isBall: true,
  side: null,
  participantId: null,
  shirtNumber: 0,
  family: null,
  x: 5_000,
  y: 5_000,
};

interface PassageSpec {
  readonly duration?: number;
  readonly period?: number;
  readonly clock?: readonly { timeMilliseconds: number; matchSecond: number }[];
  readonly outcome?: string;
  readonly occupants?: readonly HighlightEntity[];
  readonly tracks?: Record<string, readonly HighlightKeyframe[]>;
  readonly commentary?: readonly HighlightCommentary[];
  readonly cuts?: readonly PassageCut[];
  readonly sequences?: readonly number[];
  readonly source?: number;
  readonly legacyStart?: number;
}

function passage(spec: PassageSpec = {}): Passage {
  const duration = spec.duration ?? 4_000;
  const occupants = spec.occupants ?? [player('H9', 'p9', 9)];

  return {
    sourceEventSequence: spec.source ?? 0,
    minute: 10,
    stoppageMinute: 0,
    startMatchSecond: spec.legacyStart ?? 0,
    endMatchSecond: (spec.legacyStart ?? 0) + 60,
    durationMilliseconds: duration,
    outcomeCode: spec.outcome ?? 'play',
    narration: 'Play.',
    homeColour: '#1f6f43',
    awayColour: '#7a1f2b',
    eventSequences: spec.sequences ?? [],
    entities: [...occupants, BALL],
    tracks: Object.entries(spec.tracks ?? {}).map(([entityId, keyframes]) => ({
      entityId,
      keyframes,
    })),
    commentary: spec.commentary ?? [],
    ...(spec.period === undefined ? {} : { period: spec.period }),
    ...(spec.clock === undefined ? {} : { clock: spec.clock }),
    ...(spec.cuts === undefined ? {} : { cuts: spec.cuts }),
  };
}

function presentation(
  passages: readonly Passage[],
  overrides: Partial<MatchPresentation> = {},
): MatchPresentation {
  return {
    matchId: 'm1',
    presentationVersion: 'replay-v4',
    engineVersion: 'engine-v5',
    homeGoals: 0,
    awayGoals: 0,
    commentary: [],
    passages,
    reel: [],
    estimatedPayloadBytes: 0,
    homeLineup: {
      clubName: 'Home',
      shortName: 'HOM',
      primaryColour: '#1f6f43',
      secondaryColour: '#ffffff',
      formation: '4-4-2',
      starters: [lineupPlayer('p9', 'pl9', 9), lineupPlayer('p19', 'pl19', 19)],
      bench: [],
    },
    awayLineup: {
      clubName: 'Away',
      shortName: 'AWY',
      primaryColour: '#7a1f2b',
      secondaryColour: '#ffffff',
      formation: '4-4-2',
      starters: [lineupPlayer('q5', 'ql5', 5)],
      bench: [],
    },
    ...overrides,
  };
}

function lineupPlayer(participantId: string, playerId: string, shirtNumber: number) {
  return {
    participantId,
    playerId,
    shirtNumber,
    name: `Player ${shirtNumber}`,
    position: 'ST',
    family: 'attack',
    isStarter: true,
    slotNumber: shirtNumber,
    kickoffCondition: 10_000,
    finalCondition: 8_000,
    finalRating: 6_500,
    goals: 0,
    assists: 0,
    yellowCards: 0,
    sentOff: false,
    subbedOutMinute: null,
    subbedInMinute: null,
    isInjured: false,
  };
}

function slotOf(id: string): number {
  return SLOT_IDS.indexOf(id);
}

describe('buildFilmTimeline: the continuous tracks', () => {
  it('lays the passages end to end and keeps the boundary frame once', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          duration: 4_000,
          tracks: { H9: [keyframe(0, 1_000, 1_000), keyframe(4_000, 2_000, 1_000)] },
        }),
        passage({
          duration: 3_000,
          tracks: { H9: [keyframe(0, 2_000, 1_000), keyframe(3_000, 3_000, 1_000)] },
        }),
      ]),
    );
    const track = timeline.slots[slotOf('H9')].track;

    expect(timeline.durationMilliseconds).toBe(7_000);
    expect(timeline.passageStarts).toEqual([0, 4_000]);
    expect([...track.times]).toEqual([0, 4_000, 7_000]);
    expect([...track.xs]).toEqual([1_000, 2_000, 3_000]);
  });

  it('keeps both frames at a cut, as a step at one instant', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          duration: 4_000,
          tracks: { H9: [keyframe(0, 1_000, 1_000), keyframe(4_000, 2_000, 1_000)] },
        }),
        passage({
          duration: 3_000,
          // The film cut here: the second passage opens on the kick-off spot, not where the first ended.
          tracks: { H9: [keyframe(0, 5_000, 5_000), keyframe(3_000, 5_400, 5_000)] },
          cuts: [{ timeMilliseconds: 0, durationMilliseconds: 300, kind: 'kick_off' }],
        }),
      ]),
    );
    const track = timeline.slots[slotOf('H9')].track;

    expect([...track.times]).toEqual([0, 4_000, 4_000, 7_000]);
    expect([...track.xs]).toEqual([1_000, 2_000, 5_000, 5_400]);
    expect(timeline.cuts).toEqual([
      { startMilliseconds: 4_000, durationMilliseconds: 300, kind: 'kick_off' },
    ]);
  });

  it('takes the speed and the action from the later of two keyframes that are the same state', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ tracks: { H9: [keyframe(0, 100, 100), keyframe(4_000, 200, 100)] } }),
        passage({
          tracks: {
            H9: [keyframe(0, 200, 100, { speed: 800, action: 'pass' }), keyframe(4_000, 300, 100)],
          },
        }),
      ]),
    );
    const track = timeline.slots[slotOf('H9')].track;

    expect(track.length).toBe(3);
    expect(track.speeds[1]).toBe(800);
    expect(track.actions[1]).toBe('pass');
  });

  it('holds an entity at its anchor through a passage that sends it no track', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ duration: 2_000, occupants: [player('H9', 'p9', 9, { x: 3_000, y: 4_000 })] }),
      ]),
    );
    const track = timeline.slots[slotOf('H9')].track;

    expect([...track.times]).toEqual([0, 2_000]);
    expect([...track.xs]).toEqual([3_000, 3_000]);
    expect([...track.ys]).toEqual([4_000, 4_000]);
  });

  it('puts the ball on its own track', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          tracks: { ball: [keyframe(0, 100, 100, { z: 0 }), keyframe(4_000, 900, 100, { z: 40 })] },
        }),
      ]),
    );

    expect(timeline.ball.entityId).toBe(BALL_ENTITY_ID);
    expect([...timeline.ball.zs]).toEqual([0, 40]);
    expect(timeline.ballEntity.isBall).toBe(true);
  });

  it('is empty, and says so, for a presentation with no passages', () => {
    expect(buildFilmTimeline(null).isEmpty).toBe(true);
    expect(buildFilmTimeline(presentation([])).isEmpty).toBe(true);
  });
});

describe('buildFilmTimeline: the roster', () => {
  it('keeps one stint for a player who stays in their slot across passages', () => {
    const timeline = buildFilmTimeline(presentation([passage(), passage()]));
    const stints = timeline.slots[slotOf('H9')].stints;

    expect(stints).toHaveLength(1);
    expect(stints[0].startMilliseconds).toBe(0);
    expect(stints[0].endMilliseconds).toBe(8_000);
  });

  it('switches the name and number on the same slot when a substitute comes on', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ occupants: [player('H9', 'p9', 9)] }),
        passage({ occupants: [player('H9', 'p19', 19)] }),
      ]),
    );
    const slot = slotOf('H9');

    expect(timeline.stintAt(slot, 3_999)?.entity.shirtNumber).toBe(9);
    expect(timeline.stintAt(slot, 3_999)?.entity.name).toBe('Player 9');
    expect(timeline.stintAt(slot, 4_000)?.entity.shirtNumber).toBe(19);
    expect(timeline.stintAt(slot, 4_000)?.entity.participantId).toBe('p19');
  });

  it('stops drawing a player who has been sent off, and nobody replaces them', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ occupants: [player('H9', 'p9', 9), player('A5', 'q5', 5)] }),
        passage({ occupants: [player('A5', 'q5', 5)] }),
      ]),
    );

    expect(timeline.stintAt(slotOf('H9'), 1_000)).not.toBeNull();
    expect(timeline.stintAt(slotOf('H9'), 5_000)).toBeNull();
    expect(timeline.stintAt(slotOf('A5'), 5_000)).not.toBeNull();
  });

  it('draws the slots in the order the server sorts its entities', () => {
    expect(SLOT_IDS).toHaveLength(22);
    expect(SLOT_IDS[0]).toBe('A1');
    expect(SLOT_IDS[1]).toBe('A10');
    expect(SLOT_IDS[SLOT_IDS.length - 1]).toBe('H9');
  });

  it("carries the sides' colours for a film whose lineups name no kit", () => {
    const timeline = buildFilmTimeline(presentation([passage()]));

    expect(timeline.homeColour).toBe('#1f6f43');
    expect(timeline.awayColour).toBe('#7a1f2b');
  });
});

describe('buildFilmTimeline: the clock', () => {
  /** A film through the first half's stoppage, the interval, the second half's start, and its stoppage. */
  function twoHalves() {
    return buildFilmTimeline(
      presentation([
        passage({
          duration: 4_000,
          period: 1,
          clock: [
            { timeMilliseconds: 0, matchSecond: 2_640 },
            { timeMilliseconds: 2_000, matchSecond: 2_700 },
            { timeMilliseconds: 4_000, matchSecond: 2_760 },
          ],
        }),
        passage({
          duration: 3_000,
          period: 1,
          outcome: 'half_time',
          clock: [
            { timeMilliseconds: 0, matchSecond: 2_760 },
            { timeMilliseconds: 3_000, matchSecond: 2_760 },
          ],
        }),
        passage({
          duration: 4_000,
          period: 2,
          clock: [
            { timeMilliseconds: 0, matchSecond: 2_700 },
            { timeMilliseconds: 4_000, matchSecond: 2_760 },
          ],
          cuts: [{ timeMilliseconds: 0, durationMilliseconds: 300, kind: 'half_time' }],
        }),
        passage({
          duration: 2_000,
          period: 2,
          clock: [
            { timeMilliseconds: 0, matchSecond: 5_400 },
            { timeMilliseconds: 2_000, matchSecond: 5_460 },
          ],
        }),
      ]),
    );
  }

  it("reads the first half's regulation minutes and then its stoppage", () => {
    const timeline = twoHalves();

    expect(timeline.clockAt(0).label).toBe("45'");
    expect(timeline.clockAt(0).minute).toBe(45);
    expect(timeline.clockAt(1_000).label).toBe("45'");
    expect(timeline.clockAt(2_000).label).toBe("45+1'");
    expect(timeline.clockAt(2_000).minute).toBe(45);
    expect(timeline.clockAt(2_000).stoppageMinute).toBe(1);
    expect(timeline.clockAt(3_999).label).toBe("45+1'");
  });

  it("reads HT through the half-time card, whatever the first half's clock stood at", () => {
    const timeline = twoHalves();

    expect(timeline.clockAt(4_000).label).toBe('HT');
    expect(timeline.clockAt(4_000).isHalfTime).toBe(true);
    expect(timeline.clockAt(6_999).label).toBe('HT');
    expect(timeline.clockAt(6_999).minute).toBe(45);
  });

  it("starts the second half at 46' and runs its stoppage on from 90", () => {
    const timeline = twoHalves();

    expect(timeline.clockAt(7_000).label).toBe("46'");
    expect(timeline.clockAt(7_000).isHalfTime).toBe(false);
    expect(timeline.clockAt(10_999).label).toBe("46'");
    expect(timeline.clockAt(11_000).label).toBe("90+1'");
    expect(timeline.clockAt(11_000).minute).toBe(90);
    expect(timeline.clockAt(12_999).label).toBe("90+1'");
    expect(timeline.clockAt(13_000).label).toBe("90+2'");
  });

  it('never runs backwards through a half', () => {
    const timeline = twoHalves();

    for (const [from, to] of [
      [0, 4_000],
      [7_000, 13_000],
    ]) {
      let previous = -1;

      for (let moment = from; moment <= to; moment += 50) {
        const second = timeline.clockAt(moment).matchSecond;

        expect(second, `the clock at ${moment} ms`).toBeGreaterThanOrEqual(previous);
        previous = second;
      }
    }
  });

  it("reads the second half from its own clock, so it is not 48' when it begins", () => {
    const timeline = twoHalves();

    expect(timeline.clockAt(7_000).matchSecond).toBe(2_700);
    expect(timeline.clockAt(7_000).minute).toBe(46);
  });

  it('reads a presentation from before the half-aware clock as it always was', () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ duration: 6_000, legacyStart: 480 })]),
    );

    expect(timeline.clockAt(0).label).toBe("9'");
    expect(timeline.clockAt(0).period).toBe(0);
  });

  it('reuses the reading while the second has not changed', () => {
    const timeline = twoHalves();

    expect(timeline.clockAt(100)).toBe(timeline.clockAt(110));
  });

  it('has no clock to read where there is nothing to play', () => {
    expect(buildFilmTimeline(null).clockAt(0).label).toBe('');
  });
});

describe('buildFilmTimeline: the feed', () => {
  function token(timeMilliseconds: number, text: string, templateKey = 'match.build.pass') {
    return {
      timeMilliseconds,
      templateKey,
      variantKey: `${templateKey}.v1`,
      parameters: [{ name: 'playerId', value: 'pl9' }],
      text,
    };
  }

  it('places each row at the film moment its beat happens, in order, with the clock read there', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          duration: 4_000,
          period: 1,
          clock: [
            { timeMilliseconds: 0, matchSecond: 600 },
            { timeMilliseconds: 4_000, matchSecond: 660 },
          ],
          commentary: [token(3_000, 'Second'), token(1_000, 'First')],
        }),
        passage({
          duration: 4_000,
          period: 1,
          clock: [
            { timeMilliseconds: 0, matchSecond: 660 },
            { timeMilliseconds: 4_000, matchSecond: 720 },
          ],
          commentary: [token(500, 'Third')],
        }),
      ]),
    );

    expect(timeline.feed.map((row) => row.text)).toEqual(['First', 'Second', 'Third']);
    expect(timeline.feed.map((row) => row.filmMilliseconds)).toEqual([1_000, 3_000, 4_500]);
    expect(timeline.feed.map((row) => row.clock)).toEqual(["11'", "11'", "12'"]);
  });

  it("labels a first-half stoppage line 45+N' and a second-half opening line 46'", () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          duration: 4_000,
          period: 1,
          clock: [
            { timeMilliseconds: 0, matchSecond: 2_760 },
            { timeMilliseconds: 4_000, matchSecond: 2_820 },
          ],
          commentary: [token(1_000, 'Stoppage')],
        }),
        passage({
          duration: 4_000,
          period: 2,
          clock: [
            { timeMilliseconds: 0, matchSecond: 2_700 },
            { timeMilliseconds: 4_000, matchSecond: 2_760 },
          ],
          commentary: [token(500, 'Restart')],
        }),
      ]),
    );

    expect(timeline.feed.map((row) => row.clock)).toEqual(["45+2'", "46'"]);
  });

  it('knows which side a row belongs to from the facts it was built from', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          commentary: [
            token(0, 'Home'),
            {
              ...token(1_000, 'Away'),
              parameters: [{ name: 'participantId', value: 'q5' }],
            },
            { ...token(2_000, 'Neither'), parameters: [] },
          ],
        }),
      ]),
    );

    expect(timeline.feed.map((row) => row.side)).toEqual(['home', 'away', '']);
  });

  it('flags a goal row and counts how many rows the playhead has reached', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          commentary: [token(1_000, 'Build'), token(2_000, 'Goal!', 'match.goal')],
        }),
      ]),
    );

    expect(timeline.feed.map((row) => row.isGoal)).toEqual([false, true]);
    expect(timeline.feedCountAt(999)).toBe(0);
    expect(timeline.feedCountAt(1_000)).toBe(1);
    expect(timeline.feedCountAt(2_500)).toBe(2);
    expect(timeline.feedCountAt(60_000)).toBe(2);
  });
});

describe('buildFilmTimeline: cards', () => {
  const cardLine = (sequence: number, templateKey: string, playerId: string) => ({
    sequence,
    minute: 10,
    stoppageMinute: 0,
    side: 'home',
    templateKey,
    variantKey: `${templateKey}.v1`,
    parameters: [{ name: 'playerId', value: playerId }],
    text: 'A booking.',
  });

  it('gives a player their card from the moment the beat that narrates it', () => {
    const timeline = buildFilmTimeline(
      presentation(
        [
          passage({ duration: 4_000 }),
          passage({
            duration: 4_000,
            sequences: [7],
            commentary: [
              {
                timeMilliseconds: 1_500,
                templateKey: 'match.card.yellow',
                variantKey: 'v1',
                parameters: [{ name: 'playerId', value: 'pl9' }],
                text: 'Booked.',
              },
            ],
          }),
        ],
        { commentary: [cardLine(7, 'match.card.yellow', 'pl9')] },
      ),
    );

    expect(timeline.cardMoments).toEqual([
      { filmMilliseconds: 5_500, participantId: 'p9', kind: 'yellow' },
    ]);
    expect(timeline.cardsAt(5_499).has('p9')).toBe(false);
    expect(timeline.cardsAt(5_500).get('p9')).toBe('yellow');
    expect(timeline.cardsAt(60_000).get('p9')).toBe('yellow');
  });

  it('shows a card from the start of its passage when no line is pinned to a moment', () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ duration: 4_000 }), passage({ duration: 4_000, sequences: [7] })], {
        commentary: [cardLine(7, 'match.card.red', 'pl9')],
      }),
    );

    expect(timeline.cardMoments).toEqual([
      { filmMilliseconds: 4_000, participantId: 'p9', kind: 'red' },
    ]);
  });

  it('upgrades a yellow to a red when a second yellow is shown', () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ sequences: [3] }), passage({ sequences: [9] })], {
        commentary: [
          cardLine(3, 'match.card.yellow', 'pl9'),
          cardLine(9, 'match.card.second_yellow', 'pl9'),
        ],
      }),
    );

    expect(timeline.cardsAt(1_000).get('p9')).toBe('yellow');
    expect(timeline.cardsAt(5_000).get('p9')).toBe('red');
  });

  it('reads a commentary identity as the participant too, which is how the engine names a player', () => {
    // The engine's commentary names a participant; a presentation whose participants are not the underlying
    // players' identities must still put the card on the right token.
    const timeline = buildFilmTimeline(
      presentation([passage({ sequences: [7] })], {
        commentary: [cardLine(7, 'match.card.yellow', 'p9')],
      }),
    );

    expect(timeline.cardMoments).toEqual([
      { filmMilliseconds: 0, participantId: 'p9', kind: 'yellow' },
    ]);
  });

  it('shows no card for a player the lineups do not know', () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ sequences: [7] })], {
        commentary: [cardLine(7, 'match.card.yellow', 'nobody')],
      }),
    );

    expect(timeline.cardMoments).toEqual([]);
    expect(timeline.cardsAt(1_000).size).toBe(0);
  });
});

describe('buildFilmTimeline: goals, shots and the interval', () => {
  it('marks a shot where the ball is struck, and remembers where its passage began', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ duration: 4_000 }),
        passage({
          duration: 6_000,
          outcome: 'saved',
          source: 11,
          tracks: {
            H9: [keyframe(0, 7_000, 4_000), keyframe(2_500, 7_800, 4_100, { action: 'shot' })],
          },
        }),
      ]),
    );

    expect(timeline.markers).toHaveLength(1);
    expect(timeline.markers[0].kind).toBe('shot');
    expect(timeline.markers[0].filmMilliseconds).toBe(6_500);
    expect(timeline.markers[0].passageStartMilliseconds).toBe(4_000);
    expect(timeline.markers[0].passageIndex).toBe(1);
  });

  it('marks a goal, and celebrates it for four seconds from the line that narrates it', () => {
    const timeline = buildFilmTimeline(
      presentation(
        [
          passage({
            duration: 6_000,
            outcome: 'goal',
            source: 5,
            commentary: [
              {
                timeMilliseconds: 3_000,
                templateKey: 'match.goal',
                variantKey: 'v1',
                parameters: [],
                text: 'Goal!',
              },
            ],
          }),
        ],
        {
          commentary: [
            {
              sequence: 5,
              minute: 8,
              stoppageMinute: 0,
              side: 'home',
              templateKey: 'match.goal',
              variantKey: 'v1',
              parameters: [],
              text: 'Goal.',
            },
          ],
        },
      ),
    );

    expect(timeline.markers[0].kind).toBe('goal');
    expect(timeline.goals).toEqual([{ filmMilliseconds: 3_000, side: 'home', sequence: 5 }]);
    expect(timeline.celebrationAt(2_999)).toBe(-1);
    expect(timeline.celebrationAt(3_000)).toBe(0);
    expect(timeline.celebrationAt(6_000)).toBe(3_000);
    expect(timeline.celebrationAt(7_000)).toBe(-1);
    expect(timeline.activeGoalAt(4_000)?.side).toBe('home');
    expect(timeline.activeGoalAt(8_000)).toBeNull();
  });

  it('counts the score at a film moment, so a scoreboard follows the film instead of showing the final tally', () => {
    const timeline = buildFilmTimeline(
      presentation(
        [
          passage({
            duration: 6_000,
            outcome: 'goal',
            source: 5,
            commentary: [
              {
                timeMilliseconds: 3_000,
                templateKey: 'match.goal',
                variantKey: 'v1',
                parameters: [],
                text: 'Goal!',
              },
            ],
          }),
        ],
        {
          commentary: [
            {
              sequence: 5,
              minute: 8,
              stoppageMinute: 0,
              side: 'home',
              templateKey: 'match.goal',
              variantKey: 'v1',
              parameters: [],
              text: 'Goal.',
            },
          ],
        },
      ),
    );

    expect(timeline.scoreAt(0)).toEqual({ home: 0, away: 0 });
    expect(timeline.scoreAt(2_999)).toEqual({ home: 0, away: 0 });
    expect(timeline.scoreAt(3_000)).toEqual({ home: 1, away: 0 });
    expect(timeline.scoreAt(1_000_000)).toEqual({ home: 1, away: 0 });
  });

  it('has no score for an empty film', () => {
    expect(buildFilmTimeline(null).scoreAt(5_000)).toEqual({ home: 0, away: 0 });
  });

  it('falls back to most of the way through the passage when no line narrates the goal', () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ duration: 20_000, outcome: 'goal', source: 5, commentary: [] })]),
    );

    expect(timeline.goals[0].filmMilliseconds).toBe(14_000);
  });

  it('runs a celebration out even when the passage that scored ends inside it', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({
          duration: 3_000,
          outcome: 'goal',
          source: 5,
          commentary: [
            {
              timeMilliseconds: 2_500,
              templateKey: 'match.goal',
              variantKey: 'v1',
              parameters: [],
              text: 'Goal!',
            },
          ],
        }),
        passage({ duration: 5_000 }),
      ]),
    );

    expect(timeline.celebrationAt(5_000)).toBe(2_500);
  });

  it('knows where the half-time card is held', () => {
    const timeline = buildFilmTimeline(
      presentation([
        passage({ duration: 4_000 }),
        passage({ duration: 3_000, outcome: 'half_time' }),
        passage({ duration: 4_000 }),
      ]),
    );

    expect(timeline.halfTimes).toEqual([{ startMilliseconds: 4_000, endMilliseconds: 7_000 }]);
    expect(timeline.isHalfTimeAt(3_999)).toBe(false);
    expect(timeline.isHalfTimeAt(4_000)).toBe(true);
    expect(timeline.isHalfTimeAt(7_000)).toBe(false);
  });

  it("uses the presentation's own schedule for where each passage starts", () => {
    const timeline = buildFilmTimeline(
      presentation([passage({ duration: 4_000 }), passage({ duration: 3_000 })], {
        playback: [
          {
            kind: 'passage',
            sourceEventSequence: 0,
            startMilliseconds: 500,
            durationMilliseconds: 4_000,
          },
          {
            kind: 'passage',
            sourceEventSequence: 0,
            startMilliseconds: 4_500,
            durationMilliseconds: 3_000,
          },
        ],
      }),
    );

    expect(timeline.passageStarts).toEqual([500, 4_500]);
    expect(timeline.durationMilliseconds).toBe(7_500);
  });
});
