using System.Security.Claims;
using System.Threading.RateLimiting;
using Api.Application.Analysis;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Queries;
using Api.Application.Auth;
using Api.Application.Certifications;
using Api.Application.Common.Interfaces;
using Api.Application.Educations;
using Api.Application.Experiences;
using Api.Application.Languages;
using Api.Application.JobPostings;
using Api.Application.JobApplications;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Protection;
using Api.Application.JobParsing.Engine;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.Projects;
using Api.Application.ResumeContacts;
using Api.Application.ResumeLinks;
using Api.Application.Resumes;
using Api.Application.Skills;
using Api.Data;
using Api.Infrastructure.Authentication;
using Api.Infrastructure.ExceptionHandling;
using Api.Infrastructure.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using Api.Infrastructure.JobParsing.OpenAI.Contracts;
using Api.Infrastructure.Observability;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var requestLimitSection = builder.Configuration.GetSection(
    RequestLimitOptions.SectionName);
var requestLimitSettings = requestLimitSection.Get<RequestLimitOptions>() ??
    new RequestLimitOptions();
var requestLimitValidation = new RequestLimitOptionsValidator().Validate(
    Options.DefaultName,
    requestLimitSettings);
if (requestLimitValidation.Failed)
{
    throw new InvalidOperationException(string.Join(
        " ",
        requestLimitValidation.Failures));
}

var httpSecuritySection = builder.Configuration.GetSection(
    HttpSecurityOptions.SectionName);
var httpSecuritySettings = httpSecuritySection.Get<HttpSecurityOptions>() ??
    new HttpSecurityOptions();
var httpSecurityValidation = new HttpSecurityOptionsValidator().Validate(
    Options.DefaultName,
    httpSecuritySettings);
if (httpSecurityValidation.Failed)
{
    throw new InvalidOperationException(string.Join(
        " ",
        httpSecurityValidation.Failures));
}

var productionSecurityFailures = ProductionSecurityPolicy.Validate(
    builder.Environment.IsProduction(),
    builder.Configuration["AllowedHosts"],
    httpSecuritySettings);
if (productionSecurityFailures.Count > 0)
{
    throw new InvalidOperationException(string.Join(
        " ",
        productionSecurityFailures));
}

var authenticationRateLimitSection = builder.Configuration.GetSection(
    AuthenticationRateLimitOptions.SectionName);
var authenticationRateLimitSettings = authenticationRateLimitSection
    .Get<AuthenticationRateLimitOptions>() ??
    new AuthenticationRateLimitOptions();
var authenticationRateLimitValidation =
    new AuthenticationRateLimitOptionsValidator().Validate(
        Options.DefaultName,
        authenticationRateLimitSettings);
if (authenticationRateLimitValidation.Failed)
{
    throw new InvalidOperationException(string.Join(
        " ",
        authenticationRateLimitValidation.Failures));
}

builder.Services.AddOptions<RequestLimitOptions>()
    .Bind(requestLimitSection)
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<RequestLimitOptions>,
    RequestLimitOptionsValidator>();
builder.Services.AddOptions<HttpSecurityOptions>()
    .Bind(httpSecuritySection)
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<HttpSecurityOptions>,
    HttpSecurityOptionsValidator>();
builder.Services.AddOptions<AuthenticationRateLimitOptions>()
    .Bind(authenticationRateLimitSection)
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<AuthenticationRateLimitOptions>,
    AuthenticationRateLimitOptionsValidator>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize =
        requestLimitSettings.MaxRequestBodySizeBytes;
    options.Limits.MaxRequestHeadersTotalSize =
        requestLimitSettings.MaxRequestHeadersTotalSize;
    options.Limits.MaxRequestLineSize = requestLimitSettings.MaxRequestLineSize;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = httpSecuritySettings.ForwardLimit;
    options.ForwardedForHeaderName = httpSecuritySettings.ForwardedForHeaderName;

    // A dedicated client-IP header always has one value, so it cannot be paired
    // with X-Forwarded-Proto entry by entry.
    options.RequireHeaderSymmetry = string.Equals(
        httpSecuritySettings.ForwardedForHeaderName,
        ForwardedHeadersDefaults.XForwardedForHeaderName,
        StringComparison.OrdinalIgnoreCase);
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in httpSecuritySettings.TrustedProxies)
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }

    foreach (var network in httpSecuritySettings.TrustedNetworks)
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

builder.Services.AddCors(options => options.AddPolicy(
    "ConfiguredOrigins",
    policy =>
    {
        if (httpSecuritySettings.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(httpSecuritySettings.AllowedOrigins);
        }
        else
        {
            policy.SetIsOriginAllowed(_ => false);
        }

        policy.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "If-Match")
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
        if (httpSecuritySettings.AllowCredentials)
        {
            policy.AllowCredentials();
        }
    }));

builder.Services.AddHsts(options =>
{
    options.MaxAge = httpSecuritySettings.HstsMaxAge;
    options.IncludeSubDomains = httpSecuritySettings.HstsIncludeSubDomains;
    options.Preload = httpSecuritySettings.HstsPreload;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found or is empty. " +
        "Configure 'ConnectionStrings:DefaultConnection'.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var observabilitySection = builder.Configuration.GetSection(
    ObservabilityOptions.SectionName);
var observabilitySettings = observabilitySection.Get<ObservabilityOptions>() ??
    new ObservabilityOptions();
var observabilityValidation = new ObservabilityOptionsValidator().Validate(
    Options.DefaultName,
    observabilitySettings);
if (observabilityValidation.Failed)
{
    throw new InvalidOperationException(string.Join(
        " ",
        observabilityValidation.Failures));
}

builder.Services.AddOptions<ObservabilityOptions>()
    .Bind(observabilitySection)
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<ObservabilityOptions>,
    ObservabilityOptionsValidator>();

if (observabilitySettings.Enabled)
{
    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ??
        "unknown";
    var telemetry = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName: "ResuMatch.Api",
                serviceVersion: serviceVersion)
            .AddAttributes(
            [
                new KeyValuePair<string, object>(
                    "deployment.environment",
                    builder.Environment.EnvironmentName)
            ]));

    if (observabilitySettings.TracingEnabled)
    {
        telemetry.WithTracing(tracing =>
        {
            tracing
                .AddSource(JobParsingTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
            if (observabilitySettings.OtlpEnabled &&
                ObservabilityOptionsValidator.TryGetValidOtlpEndpoint(
                    observabilitySettings.OtlpEndpoint,
                    out var endpoint))
            {
                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = endpoint!;
                    options.TimeoutMilliseconds = checked((int)Math.Ceiling(
                        observabilitySettings.OtlpExportTimeout.TotalMilliseconds));
                });
            }
        });
    }

    if (observabilitySettings.MetricsEnabled)
    {
        telemetry.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(JobPostingParseDraftMetrics.MeterName)
                .AddMeter(ApiSecurityMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
            if (observabilitySettings.OtlpEnabled &&
                ObservabilityOptionsValidator.TryGetValidOtlpEndpoint(
                    observabilitySettings.OtlpEndpoint,
                    out var endpoint))
            {
                metrics.AddOtlpExporter(options =>
                {
                    options.Endpoint = endpoint!;
                    options.TimeoutMilliseconds = checked((int)Math.Ceiling(
                        observabilitySettings.OtlpExportTimeout.TotalMilliseconds));
                });
            }
        });
    }
}

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{JwtOptions.SectionName}' was not found.");

ValidateJwtConfiguration(jwtOptions);
var jwtSigningKey = new SymmetricSecurityKey(
    JwtSigningKeyMaterial.Decode(jwtOptions.SecretKey));

builder.Services.AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer),
        "JWT issuer must be configured.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Audience),
        "JWT audience must be configured.")
    .Validate(
        options => JwtSigningKeyMaterial.IsValid(options.SecretKey),
        "JWT secret key must be valid Base64 that decodes to at least 32 bytes. " +
        "Generate it with a cryptographically secure random source.")
    .Validate(
        options => options.AccessTokenExpirationMinutes is > 0 and
            <= JwtOptions.MaximumAccessTokenExpirationMinutes,
        $"JWT access token expiration must be between 1 and " +
        $"{JwtOptions.MaximumAccessTokenExpirationMinutes} minutes.")
    .ValidateOnStart();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IAnalysisEngine, AnalysisEngineV1>();
builder.Services.AddSingleton<IAnalyzerRegistry, AnalyzerRegistry>();
builder.Services.AddScoped<AnalysisInputLoader>();
builder.Services.AddScoped<IResumeAnalysisService, ResumeAnalysisService>();
builder.Services.AddScoped<ICertificationService, CertificationService>();
builder.Services.AddScoped<IEducationService, EducationService>();
builder.Services.AddScoped<IExperienceService, ExperienceService>();
builder.Services.AddScoped<ILanguageService, LanguageService>();
builder.Services.AddScoped<IJobPostingService, JobPostingService>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IResumeContactService, ResumeContactService>();
builder.Services.AddScoped<IResumeLinkService, ResumeLinkService>();
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddScoped<ISkillService, SkillService>();

builder.Services.AddOptions<JobPostingParseDraftOptions>()
    .Bind(builder.Configuration.GetSection(
        JobPostingParseDraftOptions.SectionName))
    .ValidateOnStart();
var parseDraftSettings = builder.Configuration
    .GetSection(JobPostingParseDraftOptions.SectionName)
    .Get<JobPostingParseDraftOptions>() ?? new JobPostingParseDraftOptions();
builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStopConcurrently = true;
    options.ShutdownTimeout = parseDraftSettings.ShutdownGracePeriod +
        TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<
    IValidateOptions<JobPostingParseDraftOptions>,
    JobPostingParseDraftOptionsValidator>();
var extractionSection = builder.Configuration.GetSection(
    OpenAiJobDescriptionExtractionOptions.SectionName);
var extractionSettings = extractionSection
    .Get<OpenAiJobDescriptionExtractionOptions>() ??
    new OpenAiJobDescriptionExtractionOptions();
if (builder.Environment.IsProduction() &&
    parseDraftSettings.WorkerEnabled &&
    parseDraftSettings.ProviderExecutionEnabled &&
    extractionSettings.Provider == JobDescriptionExtractionProviderKind.OpenAI &&
    (!extractionSettings.RequirePinnedModel || !extractionSettings.ModelIsPinned))
{
    throw new InvalidOperationException(
        "Production provider execution requires an explicit pinned-model policy and a model marked as pinned.");
}
builder.Services.AddOptions<OpenAiJobDescriptionExtractionOptions>()
    .Bind(extractionSection)
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<OpenAiJobDescriptionExtractionOptions>,
    OpenAiJobDescriptionExtractionOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JobDescriptionPreprocessor>();
builder.Services.AddSingleton<ProviderJsonSchemaValidator>();
builder.Services.AddSingleton<EvidenceValidator>();
builder.Services.AddSingleton<ProviderResponseValidator>();
builder.Services.AddSingleton<JobDescriptionParseResultValidator>();
builder.Services.AddSingleton<ISkillCanonicalizer, SkillCanonicalizerV1>();
builder.Services.AddSingleton<ParsedSkillDuplicateResolverV1>();
var extractionProvider = extractionSettings.Provider;
switch (extractionProvider)
{
    case JobDescriptionExtractionProviderKind.Unavailable:
        builder.Services.AddSingleton<
            IJobDescriptionExtractionProvider,
            UnavailableJobDescriptionExtractionProvider>();
        break;

    case JobDescriptionExtractionProviderKind.OpenAI:
        builder.Services.AddSingleton<ProviderFacingResponseJsonValidator>();
        builder.Services.AddSingleton<EvidenceOffsetResolver>();
        builder.Services.AddSingleton<ProviderExtractionPayloadBuilder>();
        builder.Services.AddHttpClient(
            OpenAiJobDescriptionExtractionProvider.HttpClientName,
            client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });
        builder.Services.AddSingleton<IJobDescriptionExtractionProvider>(
            serviceProvider => new OpenAiJobDescriptionExtractionProvider(
                serviceProvider
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(
                        OpenAiJobDescriptionExtractionProvider.HttpClientName),
                serviceProvider.GetRequiredService<
                    IOptions<OpenAiJobDescriptionExtractionOptions>>(),
                serviceProvider.GetRequiredService<
                    ProviderFacingResponseJsonValidator>(),
                serviceProvider.GetRequiredService<
                    ProviderExtractionPayloadBuilder>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<
                    OpenAiJobDescriptionExtractionProvider>>()));
        break;

    default:
        throw new InvalidOperationException(
            "The configured job description extraction provider is invalid.");
}
builder.Services.AddSingleton<IJobDescriptionParser, JobDescriptionParserV1>();
builder.Services.AddSingleton<IJobParserRegistry, JobParserRegistry>();
builder.Services.AddScoped<
    IJobPostingParseDraftService,
    JobPostingParseDraftService>();
builder.Services.AddScoped<
    IJobPostingParseDraftClaimService,
    JobPostingParseDraftClaimService>();
builder.Services.AddScoped<
    IJobPostingParseDraftExecutionService,
    JobPostingParseDraftExecutionService>();
builder.Services.AddScoped<
    IJobPostingParseDraftPurgeService,
    JobPostingParseDraftPurgeService>();
builder.Services.AddOptions<AiUsageBudgetOptions>()
    .Bind(builder.Configuration.GetSection(AiUsageBudgetOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<AiUsageBudgetOptions>,
    AiUsageBudgetOptionsValidator>();
var aiUsageBudgetSettings = builder.Configuration
    .GetSection(AiUsageBudgetOptions.SectionName)
    .Get<AiUsageBudgetOptions>() ?? new AiUsageBudgetOptions();
if (aiUsageBudgetSettings.Enabled &&
    aiUsageBudgetSettings.Reservation.MaximumOutputTokens <
        extractionSettings.MaxOutputTokens)
{
    throw new InvalidOperationException(
        "AI usage budget MaximumOutputTokens must be greater than or equal " +
        "to the extraction provider MaxOutputTokens when enforcement is enabled.");
}
if (extractionSettings.Provider == JobDescriptionExtractionProviderKind.OpenAI &&
    extractionSettings.MaxConcurrentRequests <
        parseDraftSettings.DistributedMaxConcurrentRequests)
{
    throw new InvalidOperationException(
        "The process-local MaxConcurrentRequests cannot be lower than " +
        "DistributedMaxConcurrentRequests.");
}
builder.Services.AddScoped<IAiUsageBudgetService, AiUsageBudgetService>();
builder.Services.AddScoped<IAiProviderConcurrencyService, AiProviderConcurrencyService>();
builder.Services.AddScoped<IAiProviderCircuitBreaker, AiProviderCircuitBreaker>();
builder.Services.AddScoped<
    IAiUsageReservationReconciliationService,
    AiUsageReservationReconciliationService>();
builder.Services.AddHostedService<JobPostingParseDraftBackgroundService>();
builder.Services.AddHostedService<JobPostingParseDraftPurgeBackgroundService>();
builder.Services.AddHostedService<AiUsageReservationReconciliationBackgroundService>();
builder.Services.AddHostedService<AiUsageLedgerPurgeBackgroundService>();
builder.Services.AddHostedService<JobParsingOperationalSampler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?
                    .FindFirst(ClaimTypes.NameIdentifier)?
                    .Value;

                if (!Guid.TryParse(userIdClaim, out var userId) || userId == Guid.Empty)
                {
                    context.Fail("Token validation failed.");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices
                    .GetRequiredService<AppDbContext>();

                var isActiveUser = await dbContext.Users
                    .AsNoTracking()
                    .AnyAsync(
                        user => user.Id == userId && user.IsActive,
                        context.HttpContext.RequestAborted);

                if (!isActiveUser)
                {
                    context.Fail("Token validation failed.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        TimeSpan? retryAfter = null;
        if (context.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var leaseRetryAfter))
        {
            retryAfter = leaseRetryAfter;
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling(leaseRetryAfter.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var policy = RateLimitPolicyResolver.Resolve(context.HttpContext.Request.Path);
        ApiSecurityMetrics.RateLimited.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy));
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Api.RateLimiting");
        logger.LogWarning(
            "Request rate limit exceeded. Policy={Policy} RetryAfterSeconds={RetryAfterSeconds}",
            policy,
            retryAfter is null ? null : Math.Ceiling(retryAfter.Value.TotalSeconds));

        context.HttpContext.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "The request rate limit was exceeded.",
                Instance = context.HttpContext.Request.Path
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    };
    options.AddPolicy(
        AuthenticationRateLimitPolicyNames.Login,
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authenticationRateLimitSettings.LoginPermitLimit,
                Window = authenticationRateLimitSettings.LoginWindow,
                QueueLimit = authenticationRateLimitSettings.QueueLimit,
                AutoReplenishment = true
            }));

    options.AddPolicy(
        AuthenticationRateLimitPolicyNames.Register,
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authenticationRateLimitSettings.RegisterPermitLimit,
                Window = authenticationRateLimitSettings.RegisterWindow,
                QueueLimit = authenticationRateLimitSettings.QueueLimit,
                AutoReplenishment = true
            }));

    options.AddPolicy<string>(
        JobParsingRateLimitPolicyNames.RequestParse,
        httpContext =>
        {
            if (!parseDraftSettings.ParseRateLimitEnabled)
            {
                return RateLimitPartition.GetNoLimiter("parse-disabled");
            }

            var userId = httpContext.User
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;
            if (!Guid.TryParse(userId, out var parsedUserId) ||
                parsedUserId == Guid.Empty)
            {
                return RateLimitPartition.GetNoLimiter("unauthenticated");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: parsedUserId.ToString("N"),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = parseDraftSettings.ParseRequestsPerWindow,
                    Window = parseDraftSettings.ParseRateLimitWindow,
                    QueueLimit = parseDraftSettings.ParseRateLimitQueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton<ISchemaCompatibilityChecker, EfSchemaCompatibilityChecker>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "process",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<DatabaseReadinessHealthCheck>(
        "database",
        tags: ["ready"])
    .AddCheck<SchemaCompatibilityHealthCheck>(
        "schema",
        tags: ["ready"])
    .AddCheck<ConfigurationHealthCheck>(
        "configuration",
        tags: ["ready"])
    .AddCheck<JobParserOperationalHealthCheck>(
        "job-parser",
        tags: ["ready"]);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.MaxDepth = requestLimitSettings.JsonMaxDepth;
        options.JsonSerializerOptions.AllowDuplicateProperties = false;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .Take(50)
            .ToDictionary(
                entry => string.IsNullOrWhiteSpace(entry.Key)
                    ? "request"
                    : entry.Key.Length <= 128
                        ? entry.Key
                        : entry.Key[..128],
                _ => new[] { "The supplied value is invalid." },
                StringComparer.Ordinal);
        if (errors.Count == 0)
        {
            errors["request"] = ["The request is invalid."];
        }

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ??
            context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (httpSecuritySettings.TrustedProxies.Length > 0 ||
    httpSecuritySettings.TrustedNetworks.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsProduction() && httpSecuritySettings.HstsEnabled)
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseMiddleware<RequestSecurityMiddleware>();

if (app.Environment.IsDevelopment() || httpSecuritySettings.ExposeOpenApi)
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "API";
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseCors("ConfiguredOrigins");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapControllers();

app.Run();

static void ValidateJwtConfiguration(JwtOptions options)
{
    if (string.IsNullOrWhiteSpace(options.Issuer))
    {
        throw new InvalidOperationException("JWT issuer must be configured.");
    }

    if (string.IsNullOrWhiteSpace(options.Audience))
    {
        throw new InvalidOperationException("JWT audience must be configured.");
    }

    if (!JwtSigningKeyMaterial.IsValid(options.SecretKey))
    {
        throw new InvalidOperationException(
            "JWT secret key must be valid Base64 that decodes to at least 32 bytes. " +
            "Generate it with a cryptographically secure random source.");
    }

    if (options.AccessTokenExpirationMinutes is <= 0 or
        > JwtOptions.MaximumAccessTokenExpirationMinutes)
    {
        throw new InvalidOperationException(
            $"JWT access token expiration must be between 1 and " +
            $"{JwtOptions.MaximumAccessTokenExpirationMinutes} minutes.");
    }
}

public partial class Program
{
}
