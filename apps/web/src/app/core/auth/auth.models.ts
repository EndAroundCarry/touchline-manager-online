/**
 * The auth module's transport shapes.
 *
 * These mirror `TouchlineManager.Contracts.Auth` on the server. They are handwritten for now because
 * the generated client arrives with the OpenAPI pipeline (`tools/openapi-client`); when that lands,
 * these interfaces are replaced by its output and the handwritten copies are deleted rather than kept
 * in step by hand.
 */

/** The account lifecycle state, as the server's stable lowercase code. */
export type UserStatus = 'pending' | 'active' | 'suspended' | 'deletion_pending' | 'anonymized';

/** The public projection of an account. */
export interface UserProfile {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailVerified: boolean;
  readonly status: UserStatus;
  readonly roles: readonly string[];
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
}

/** A signed-in session. The refresh token is never here; it travels only as an HttpOnly cookie. */
export interface AuthSession {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: string;
  readonly serverTime: string;
  readonly user: UserProfile;
}

/** The result of a registration request. */
export interface RegistrationAccepted {
  readonly userId: string;
  readonly email: string;
  readonly verificationEmailSent: boolean;
}

/** A neutral acknowledgement for operations that must not reveal whether an account exists. */
export interface RequestAccepted {
  readonly message: string;
  readonly serverTime: string;
}

/** The result of a profile change, including the new concurrency version. */
export interface ProfileUpdated {
  readonly user: UserProfile;
  readonly version: number;
}

/** A profile read together with the entity tag the next conditional write must carry. */
export interface ProfileSnapshot {
  readonly profile: UserProfile;
  readonly etag: string | null;
}

/**
 * The account's own data as one document (`F-07`, master plan §12.4).
 *
 * Mirrors `TouchlineManager.Contracts.Auth.AccountExportResponse`. It carries no secret or hidden value:
 * no password hash, token hash, or client-fingerprint hash.
 */
export interface AccountExport {
  readonly generatedAt: string;
  readonly account: ExportedAccount;
  readonly consents: readonly ExportedConsent[];
  readonly manager: ExportedManager | null;
  readonly tenures: readonly ExportedTenure[];
  readonly sessions: readonly ExportedSession[];
  readonly finance: ExportedFinance | null;
}

/** The account's own identity and lifecycle record. */
export interface ExportedAccount {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailVerified: boolean;
  readonly status: UserStatus;
  readonly roles: readonly string[];
  readonly lastLoginAt: string | null;
  readonly createdAt: string;
}

/** One legal document the account accepted, and when. */
export interface ExportedConsent {
  readonly documentType: string;
  readonly version: string;
  readonly acceptedAt: string;
}

/** The account's manager profile. */
export interface ExportedManager {
  readonly id: string;
  readonly reputation: number;
  readonly locale: string;
  readonly timeZone: string;
  readonly takeoverCooldownUntil: string | null;
  readonly createdAt: string;
}

/** One club the account has managed. */
export interface ExportedTenure {
  readonly id: string;
  readonly clubId: string;
  readonly clubName: string;
  readonly controlStatus: string;
  readonly startedAt: string;
  readonly endedAt: string | null;
  readonly endReason: string | null;
}

/** One active session, without any token material. */
export interface ExportedSession {
  readonly id: string;
  readonly issuedAt: string;
  readonly expiresAt: string;
  readonly lastUsedAt: string | null;
}

/** The current club's ledger, as the account's own transactional history. */
export interface ExportedFinance {
  readonly clubId: string;
  readonly entries: readonly ExportedLedgerEntry[];
  readonly truncated: boolean;
}

/** One ledger line in the export. */
export interface ExportedLedgerEntry {
  readonly sequence: number;
  readonly category: string;
  readonly cashDeltaMinor: number;
  readonly reservedDeltaMinor: number;
  readonly resultingCashMinor: number;
  readonly resultingReservedMinor: number;
  readonly descriptionTemplate: string;
  readonly createdAt: string;
}

/** Request bodies. */
export interface RegisterPayload {
  readonly email: string;
  readonly displayName: string;
  readonly password: string;
  readonly acceptTerms: boolean;
}

export interface LoginPayload {
  readonly email: string;
  readonly password: string;
}

export interface ResetPasswordPayload {
  readonly userId: string;
  readonly token: string;
  readonly newPassword: string;
}
