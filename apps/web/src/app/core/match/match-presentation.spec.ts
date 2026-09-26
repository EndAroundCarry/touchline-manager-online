import {
  commentarySideLabel,
  highlightIndexForLine,
  highlightTitle,
  isGoalOutcome,
  matchClockLabel,
  matchStatisticRows,
  outcomeLabel,
  possessionPercent,
  scoreLine,
} from './match-presentation';
import { CommentaryLine, Highlight, MatchStatistics } from './match.models';

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
});
