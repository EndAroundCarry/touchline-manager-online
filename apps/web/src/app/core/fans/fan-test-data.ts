import { ClubFixture } from '../competition/competition.models';
import { FanBase, FanEvent, FanExpectation, FanRecentMatch } from './fan.models';

/** Builders for the specs that read the fan base. Not imported by the app. */

/** A fixture of the manager's club against Vale Athletic. */
export function clubFixture(overrides: Partial<ClubFixture> = {}): ClubFixture {
  return {
    id: 'fixture-1',
    roundNumber: 1,
    venue: 'home',
    opponentClubId: 'club-2',
    opponentName: 'Vale Athletic',
    opponentShortName: 'VAL',
    opponentPrimaryColour: '#12284c',
    opponentSecondaryColour: '#fcd116',
    opponentCrestShape: 'shield',
    opponentCrestPattern: 'stripes',
    kickoffAt: '2026-10-20T18:00:00Z',
    lockAt: '2026-10-20T17:00:00Z',
    status: 'scheduled',
    homeScore: null,
    awayScore: null,
    matchId: null,
    outcome: null,
    kind: 'league',
    stageLabel: null,
    ...overrides,
  };
}

/** What the fans expect: a likely win, 58% win, 24% draw, 18% loss, a 2–1 bet. */
export function expectation(overrides: Partial<FanExpectation> = {}): FanExpectation {
  return {
    expectedPointsCentis: 198,
    winBp: 5_800,
    drawBp: 2_400,
    lossBp: 1_800,
    labelCode: 'should_win',
    labelPhrase: 'Should win',
    predictedGoalsFor: 2,
    predictedGoalsAgainst: 1,
    ...overrides,
  };
}

/** One point of the history. */
export function fanEvent(overrides: Partial<FanEvent> = {}): FanEvent {
  return {
    id: 'event-1',
    kind: 'match',
    occurredAt: '2026-10-13T20:00:00Z',
    seasonNumber: 1,
    fansAfter: 10_000,
    moodAfterBp: 5_000,
    fixtureId: null,
    expectedPointsCentis: null,
    pointsWon: null,
    gap: null,
    ...overrides,
  };
}

/** A published match with what the fans expected and how they reacted. */
export function recentMatch(overrides: Partial<FanRecentMatch> = {}): FanRecentMatch {
  return {
    fixture: clubFixture({
      id: 'fixture-0',
      roundNumber: 0,
      status: 'published',
      homeScore: 2,
      awayScore: 0,
      matchId: 'match-0',
      outcome: 'win',
      opponentName: 'Northfield',
    }),
    expectation: expectation(),
    pointsWon: 3,
    fansBefore: 10_000,
    fansAfter: 10_450,
    moodBeforeBp: 5_000,
    moodAfterBp: 5_280,
    ...overrides,
  };
}

/** The Fan Base page for a club at its opening, with one match published. */
export function fanBase(overrides: Partial<FanBase> = {}): FanBase {
  return {
    clubId: 'club-1',
    clubName: 'Harbour Town',
    tierNumber: 1,
    fans: 10_450,
    wantToCome: 3_135,
    homeDemand: 3_200,
    moodBp: 5_280,
    moodCode: 'calm',
    moodPhrase: 'Calm',
    floorFans: 4_000,
    softCapFans: 183_333,
    seasonTargetRank: 6,
    lastWeekGap: 2,
    seasonFansChange: 450,
    history: 'tenure',
    tenureStartedAt: '2026-10-13T08:00:00Z',
    nextFixture: { fixture: clubFixture(), expectation: expectation(), frozen: true },
    recentMatches: [recentMatch()],
    events: [
      fanEvent({ id: 'e1', kind: 'takeover', fansAfter: 10_000, moodAfterBp: 5_000 }),
      fanEvent({
        id: 'e2',
        kind: 'match',
        occurredAt: '2026-10-14T20:00:00Z',
        fixtureId: 'fixture-0',
        fansAfter: 10_450,
        moodAfterBp: 5_280,
        expectedPointsCentis: 198,
        pointsWon: 3,
      }),
    ],
    serverTime: '2026-10-15T09:00:00Z',
    ...overrides,
  };
}
