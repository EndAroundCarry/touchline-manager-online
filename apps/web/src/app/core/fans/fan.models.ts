import { ClubFixture } from '../competition/competition.models';

/** How much of the club's fan history the Fan Base page asks for (`FAN-10`). */
export type FanHistoryScope = 'tenure' | 'club';

/** What a club's fans expect from one match, from that club's side (`FAN-4`). */
export interface FanExpectation {
  /** The points the fans expect the club to take, in hundredths. */
  readonly expectedPointsCentis: number;
  readonly winBp: number;
  readonly drawBp: number;
  readonly lossBp: number;

  /** The stable code of the label, such as `should_win`. */
  readonly labelCode: string;

  /** The words the game shows, such as "Should win". */
  readonly labelPhrase: string;
  readonly predictedGoalsFor: number;
  readonly predictedGoalsAgainst: number;
}

/** The club's next match with what its fans expect from it. */
export interface FanNextFixture {
  readonly fixture: ClubFixture;
  readonly expectation: FanExpectation;

  /** Whether the expectation was frozen when the team sheets locked; false means it can still move. */
  readonly frozen: boolean;
}

/** One published match with what the fans expected and how they reacted. */
export interface FanRecentMatch {
  readonly fixture: ClubFixture;

  /** What the fans expected at the lock, or null when the match was locked before fans existed. */
  readonly expectation: FanExpectation | null;
  readonly pointsWon: number | null;
  readonly fansBefore: number | null;
  readonly fansAfter: number | null;
  readonly moodBeforeBp: number | null;
  readonly moodAfterBp: number | null;
}

/** The kinds of change that put a point on the charts. */
export type FanEventKind =
  'opening' | 'match' | 'weekly' | 'season_reset' | 'promotion' | 'relegation' | 'takeover';

/** One point on the charts: a change to the club's fans. */
export interface FanEvent {
  readonly id: string;
  readonly kind: FanEventKind;
  readonly occurredAt: string;
  readonly seasonNumber: number;
  readonly fansAfter: number;
  readonly moodAfterBp: number;
  readonly fixtureId: string | null;
  readonly expectedPointsCentis: number | null;
  readonly pointsWon: number | null;
  readonly gap: number | null;
}

/** The Fan Base page: the fans as they stand, what they expect next, how the last matches went, and the history. */
export interface FanBase {
  readonly clubId: string;
  readonly clubName: string;
  readonly tierNumber: number;
  readonly fans: number;
  readonly wantToCome: number;
  readonly homeDemand: number;
  readonly moodBp: number;
  readonly moodCode: string;
  readonly moodPhrase: string;
  readonly floorFans: number;
  readonly softCapFans: number;
  readonly seasonTargetRank: number | null;
  readonly lastWeekGap: number | null;
  readonly seasonFansChange: number;
  readonly history: FanHistoryScope;
  readonly tenureStartedAt: string | null;
  readonly nextFixture: FanNextFixture | null;
  readonly recentMatches: readonly FanRecentMatch[];
  readonly events: readonly FanEvent[];
  readonly serverTime: string;
}
