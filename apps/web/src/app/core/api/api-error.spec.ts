import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ApiError } from './api-error';

/**
 * The error mapping is the single place where a server failure becomes something the UI can branch
 * on, so the machine-readable code and the correlation ID are asserted rather than assumed.
 */
describe('ApiError', () => {
  it('reads the stable code, detail and correlation ID from a problem document', () => {
    const response = new HttpErrorResponse({
      status: 409,
      statusText: 'Conflict',
      url: '/api/v1/club-claims',
      headers: new HttpHeaders({ 'X-Correlation-Id': 'corr-123' }),
      error: {
        type: 'https://touchline.example/problems/club-already-claimed',
        title: 'Club already claimed',
        status: 409,
        detail: 'Another manager took this club first.',
        code: 'CLUB_ALREADY_CLAIMED',
      },
    });

    const error = ApiError.fromResponse(response);

    expect(error.status).toBe(409);
    expect(error.code).toBe('CLUB_ALREADY_CLAIMED');
    expect(error.detail).toBe('Another manager took this club first.');
    expect(error.correlationId).toBe('corr-123');
    expect(error.message).toBe('Another manager took this club first.');
  });

  it('falls back to a stable code when the problem document omits one', () => {
    const response = new HttpErrorResponse({ status: 503, error: { title: 'Unavailable' } });

    const error = ApiError.fromResponse(response);

    expect(error.code).toBe('HTTP_503');
    expect(error.detail).toBe('Unavailable');
  });

  it('survives a non-object error body', () => {
    const response = new HttpErrorResponse({ status: 502, error: 'bad gateway' });

    const error = ApiError.fromResponse(response);

    expect(error.code).toBe('UNEXPECTED_RESPONSE');
    expect(error.detail).toBe('The server returned an unexpected response.');
  });

  it('collects field-level validation messages', () => {
    const response = new HttpErrorResponse({
      status: 400,
      error: {
        status: 400,
        code: 'VALIDATION_FAILED',
        detail: 'The request was not valid.',
        errors: { displayName: ['Display name is required.', 'Display name is too long.'], other: 'not-an-array' },
      },
    });

    const error = ApiError.fromResponse(response);

    expect(error.fieldErrors.get('displayName')).toEqual([
      'Display name is required.',
      'Display name is too long.',
    ]);
    expect(error.fieldErrors.has('other')).toBe(false);
  });

  it('recognises a stale-write conflict', () => {
    const error = ApiError.fromResponse(new HttpErrorResponse({ status: 412 }));

    expect(error.isPreconditionFailed).toBe(true);
  });

  it('recognises a request that never reached the server', () => {
    const error = ApiError.fromResponse(new HttpErrorResponse({ status: 0 }));

    expect(error.isOffline).toBe(true);
    expect(error.isPreconditionFailed).toBe(false);
  });
});
