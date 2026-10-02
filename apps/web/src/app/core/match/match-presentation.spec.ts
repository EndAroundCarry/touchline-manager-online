import {
  cardKindFor,
  commentarySideLabel,
  conditionColorClass,
  highlightIndexForLine,
  highlightTitle,
  isGoalCommentary,
  isGoalOutcome,
  isShotOutcome,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  possessionPercent,
  scoreLine,
  shotMapEntries,
} from './match-presentation';
import { CommentaryLine, Highlight, HighlightKeyframe, MatchStatistics } from './match.models';

/** The match center's pure formatting guarantees. */

function statistics(overrides: Partial<MatchStatistics> = {}): MatchStatistics {
  return {
    possessionBasisPoints: 5_000,
    goals: 0,
    shots: 0,
    shotsOnTarget: 0,
    shotsOffTarget: 0,
    shotsBlocked: 0,
    woodworkHits: 0,
    saves: 0,
    corners: 0,
    offsides: 0,
    fouls: 0,
    yellowCards: 0,
    redCards: 0,
    penaltiesAwarded: 0,
    penaltiesScored: 0,
    injuries: 0,
    substitutions: 0,
    ...overrides,
  };
}

function highlight(sequence: number): Highlight {
  return {
    sourceEventSequence: sequence,
    minute: 67,
    stoppageMinute: 0,
    durationMilliseconds: 8_000,
    outcomeCode: 'goal',
    narration: 'Goal',
    homeColour: '#1f4e79',
    awayColour: '#8c2f39',
    entities: [],
    tracks: [],
  };
}

function shot(overrides: {
  sequence: number;
  outcomeCode: string;
  side?: 'home' | 'away';
  action?: string;
  ball?: HighlightKeyframe;
}): Highlight {
  const side = overrides.side ?? 'home';

  return {
    ...highlight(overrides.sequence),
    outcomeCode: overrides.outcomeCode,
    entities: [
      {
        entityId: 'H9',
        isBall: false,
        side,
        participantId: 'p1',
        shirtNumber: 9,
        family: 'attack',
        x: 7_000,
        y: 3_500,
      },
      {
        entityId: 'ball',
        isBall: true,
        side: null,
        participantId: null,
        shirtNumber: 0,
        family: null,
        x: 7_000,
        y: 3_500,
      },
    ],
    tracks: [
      {
        entityId: 'H9',
        keyframes: [
          { timeMilliseconds: 0, x: 6_000, y: 3_000 },
          {
            timeMilliseconds: 5_000,
            x: 7_400,
            y: 3_600,
            action: overrides.action ?? 'shot',
          },
        ],
      },
      {
        entityId: 'ball',
        keyframes: [
          { timeMilliseconds: 0, x: 5_000, y: 3_500 },
          overrides.ball ?? { timeMilliseconds: 8_000, x: 7_800, y: 3_600 },
        ],
      },
    ],
  };
}

function line(sequence: number): CommentaryLine {
  return {
    sequence,
    minute: 67,
    stoppageMinute: 0,
    side: 'home',
    templateKey: 'match.goal',
    variantKey: 'match.goal.v1',
    parameters: [],
    text: 'Goal!',
  };
}

describe('match presentation helpers', () => {
  it('writes a match minute, with stoppage time kept separate from the minute', () => {
    expect(matchClockLabel(67, 0)).toBe("67'");
    expect(matchClockLabel(90, 3)).toBe("90+3'");
  });

  it('names an outcome and falls back to the code for one it does not know', () => {
    expect(outcomeLabel('goal')).toBe('Goal');
    expect(outcomeLabel('new_outcome')).toBe('new_outcome');
  });

  it('treats a goal and a penalty goal as goals', () => {
    expect(isGoalOutcome('goal')).toBe(true);
    expect(isGoalOutcome('penalty_goal')).toBe(true);
    expect(isGoalOutcome('saved')).toBe(false);
  });

  it('formats a scoreline and a possession share', () => {
    expect(scoreLine(2, 1)).toBe('2\u20131');
    expect(possessionPercent(5_400)).toBe('54%');
  });

  it('names which end a line belongs to', () => {
    expect(commentarySideLabel('home')).toBe('Home');
    expect(commentarySideLabel('away')).toBe('Away');
  });

  it('titles a highlight with its outcome and clock', () => {
    expect(highlightTitle(highlight(1))).toBe("Goal \u2014 67'");
  });

  it('builds the statistics panel with both sides on one row each', () => {
    const rows = matchStatisticRows(
      statistics({ goals: 2, shots: 9, possessionBasisPoints: 6_000 }),
      statistics({ goals: 1, shots: 4, possessionBasisPoints: 4_000 }),
    );

    expect(rows).toHaveLength(17);
    expect(rows[0]).toEqual({ label: 'Goals', home: '2', away: '1' });
    expect(rows[1]).toEqual({ label: 'Possession', home: '60%', away: '40%' });
    expect(rows[2]).toEqual({ label: 'Shots', home: '9', away: '4' });
  });

  it('finds the highlight a commentary line can be shown as, or -1', () => {
    expect(highlightIndexForLine([highlight(4), highlight(9)], line(9))).toBe(1);
    expect(highlightIndexForLine([highlight(4)], line(5))).toBe(-1);
  });

  it('knows the commentary templates that report a goal', () => {
    expect(isGoalCommentary('match.goal')).toBe(true);
    expect(isGoalCommentary('match.penalty.goal')).toBe(true);
    expect(isGoalCommentary('match.shot.saved')).toBe(false);
  });

  it('colours a condition bar green through red as a player tires', () => {
    expect(conditionColorClass(9_500)).toBe('bg-emerald-500');
    expect(conditionColorClass(6_000)).toBe('bg-lime-500');
    expect(conditionColorClass(4_500)).toBe('bg-amber-500');
    expect(conditionColorClass(1_500)).toBe('bg-rose-500');
  });

  it('plots a shot where its own track says the ball was struck from', () => {
    const entries = shotMapEntries([
      shot({ sequence: 3, outcomeCode: 'saved' }),
      shot({ sequence: 4, outcomeCode: 'goal', side: 'away', action: 'penalty' }),
      highlight(5),
    ]);

    expect(entries).toHaveLength(2);
    expect(entries[0]).toMatchObject({ sourceEventSequence: 3, side: 'home', x: 7_400, y: 3_600 });
    expect(entries[1]).toMatchObject({ sourceEventSequence: 4, side: 'away', x: 7_400, y: 3_600 });
  });

  it('plots a tagless vintage shot where the ball came to rest, on the half it finished in', () => {
    const entries = shotMapEntries([shot({ sequence: 3, outcomeCode: 'blocked', action: 'run' })]);

    expect(entries).toHaveLength(1);
    expect(entries[0]).toMatchObject({ side: 'home', x: 7_800, y: 3_600 });
  });

  it('plots nothing for a highlight that is not a shot at all', () => {
    expect(isShotOutcome('goal')).toBe(true);
    expect(isShotOutcome('bridge')).toBe(false);
    expect(shotMapEntries([{ ...highlight(1), outcomeCode: 'bridge' }])).toHaveLength(0);
  });

  it('reads a booking from the line that reports it, second yellow included', () => {
    expect(cardKindFor('match.card.yellow')).toBe('yellow');
    expect(cardKindFor('match.card.second_yellow')).toBe('red');
    expect(cardKindFor('match.card.red')).toBe('red');
    expect(cardKindFor('match.foul')).toBeNull();
  });
});
