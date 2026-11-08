import type { AuthSession } from '../auth/session';
import { readProblem, type ApiProblem } from './problem';

/** A failed API call: an HTTP error with its problem document, or no answer at all. */
export class ApiError extends Error {
  readonly status: number | null;
  readonly problem: ApiProblem | null;

  constructor(status: number | null, problem: ApiProblem | null) {
    super(status === null ? 'The API could not be reached.' : `The API answered ${String(status)}.`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get isNetworkError(): boolean {
    return this.status === null;
  }

  get code() {
    return this.problem?.code ?? null;
  }
}

export interface ApiRequest {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** The ETag the change is based on; sent as If-Match. */
  ifMatch?: string;
  signal?: AbortSignal;
}

/**
 * Calls the API through the session (Bearer token, one refresh on 401) and
 * returns the JSON body, or undefined for 204. Throws ApiError otherwise.
 */
export async function apiJson<T>(session: AuthSession, path: string, request: ApiRequest = {}): Promise<T> {
  const headers = new Headers();
  if (request.body !== undefined) headers.set('Content-Type', 'application/json');
  if (request.ifMatch !== undefined) headers.set('If-Match', request.ifMatch);

  let response: Response;
  try {
    response = await session.authorizedFetch(path, {
      method: request.method ?? 'GET',
      headers,
      ...(request.body === undefined ? {} : { body: JSON.stringify(request.body) }),
      ...(request.signal ? { signal: request.signal } : {}),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(null, null);
  }

  if (!response.ok) throw new ApiError(response.status, await readProblem(response));
  if (response.status === 204) return undefined as T;
  try {
    return (await response.json()) as T;
  } catch {
    // A success status with an unreadable body is a platform problem, not data.
    throw new ApiError(response.status, null);
  }
}

const maximumRetries = 2;

/**
 * Retries only what can heal by itself: no answer, or a 5xx. Never a 4xx,
 * whose answer will not change. Used for queries (GETs); mutations are never
 * retried automatically.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= maximumRetries) return false;
  if (!(error instanceof ApiError)) return false;
  return error.isNetworkError || (error.status !== null && error.status >= 500);
}
