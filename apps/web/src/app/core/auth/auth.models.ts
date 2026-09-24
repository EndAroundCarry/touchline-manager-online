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
