# API error codes

Every error body is `application/problem+json` and carries a stable `code` extension. Use `code` for program logic and translations. `title` and `detail` are English text for developers and may change.

```json
{
  "status": 409,
  "title": "Conflict",
  "detail": "The skill is already added to this resume.",
  "instance": "/api/resumes/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/skills",
  "code": "duplicate_skill",
  "traceId": "00-..."
}
```

`traceId` identifies the request in support cases. The `401` and `429` responses produced before a controller runs have no `traceId`.

Rules for changing this list:

- Never rename or reuse a code. Clients depend on it.
- Add new codes here in the same pull request. A test fails when a code in `ApiErrorCodes` is missing from this file.
- A code never contains request data, identifiers or internal details.

## General codes

| Code | Status | Meaning |
|---|---|---|
| `validation_failed` | 400 | The request failed validation. Field messages are in `errors` when available. |
| `malformed_request` | 400 and other 4xx | The server could not read the request, for example bad framing. |
| `unauthorized` | 401 | The token is missing, invalid or expired, or the credentials are wrong. |
| `not_found` | 404 | The resource does not exist or belongs to another user. |
| `conflict` | 409 | The request conflicts with the current state and no specific code applies. |
| `precondition_failed` | 412 | `If-Match` is stale. Reload the resource and retry with the new ETag. |
| `payload_too_large` | 413 | The body exceeds the size limit. |
| `unsupported_media_type` | 415 | Form bodies are not accepted. |
| `precondition_required` | 428 | The operation needs an `If-Match` header. |
| `rate_limited` | 429 | A rate limit was reached. See `Retry-After`. |
| `internal_error` | 500 | An unexpected server error. Report the `traceId`. |

## Specific conflict codes

All have status 409 unless noted.

| Code | Meaning |
|---|---|
| `registration_rejected` | Registration could not be completed. The message does not say why, so that it does not reveal whether an email is registered. |
| `section_limit_reached` | A resume section reached its maximum item count. |
| `duplicate_skill` | The skill is already on the resume, project or job posting. |
| `duplicate_language` | The language is already on the resume. |
| `duplicate_application` | An application already exists for this job posting. |
| `reference_conflict` | A referenced job posting or resume is no longer valid for this user. |
| `resource_in_use` | The resource cannot be deleted while other records use it. |
| `invalid_status_transition` | The application cannot move to the requested status. |
| `parse_draft_state_invalid` | The parse draft status does not allow this operation, or it has expired. |
| `parse_draft_outdated` | The parse draft no longer matches the current data. Request a new parse. |
| `skill_provenance_locked` | The source of this job posting skill does not allow manual adoption. |
| `parse_cooldown` | Status 429. The same content was parsed recently. See `Retry-After`. |

## Validation errors

```json
{
  "status": 400,
  "title": "Validation failed",
  "code": "validation_failed",
  "errors": {
    "$.workType": ["Must be one of: OnSite, Hybrid, Remote."]
  }
}
```

Field messages are generic, so that they never echo input. The exception is an unknown enum value, whose message lists the allowed names.

## Enums

Enums are sent and received as names, such as `"Applied"`. Numbers are rejected in both JSON bodies and query strings. Names are matched without regard to case on input and are always written in the form shown in the OpenAPI document.

## Responses without a body

These do not pass through the API error handling and have no `code`:

- `404` for a path that does not exist, and `405` for an unsupported method.
- Errors from Render or Cloudflare before the request reaches the API, for example while the service wakes up.

Treat a missing or non-JSON body as a connection or platform error.

## Browser access

Allowed origins can read `ETag` and `Retry-After` through `Access-Control-Expose-Headers`. Other origins receive no CORS headers.

Requests rejected for size (`413`, `415`) are answered before the CORS step, so a browser reports them as a network error.
