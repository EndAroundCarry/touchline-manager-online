import { ApiError } from '../api/api-error';
import { isEmbargo } from './embargo';

/** The embargo is told apart by its code, never by its wording. */
describe('isEmbargo', () => {
  function failure(code: string): ApiError {
    return new ApiError(409, code, 'Whatever the server says.', null, new Map());
  }

  it.each(['TRANSFER_EMBARGO', 'BUILD_EMBARGO'])('recognises %s', (code) => {
    expect(isEmbargo(failure(code))).toBe(true);
  });

  it('leaves other refusals alone', () => {
    expect(isEmbargo(failure('INSUFFICIENT_FUNDS'))).toBe(false);
    expect(isEmbargo(new Error('TRANSFER_EMBARGO'))).toBe(false);
    expect(isEmbargo(null)).toBe(false);
  });
});
