import {
  EVERYTHING_HAS_HAPPENED,
  NOTHING_HAS_HAPPENED,
  hasHappened,
  playerEventMinutes,
} from './match-lineup-events';
import { LineupIndex } from './match-lineups';
import { CommentaryLine } from './match.models';

/**
 * When the lineup panels may show a card, an injury or a change.
 *
 * The panels fill in as the replay plays, so the events have to be dated: a booking at 30' must not be on
 * a player's row at 1'.
 */

const lineups = {
  participantByPlayerId: new Map([
    ['pl1', 'p1'],
    ['pl2', 'p2'],
  ]),
} as unknown as LineupIndex;

function line(templateKey: string, playerId: string, minute: number): CommentaryLine {
  return {
    sequence: minute,
    minute,
    stoppageMinute: 0,
    side: 'home',
    templateKey,
    variantKey: 'a',
    parameters: [{ name: 'playerId', value: playerId }],
    text: '',
  };
}

describe('playerEventMinutes', () => {
  it('dates a booking, a sending off and an injury to the participant', () => {
    const events = playerEventMinutes(
      [
        line('match.card.yellow', 'pl1', 30),
        line('match.card.second_yellow', 'pl1', 70),
        line('match.injury', 'pl2', 55),
      ],
      lineups,
    );

    expect(events.get('p1')).toEqual({ yellow: 30, red: 70, injury: null });
    expect(events.get('p2')).toEqual({ yellow: null, red: null, injury: 55 });
  });

  it('keeps the first of two events of a kind and skips a player nobody knows', () => {
    const events = playerEventMinutes(
      [
        line('match.injury', 'pl1', 40),
        line('match.injury', 'pl1', 20),
        line('match.card.red', 'stranger', 10),
      ],
      lineups,
    );

    expect(events.get('p1')?.injury).toBe(20);
    expect(events.size).toBe(1);
  });
});

describe('hasHappened', () => {
  it('shows nothing before the replay starts', () => {
    expect(hasHappened(1, NOTHING_HAS_HAPPENED)).toBe(false);
    expect(hasHappened(null, NOTHING_HAS_HAPPENED)).toBe(false);
  });

  it('shows an event once the clock reaches its minute, and not before', () => {
    expect(hasHappened(30, 29)).toBe(false);
    expect(hasHappened(30, 30)).toBe(true);
    expect(hasHappened(30, 31)).toBe(true);
  });

  it('shows an undated event only when the whole match is shown', () => {
    expect(hasHappened(null, 89)).toBe(false);
    expect(hasHappened(null, EVERYTHING_HAS_HAPPENED)).toBe(true);
  });
});
