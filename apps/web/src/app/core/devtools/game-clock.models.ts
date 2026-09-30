/**
 * The stepped-clock transport shapes (ADR-0049).
 *
 * These mirror `TouchlineManager.Api.Endpoints.OpsEndpoints` on the server. They exist only in
 * non-production: the endpoints they call are mapped only when the stepped clock is in force.
 */

/** Which way a step moves the game clock. */
export type GameClockTarget = 'day' | 'matchday';

/** The current game instant and the next round, for the toolbar. */
export interface GameClockStatus {
  readonly gameNow: string;
  readonly nextMatchdayAt: string | null;
}

/** The result of asking for a step. */
export interface GameClockAdvance {
  readonly target: string;
  readonly targetInstantUtc: string;
  readonly businessKey: string;
  readonly enqueued: boolean;
}
