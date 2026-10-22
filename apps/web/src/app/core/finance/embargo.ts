import { ApiError } from '../api/api-error';

/**
 * The codes the server refuses with while a club is in the red or in administration (`DEBT-3`): no bids, no
 * building, until the cash is back above zero.
 */
const EMBARGO_CODES: ReadonlySet<string> = new Set(['TRANSFER_EMBARGO', 'BUILD_EMBARGO']);

/** Whether a failure is the embargo, so the screen can say why and point at the finances. */
export function isEmbargo(error: unknown): boolean {
  return error instanceof ApiError && EMBARGO_CODES.has(error.code);
}
