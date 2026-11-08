import { describe, expect, it, vi } from 'vitest';
import { AuthSession } from '../auth/session';
import { apiJson, ApiError, shouldRetry } from './http';

function sessionWith(fetchFn: typeof fetch) {
  return new AuthSession({ fetchFn, locks: null, createChannel: null, onOnline: null });
}

describe('apiJson', () => {
  it('sends JSON and If-Match, and reads the body', async () => {
    const fetchFn = vi.fn<typeof fetch>(() =>
      Promise.resolve(new Response('{"eTag":"\\"2\\""}', { status: 200, headers: { 'content-type': 'application/json' } })),
    );

    const body = await apiJson<{ eTag: string }>(sessionWith(fetchFn), '/api/resumes/r1', {
      method: 'PUT',
      body: { title: 'Yeni' },
      ifMatch: '"1"',
    });

    expect(body).toEqual({ eTag: '"2"' });
    const init = fetchFn.mock.calls[0]?.[1];
    const headers = new Headers(init?.headers);
    expect(init?.method).toBe('PUT');
    expect(init?.body).toBe('{"title":"Yeni"}');
    expect(headers.get('If-Match')).toBe('"1"');
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('returns undefined for 204', async () => {
    const fetchFn = vi.fn<typeof fetch>(() => Promise.resolve(new Response(null, { status: 204 })));
    expect(await apiJson(sessionWith(fetchFn), '/api/resumes/r1/archive', { method: 'PATCH' })).toBeUndefined();
  });

  it('throws ApiError with the problem for an error status', async () => {
    const fetchFn = vi.fn<typeof fetch>(() =>
      Promise.resolve(
        new Response('{"status":412,"code":"precondition_failed"}', {
          status: 412,
          headers: { 'content-type': 'application/problem+json' },
        }),
      ),
    );

    const error = await apiJson(sessionWith(fetchFn), '/api/resumes/r1').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 412, code: 'precondition_failed', isNetworkError: false });
  });

  it('throws a network ApiError when there is no answer', async () => {
    const fetchFn = vi.fn<typeof fetch>(() => Promise.reject(new TypeError('Failed to fetch')));

    const error = await apiJson(sessionWith(fetchFn), '/api/resumes').catch((caught: unknown) => caught);

    expect(error).toMatchObject({ status: null, isNetworkError: true });
  });
});

describe('shouldRetry', () => {
  const http = (status: number) => new ApiError(status, null);

  it.each([400, 401, 403, 404, 409, 412, 428, 429])('never retries a %i', (status) => {
    expect(shouldRetry(0, http(status))).toBe(false);
  });

  it('retries a network error or a 5xx at most twice', () => {
    for (const error of [new ApiError(null, null), http(500), http(503)]) {
      expect(shouldRetry(0, error)).toBe(true);
      expect(shouldRetry(1, error)).toBe(true);
      expect(shouldRetry(2, error)).toBe(false);
    }
  });

  it('does not retry unknown errors', () => {
    expect(shouldRetry(0, new Error('bug'))).toBe(false);
  });
});
