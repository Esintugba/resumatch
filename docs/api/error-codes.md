# API error codes

Every error body is `application/problem+json` and carries a stable `code` extension, with one exception listed under [Responses without a code](#responses-without-a-code). Use `code` for program logic and translations. `title` and `detail` are English text for developers and may change.

The OpenAPI document describes these bodies with three schemas, and the web client's types are generated from it:

| Schema | Used for | `code` |
|---|---|---|
| `ProblemDetails` | Every error status except 400 and 415 | Required, one of the codes below |
| `ValidationProblemDetails` | 400 | Required; `errors` holds field messages when available |
| `UnsupportedMediaTypeProblemDetails` | 415 | Optional |

Besides the statuses an endpoint declares, every endpoint lists 500, every endpoint that needs a user lists 401, every endpoint with parameters lists 400, every endpoint with a body lists 413 and 415, and every rate-limited endpoint lists 429.

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
| `csrf_rejected` | 403 | A refresh or logout request lacked the `X-ResuMatch-CSRF: 1` header or did not come from an allowed origin. Do not retry. |
| `not_found` | 404 | The resource does not exist or belongs to another user. |
| `conflict` | 409 | The request conflicts with the current state and no specific code applies. |
| `precondition_failed` | 412 | `If-Match` is stale. Reload the resource and retry with the new ETag. |
| `payload_too_large` | 413 | The body exceeds the size limit. |
| `unsupported_media_type` | 415 | Form bodies are not accepted. |
| `precondition_required` | 428 | The operation needs an `If-Match` header. |
| `rate_limited` | 429 | A rate limit was reached. See `Retry-After`. |
| `internal_error` | 500 | An unexpected server error. Report the `traceId`. |
| `feature_disabled` | 503 | This deployment has the feature turned off. Do not retry. Check `GET /api/capabilities`. |

## Specific conflict codes

All have status 409 unless noted.

| Code | Meaning |
|---|---|
| `registration_rejected` | Registration could not be completed. The message does not say why, so that it does not reveal whether an email is registered. |
| `section_limit_reached` | A resume section reached its maximum item count. |
| `duplicate_skill` | The skill is already on the resume, project or job posting. An existing row is never changed. |
| `duplicate_language` | The language is already on the resume. |
| `duplicate_application` | An application already exists for this job posting. |
| `reference_conflict` | A referenced job posting or resume is no longer valid for this user. |
| `resource_in_use` | The resource cannot be deleted while other records use it. |
| `invalid_status_transition` | The application cannot move to the requested status. |
| `parse_draft_state_invalid` | The parse draft status does not allow this operation, or it has expired. |
| `parse_draft_outdated` | The parse draft no longer matches the current data. Request a new parse. |
| `skill_provenance_locked` | The source of this job posting skill does not allow the operation. It cannot be adopted, or it came from AI parsing and cannot be removed by hand. |
| `job_posting_skill_limit_reached` | The job posting has the maximum of 50 skills. |
| `parse_cooldown` | Status 429. The same content was parsed recently. See `Retry-After`. |
| `refresh_token_superseded` | Another tab refreshed with the same cookie a moment ago. The browser already holds the new cookie: retry the refresh once. |

## Optimistic concurrency

Records with an `eTag` are changed with `If-Match: <eTag>`. Without it the answer is 428 `precondition_required`; with an outdated one, 412 `precondition_failed`. Reload the record, then retry with the new ETag. Every successful change returns the new ETag in the body and the `ETag` header.

| Endpoint | Rule |
|---|---|
| `PUT /api/resumes/{id}` | If-Match required. |
| `PATCH /api/resumes/{id}/archive`, `/unarchive` | If-Match required to change the state; 200 with the resume and its new ETag. Already in that state: 200, unchanged, no precondition. |
| `PUT /api/resumes/{id}/contact` | No contact yet: send no If-Match; 201 creates it. With If-Match while there is no contact, or when another request created it first: 412. Existing contact: If-Match required. |
| `DELETE /api/resumes/{id}/contact` | If-Match required. |

The resume's main fields and its archive state share one ETag; the contact has its own.

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

## Capabilities

`GET /api/capabilities` is anonymous and tells a client which optional features this deployment offers:

```json
{ "jobParsing": false }
```

When `jobParsing` is `false`, `POST /api/job-postings/{id}/parse` returns `feature_disabled`, whatever the job posting or `If-Match` value. No draft is created and no provider is called.

## Responses without a code

A JSON endpoint that receives no body or a media type other than JSON answers 415 from ASP.NET Core itself. The body is `application/problem+json` with `status` and `traceId` but no `code`. A form body gets 415 from the API with the code `unsupported_media_type`.

## Responses without a body

These do not pass through the API error handling and have no `code`:

- `404` for a path that does not exist, and `405` for an unsupported method.
- Errors from Render or Cloudflare before the request reaches the API, for example while the service wakes up.

Treat a missing or non-JSON body as a connection or platform error.

## Browser access

Allowed origins can read `ETag` and `Retry-After` through `Access-Control-Expose-Headers`. Other origins receive no CORS headers.

Requests rejected for size (`413`, `415`) are answered before the CORS step, so a browser reports them as a network error.
