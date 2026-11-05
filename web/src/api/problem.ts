import type { components } from './schema';

// Error bodies as the OpenAPI document describes them (docs/api/error-codes.md).
export type ProblemDetails = components['schemas']['ProblemDetails'];
export type ValidationProblemDetails = components['schemas']['ValidationProblemDetails'];
export type ApiErrorCode = ProblemDetails['code'];

/** An error answer from the API, read from an application/problem+json body. */
export interface ApiProblem {
  status: number;
  /** Null when the body has no code (ASP.NET Core's own 415) or a code this client does not know. */
  code: ApiErrorCode | null;
  title?: string;
  detail?: string;
  traceId?: string;
  /** Field messages of a validation error, keyed by field path. */
  errors?: Record<string, string[]>;
}

// Typed as a record so the typecheck fails when the API adds or removes a code
// and this list is not updated with the regenerated schema.
const knownCodes: Record<ApiErrorCode, true> = {
  validation_failed: true,
  malformed_request: true,
  unauthorized: true,
  not_found: true,
  conflict: true,
  precondition_failed: true,
  precondition_required: true,
  payload_too_large: true,
  unsupported_media_type: true,
  rate_limited: true,
  internal_error: true,
  feature_disabled: true,
  csrf_rejected: true,
  registration_rejected: true,
  section_limit_reached: true,
  duplicate_skill: true,
  duplicate_language: true,
  duplicate_application: true,
  reference_conflict: true,
  resource_in_use: true,
  invalid_status_transition: true,
  parse_draft_state_invalid: true,
  parse_draft_outdated: true,
  skill_provenance_locked: true,
  job_posting_skill_limit_reached: true,
  parse_cooldown: true,
  refresh_token_superseded: true,
};

export function isApiErrorCode(value: unknown): value is ApiErrorCode {
  return typeof value === 'string' && Object.hasOwn(knownCodes, value);
}

/**
 * Reads an error response. Returns null when the body is not a problem
 * document, for example a platform error while the server wakes up; the
 * caller then treats it as a connection error. Never throws.
 */
export async function readProblem(response: Response): Promise<ApiProblem | null> {
  if (response.ok || !isProblemJson(response.headers.get('content-type'))) return null;

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return null;
  }
  if (!isRecord(body)) return null;

  const problem: ApiProblem = {
    status: typeof body.status === 'number' ? body.status : response.status,
    code: isApiErrorCode(body.code) ? body.code : null,
  };
  if (typeof body.title === 'string') problem.title = body.title;
  if (typeof body.detail === 'string') problem.detail = body.detail;
  if (typeof body.traceId === 'string') problem.traceId = body.traceId;
  const errors = readErrors(body.errors);
  if (errors) problem.errors = errors;
  return problem;
}

function isProblemJson(contentType: string | null): boolean {
  return contentType?.split(';')[0]?.trim().toLowerCase() === 'application/problem+json';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readErrors(value: unknown): Record<string, string[]> | undefined {
  if (!isRecord(value)) return undefined;
  const errors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      errors[field] = messages.filter((message): message is string => typeof message === 'string');
    }
  }
  return errors;
}
