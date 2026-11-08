# ResuMatch

[![CI](https://github.com/Esintugba/resumatch/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Esintugba/resumatch/actions/workflows/ci.yml)

**Live API:** <https://resumatch-api-da3z.onrender.com> (free tier: the first request after 15 idle minutes takes about 40 seconds; check [`/health/ready`](https://resumatch-api-da3z.onrender.com/health/ready))

ResuMatch is an ASP.NET Core Web API for ATS-oriented resume analysis and job posting matching. Users manage structured resumes (contact, experience, education, projects, skills, languages, certifications, links) and job postings, run a deterministic rule-based ATS analysis (`ats-v1`) with optional job-match scoring and recommendations, and track job applications. Job descriptions can be parsed into skill drafts by an optional OpenAI-backed background worker protected by budgets, rate limits, distributed concurrency, and a circuit breaker.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL. The integration tests run against `postgres:17.6-alpine`, which is the tested version.
- `dotnet-ef` for applying migrations: `dotnet tool install --global dotnet-ef --version 10.0.12`
- Docker, for the integration tests only (they start PostgreSQL with Testcontainers).

## First-time setup

### 1. Configure secrets

Development settings are read from [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (the `UserSecretsId` is already set in `API/API.csproj`). Two values are required for the API to start:

| Key | Requirement |
|---|---|
| `ConnectionStrings:DefaultConnection` | Npgsql connection string to an existing PostgreSQL server. |
| `Jwt:SecretKey` | Base64 that decodes to at least 32 bytes, from a cryptographically secure random source. |

`Jwt:Issuer`, `Jwt:Audience`, and the remaining settings have working defaults in `API/appsettings.json`.

```powershell
# Generate a 32-byte key (Windows PowerShell)
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes)

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=resumatch_dev;Username=postgres;Password=<password>" --project API
dotnet user-secrets set "Jwt:SecretKey" $key --project API
```

The job description parser is disabled by default (`JobDescriptionExtraction:Provider` is `Unavailable` and the parse worker is off). While it is disabled, parse requests return 503 with the code `feature_disabled`, and `GET /api/capabilities` reports `"jobParsing": false`. To use OpenAI, set `JobDescriptionExtraction:Provider` to `OpenAI` and provide `JobDescriptionExtraction:ApiKey` and `JobDescriptionExtraction:Model`; the validator rejects an OpenAI provider without both.

### 2. Create the database

The API never migrates at startup. Apply the migrations explicitly; this also creates the database if it does not exist:

```powershell
dotnet ef database update --project API
```

### 3. Run the API

```powershell
dotnet run --project API --launch-profile http    # http://localhost:5252
dotnet run --project API --launch-profile https   # https://localhost:7127
```

Check that it is up:

- `GET /health/live`: process liveness (no dependencies).
- `GET /health/ready`: database, schema compatibility, configuration, and job parser checks.

In the `Development` environment the OpenAPI document is served anonymously at `/openapi/v1.json` and Swagger UI at `/swagger` (for example `http://localhost:5252/swagger`). Other non-production environments can opt in with `HttpSecurity:ExposeOpenApi=true`; in `Production` both are disabled and enabling the option makes startup fail.

All `/api/*` endpoints except `POST /api/auth/register` and `POST /api/auth/login` require a bearer token returned by those endpoints.

Enums are sent and received as names such as `"Applied"`; numbers are rejected. Error responses are `application/problem+json` with a stable `code`, listed in [API error codes](docs/api/error-codes.md).

## Web app (`web/`)

React, TypeScript and Vite. There are no screens yet: one placeholder page and a draft of the "server is waking up" message. The web app is not deployed yet.

**Requirements:** Node.js at the exact version in [`web/.nvmrc`](web/.nvmrc) (`nvm use` or `fnm use` in `web/`). `web/.npmrc` sets `engine-strict`, so npm refuses other Node majors and older 24.x releases.

```powershell
cd web
npm ci                              # always from package-lock.json
npm run dev                         # http://localhost:5173
```

Vite proxies `/api` and `/health` to the API's `http` profile on `http://localhost:5252`, so start the API first (`dotnet run --project API --launch-profile http`). The browser then talks only to `localhost:5173`, which the API's Development settings allow with credentials; see [Local development](docs/api/authentication.md#local-development). Without the API, the page shows the waking message and, after 90 seconds, a retry button.

| Command | What it does |
|---|---|
| `npm run lint` | ESLint with type-aware rules; warnings fail |
| `npm run typecheck` | `tsc` in strict mode |
| `npm test` | Vitest and React Testing Library |
| `npm run build` | Production build into `web/dist` |
| `npm run e2e` | Playwright in Chromium and WebKit against the production build. Run `npx playwright install chromium webkit` once. API calls are stubbed. |
| `bash scripts/run-e2e-fullstack.sh` | Sign-in, reload, new tab and sign-out against a real API and a throwaway PostgreSQL container (Docker). Run from the repository root. |

### API contract

The web app's API types come from the API's OpenAPI document. Both are committed:

- `web/openapi/v1.json`: the document.
- `web/src/api/schema.d.ts`: types generated from it by `openapi-typescript`.

After changing a controller or DTO, regenerate both and commit them:

```bash
bash scripts/generate-api-contract.sh   # Git Bash on Windows; needs `npm ci` in web/ first
```

The script builds the API in Release and runs it briefly on `127.0.0.1:5290` in a `ContractGeneration` environment with throwaway settings: user secrets are not loaded and no database is contacted. CI runs the same script and fails if the result differs from the committed files.

## Running tests

```powershell
# Everything (Docker must be running)
dotnet test ResuMatch.slnx

# Unit tests only (no Docker needed)
dotnet test ResuMatch.slnx --filter "FullyQualifiedName!~API.Tests.Integration"

# Integration tests only
dotnet test ResuMatch.slnx --filter "FullyQualifiedName~API.Tests.Integration"
```

Integration tests live in the `API.Tests.Integration.*` namespace. They start a disposable PostgreSQL container, run serially in one xUnit collection, and host the API in memory with `WebApplicationFactory`. A few tests also start the API on real Kestrel on a dynamic loopback port.

## What CI checks

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every push and pull request to `main` and on manual dispatch. A newer run on the same pull request cancels the older one; runs on `main` are never cancelled.

- **build-and-unit-tests**
  - Restores packages and the `dotnet-ef` local tool, then builds in Release with warnings treated as errors.
  - Runs the unit tests.
  - Fails if the EF Core model has changes without a migration (`dotnet ef migrations has-pending-model-changes`).
  - Fails if any direct or transitive package has a known vulnerability.
- **integration-tests**: runs after the first job and executes the integration tests against PostgreSQL in Docker (Testcontainers).
- **web-checks**: `npm ci`, lint, typecheck, unit tests and build of `web/`, then `npm audit --omit=dev` for runtime packages.
- **api-contract**: builds the API, regenerates the OpenAPI document and the TypeScript types, and fails if they differ from the committed files.

The two web jobs run in parallel with the backend jobs and do not gate `deploy`, which still deploys only the API.

- **deploy**: on pushes to `main` only, after both jobs pass. Applies migrations to Neon, deploys to Render, and runs a read-only smoke test. See [Render + Neon](docs/operations/deployment.md#render--neon).

Both test jobs upload their `.trx` results as artifacts, even when tests fail. The test jobs need no secrets: tests use built-in test keys and fake providers. Dependabot proposes weekly NuGet, npm (`web/`) and GitHub Actions updates, grouping the Microsoft and EF Core packages into one PR and the npm packages by purpose.

## Project structure

```
API/
  Controllers/      HTTP endpoints (auth, resumes and sections, job postings, parse drafts, analyses, applications)
  Application/      Use-case services, the ATS analysis engine, and the job parsing pipeline
  Data/             AppDbContext and EF Core entity configurations
  Entities/         Domain entities and enums
  DTOs/             Request and response contracts
  Infrastructure/   Authentication, security middleware, OpenAI provider, observability, exception handling
  Migrations/       EF Core migrations
API.Tests/
  Analysis/, JobParsing/, Observability/, Security/   Unit tests
  Integration/                                        PostgreSQL-backed and HTTP integration tests
web/
  openapi/          Committed OpenAPI document (generated)
  src/api/          Generated types, API configuration, cold start check
  src/components/   Shared components
  e2e/              Playwright tests
scripts/            Smoke test, API contract generation
docs/operations/    Production operations documentation
```

## Operations documentation

- [API error codes](docs/api/error-codes.md): stable error codes and the enum contract
- [Authentication and sessions](docs/api/authentication.md): access tokens, sessions, refresh token rotation
- [Skill catalog](docs/api/skill-catalog.md): catalog source, search behavior and the unlisted skills note
- [Deployment contract](docs/operations/deployment.md): probes, migrations, canary, rollback, HTTP security and proxy settings
- [Runbooks](docs/operations/runbooks.md)
- [SLOs](docs/operations/slo.md)
- [Alerts](docs/operations/alerts.md)
- [Staging smoke test](docs/operations/staging-smoke-test.md)
- [Change and merge policy](docs/operations/change-and-merge-policy.md)

`API/appsettings.Production.example.json` documents the production configuration shape with placeholder values; inject real secrets from a secret manager.
