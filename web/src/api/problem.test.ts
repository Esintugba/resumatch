import { describe, expect, it } from 'vitest';
import { isApiErrorCode, readProblem } from './problem';

function problem(status: number, body: unknown, contentType = 'application/problem+json; charset=utf-8') {
  return new Response(typeof body === 'string' ? body : JSON.stringify(body), {
    status,
    headers: { 'content-type': contentType },
  });
}

describe('readProblem', () => {
  it('reads a problem with its code and trace id', async () => {
    const result = await readProblem(
      problem(404, { status: 404, title: 'Not Found', code: 'not_found', traceId: '00-abc-01' }),
    );

    expect(result).toEqual({ status: 404, title: 'Not Found', code: 'not_found', traceId: '00-abc-01' });
  });

  it('reads the field messages of a validation error', async () => {
    const result = await readProblem(
      problem(400, {
        status: 400,
        code: 'validation_failed',
        errors: { Email: ['The supplied value is invalid.'], Password: ['The supplied value is invalid.'] },
      }),
    );

    expect(result?.code).toBe('validation_failed');
    expect(result?.errors).toEqual({
      Email: ['The supplied value is invalid.'],
      Password: ['The supplied value is invalid.'],
    });
  });

  it('keeps the status when the body has no code, as in the framework 415', async () => {
    const result = await readProblem(problem(415, { status: 415, title: 'Unsupported Media Type', traceId: 't' }));

    expect(result).toEqual({ status: 415, title: 'Unsupported Media Type', code: null, traceId: 't' });
  });

  it('does not trust an unknown code', async () => {
    const result = await readProblem(problem(409, { status: 409, code: 'something_new' }));

    expect(result).toEqual({ status: 409, code: null });
  });

  it('falls back to the response status when the body has none', async () => {
    expect((await readProblem(problem(401, { code: 'unauthorized' })))?.status).toBe(401);
  });

  it.each([
    ['a success response', new Response('{}', { status: 200, headers: { 'content-type': 'application/problem+json' } })],
    ['an HTML platform error', problem(502, '<html>Bad gateway</html>', 'text/html')],
    ['plain JSON', problem(500, { code: 'internal_error' }, 'application/json')],
    ['a broken body', problem(500, '{"status":', 'application/problem+json')],
    ['a body that is not an object', problem(500, '[1,2]')],
  ])('returns null for %s', async (_name, response) => {
    expect(await readProblem(response)).toBeNull();
  });
});

describe('isApiErrorCode', () => {
  it('accepts documented codes only', () => {
    expect(isApiErrorCode('refresh_token_superseded')).toBe(true);
    expect(isApiErrorCode('toString')).toBe(false);
    expect(isApiErrorCode(42)).toBe(false);
  });
});
