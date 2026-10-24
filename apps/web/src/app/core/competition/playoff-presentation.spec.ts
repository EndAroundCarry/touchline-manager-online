import type { PlayoffResult } from './competition.models';
import {
  kickOutcomeLabel,
  kicksByRound,
  playoffSummary,
  roundLabel,
} from './competition-presentation';

/**
 * The words the screens use for a promotion play-off match (`PR-4`, `PR-6`).
 *
 * Pure functions, so they are tested without a component: a stage stands in for a round number, and the shoot-out
 * is read winner first whichever side was at home.
 */
describe('the promotion play-off', () => {
  const home = { clubId: 'c1', name: 'Ashvale United' };
  const away = { clubId: 'c2', name: 'Bramford Rovers' };

  function result(overrides: Partial<PlayoffResult>): PlayoffResult {
    return {
      stage: 'final',
      stageLabel: 'Play-off final',
      decidedBy: 'score',
      winnerClubId: 'c1',
      shootoutHomeScore: null,
      shootoutAwayScore: null,
      firstSide: null,
      kicks: [],
      ...overrides,
    };
  }

  it('labels a match by its stage when it has one, and by its round otherwise', () => {
    expect(roundLabel(14)).toBe('Round 14');
    expect(roundLabel(14, null)).toBe('Round 14');
    expect(roundLabel(32, 'Play-off final')).toBe('Play-off final');
  });

  it('says who won a tie decided on the score', () => {
    expect(playoffSummary(result({}), home, away)).toBe('Ashvale United won the tie');
  });

  it('reads a shoot-out score winner first whichever side was at home', () => {
    const homeWon = result({
      decidedBy: 'penalties',
      winnerClubId: 'c1',
      shootoutHomeScore: 5,
      shootoutAwayScore: 4,
    });
    const awayWon = result({
      decidedBy: 'penalties',
      winnerClubId: 'c2',
      shootoutHomeScore: 3,
      shootoutAwayScore: 4,
    });

    expect(playoffSummary(homeWon, home, away)).toBe('Ashvale United won 5–4 on penalties');
    expect(playoffSummary(awayWon, home, away)).toBe('Bramford Rovers won 4–3 on penalties');
  });

  it('says nothing for a league match or a tie that is not decided', () => {
    expect(playoffSummary(null, home, away)).toBeNull();
    expect(playoffSummary(result({ winnerClubId: null, decidedBy: null }), home, away)).toBeNull();
  });

  it('names how a kick ended, falling back to the code', () => {
    expect(kickOutcomeLabel('scored')).toBe('Scored');
    expect(kickOutcomeLabel('saved')).toBe('Saved');
    expect(kickOutcomeLabel('wide')).toBe('Missed');
    expect(kickOutcomeLabel('hit_post')).toBe('hit_post');
  });

  it('lays the kicks out a round at a time, with an empty slot for a side that did not kick', () => {
    const kick = (number: number, round: number, side: string) => ({
      number,
      round,
      side,
      takerName: `Taker ${number}`,
      goalkeeperName: null,
      outcome: 'scored',
    });

    const rounds = kicksByRound([kick(1, 1, 'home'), kick(2, 1, 'away'), kick(3, 2, 'home')]);

    expect(rounds.map((row) => row.round)).toEqual([1, 2]);
    expect(rounds[0].home?.takerName).toBe('Taker 1');
    expect(rounds[0].away?.takerName).toBe('Taker 2');
    expect(rounds[1].away).toBeNull();
    expect(kicksByRound([])).toEqual([]);
  });
});
