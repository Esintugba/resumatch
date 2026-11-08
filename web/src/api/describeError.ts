import { messages } from '../i18n';
import { ApiError } from './http';
import type { ApiProblem } from './problem';

/** The Turkish text for a failed call: by problem code, a connection error, or a generic one with the support code. */
export function describeError(error: unknown): string {
  const problem: ApiProblem | null = error instanceof ApiError ? error.problem : null;
  if (error instanceof ApiError && error.isNetworkError) return messages.errors.network;
  if (problem === null) return error instanceof ApiError ? messages.errors.network : messages.errors.unexpected;
  const text = problem.code === null ? messages.errors.unexpected : messages.errors.codes[problem.code];
  return problem.code === 'internal_error' && problem.traceId
    ? `${text} ${messages.errors.supportCode(problem.traceId)}`
    : text;
}
