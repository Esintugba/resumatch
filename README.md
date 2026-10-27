# ResuMatch

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

The job description parser is disabled by default (`JobDescriptionExtraction:Provider` is `Unavailable` and the parse worker is off). To use OpenAI, set `JobDescriptionExtraction:Provider` to `OpenAI` and provide `JobDescriptionExtraction:ApiKey` and `JobDescriptionExtraction:Model`; the validator rejects an OpenAI provider without both.

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
docs/operations/    Production operations documentation
```

## Operations documentation

- [Deployment contract](docs/operations/deployment.md): probes, migrations, canary, rollback, HTTP security and proxy settings
- [Runbooks](docs/operations/runbooks.md)
- [SLOs](docs/operations/slo.md)
- [Alerts](docs/operations/alerts.md)
- [Staging smoke test](docs/operations/staging-smoke-test.md)

`API/appsettings.Production.example.json` documents the production configuration shape with placeholder values; inject real secrets from a secret manager.
