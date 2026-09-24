import { randomUUID } from 'node:crypto';

/** An account the journeys create for themselves. */
export interface TestAccount {
  readonly email: string;
  readonly displayName: string;
  readonly password: string;
}

/**
 * The password every journey account uses.
 *
 * It satisfies the server's minimum length and is deliberately a passphrase rather than a fixture
 * secret, because a test asserting on password rules should use a password a person would choose.
 */
export const password = 'correct-horse-battery';

/**
 * Builds a unique account.
 *
 * Every journey registers a fresh account so that no test can be affected by another's data, and so
 * a journey can be run repeatedly against the same persistent database without cleanup.
 */
export function createAccount(): TestAccount {
  const unique = randomUUID().replaceAll('-', '').slice(0, 12);

  return {
    email: `e2e-${unique}@example.com`,
    displayName: `Mgr${unique}`,
    password,
  };
}
