import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Marks a request as an auth endpoint.
 *
 * The access-token interceptor leaves these alone: they must not carry a bearer token, and a 401 from
 * one of them must not trigger a refresh — a failed sign-in is an answer, not an expired session.
 */
export const IS_AUTH_ENDPOINT = new HttpContextToken<boolean>(() => false);

/**
 * Marks a request as already retried after a refresh.
 *
 * Without it, a request that is still unauthorised after a successful refresh would refresh and retry
 * forever.
 */
export const SKIP_AUTH_RETRY = new HttpContextToken<boolean>(() => false);

/** Builds the context for an auth endpoint request. */
export function authEndpointContext(): HttpContext {
  return new HttpContext().set(IS_AUTH_ENDPOINT, true);
}
