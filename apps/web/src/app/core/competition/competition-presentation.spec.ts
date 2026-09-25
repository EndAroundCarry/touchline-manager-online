import {
  fixtureStatusLabel,
  goalDifferenceLabel,
  isUpcoming,
  lockCountdown,
  outcomeLabel,
  roundLabel,
  scoreLabel,
  teamSheetIssueMessage,
  venueLabel,
} from './competition-presentation';

/**
 * The competition module's presentation helpers.
 *
 * Pure functions, so they are tested without a component and without a server. The countdown is the one
 * with real behaviour: it is bounded to days, hours, and minutes because it is what a manager reads on the
 * dashboard, and the moment the deadline passes it must say so rather than count backwards.
 */

const NOW = new Date('2026-09-25T12:00:00Z');

describe('lockCountdown', () => {
  it('counts days and hours when the deadline is far off', () => {
    expect(lockCountdown('2026-09-28T12:00:00Z', NOW)).toBe('Locks in 3d 0h');
  });

  it('counts hours and minutes on the day', () => {
    expect(lockCountdown('2026-09-25T15:30:00Z', NOW)).toBe('Locks in 3h 30m');
  });

  it('counts minutes in the last hour', () => {
    expect(lockCountdown('2026-09-25T12:45:00Z', NOW)).toBe('Locks in 45m');
  });

  it('says the sheets are locked once the deadline has passed', () => {
    expect(lockCountdown('2026-09-25T11:59:00Z', NOW)).toBe('Team sheets are locked');
  });

  it('falls back rather than showing NaN for an unusable instant', () => {
    expect(lockCountdown('not-a-date', NOW)).toBe('Lock time unavailable');
  });
});

describe('fixture labels', () => {
  it('names a lifecycle state', () => {
    expect(fixtureStatusLabel('scheduled')).toBe('Scheduled');
    expect(fixtureStatusLabel('published')).toBe('Played');
  });

  it('falls back to the raw code for a state it does not know', () => {
    expect(fixtureStatusLabel('abandoned')).toBe('abandoned');
  });

  it('names the side', () => {
    expect(venueLabel('home')).toBe('Home');
    expect(venueLabel('away')).toBe('Away');
  });

  it('names an outcome only when there is one', () => {
    expect(outcomeLabel('win')).toBe('Win');
    expect(outcomeLabel('loss')).toBe('Loss');
    expect(outcomeLabel(null)).toBe('');
  });

  it('formats a scoreline only when both halves exist', () => {
    expect(scoreLabel(2, 1)).toBe('2\u20131');
    expect(scoreLabel(null, 1)).toBe('');
    expect(scoreLabel(1, null)).toBe('');
  });

  it('labels a round', () => {
    expect(roundLabel(14)).toBe('Round 14');
  });
});

describe('teamSheetIssueMessage', () => {
  it('names the slot when the issue concerns one', () => {
    expect(teamSheetIssueMessage({ code: 'TEAM_SHEET_PLAYER_UNAVAILABLE', slotNumber: 5 })).toBe(
      'Slot 5: That player is injured or suspended.',
    );
  });

  it('leaves the slot out when the issue concerns the whole side', () => {
    expect(teamSheetIssueMessage({ code: 'TEAM_SHEET_INCOMPLETE', slotNumber: null })).toBe(
      'Pick all eleven starters before saving.',
    );
  });
});

describe('isUpcoming', () => {
  it('counts everything not yet published or void', () => {
    expect(isUpcoming('scheduled')).toBe(true);
    expect(isUpcoming('staged')).toBe(true);
    expect(isUpcoming('published')).toBe(false);
    expect(isUpcoming('void')).toBe(false);
  });
});

describe('goalDifferenceLabel', () => {
  it('signs a positive difference, so the sign is what is read', () => {
    expect(goalDifferenceLabel(7)).toBe('+7');
  });

  it('leaves a negative difference and zero as the number they are', () => {
    expect(goalDifferenceLabel(-3)).toBe('-3');
    expect(goalDifferenceLabel(0)).toBe('0');
  });
});
