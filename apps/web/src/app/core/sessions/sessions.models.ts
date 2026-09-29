/**
 * The session-management transport shapes (`F-07`).
 *
 * These mirror `TouchlineManager.Contracts.Auth` on the server. A session carries no token, token hash,
 * or client-fingerprint hash: a manager lists sessions to recognise and revoke them, not to inspect
 * them.
 */

/** One active refresh session. */
export interface ActiveSession {
  readonly id: string;
  readonly issuedAt: string;
  readonly expiresAt: string;
  readonly lastUsedAt: string | null;
  readonly isCurrent: boolean;
}

/** The account's active sessions, and which one produced the request. */
export interface Sessions {
  readonly sessions: readonly ActiveSession[];
  readonly serverTime: string;
}
