using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Api.Application.Common.Exceptions;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Infrastructure.ExceptionHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private const string NormalizedEmailIndexName = "IX_Users_NormalizedEmail";
    private const string ResumeSkillUniqueIndexName =
        "IX_ResumeSkills_ResumeId_SkillId";
    private const string ProjectSkillUniqueIndexName =
        "IX_ProjectSkills_ResumeProjectId_SkillId";
    private const string JobPostingSkillUniqueIndexName =
        "IX_JobPostingSkills_JobPostingId_SkillId";
    private const string JobPostingSkillSourceCandidateUniqueIndexName =
        "IX_JobPostingSkills_SourceParseDraftSkillId";
    private const string DuplicateEmailMessage =
        "Registration could not be completed with the supplied information.";
    private const string DuplicateResumeSkillMessage =
        "The skill is already added to this resume.";
    private const string DuplicateProjectSkillMessage =
        "The skill is already added to this project.";
    private const string DuplicateJobPostingSkillMessage =
        "The skill is already associated with this job posting.";

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Kestrel enforces the server-wide body limit itself while the body is
        // read; answer exactly like RequestSecurityMiddleware does.
        if (exception is BadHttpRequestException
            {
                StatusCode: StatusCodes.Status413PayloadTooLarge
            })
        {
            _logger.LogWarning(
                "Request {Method} {Path} failed with status code {StatusCode} and exception type {ExceptionType}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                StatusCodes.Status413PayloadTooLarge,
                exception.GetType().Name);
            await RequestSecurityMiddleware.WriteBodyTooLargeAsync(httpContext);
            return true;
        }

        var problemDetails = CreateProblemDetails(httpContext, exception);

        if (exception is TooManyRequestsException { RetryAfter: { } retryAfter })
        {
            httpContext.Response.Headers.RetryAfter = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (problemDetails.Status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "An unhandled exception occurred while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Request {Method} {Path} failed with status code {StatusCode} and exception type {ExceptionType}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                problemDetails.Status,
                exception.GetType().Name);
        }

        httpContext.Response.StatusCode = problemDetails.Status
            ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        Exception exception)
    {
        var problemDetails = exception switch
        {
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Authentication is required or the supplied credentials are invalid.",
                Extensions = { ["code"] = ApiErrorCodes.Unauthorized }
            },

            ConflictException conflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflictException.Message,
                Extensions = { ["code"] = conflictException.Code }
            },

            NotFoundException notFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = notFoundException.Message,
                Extensions = { ["code"] = ApiErrorCodes.NotFound }
            },

            PreconditionRequiredException preconditionRequiredException =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status428PreconditionRequired,
                    Title = "Precondition Required",
                    Detail = preconditionRequiredException.Message,
                    Extensions = { ["code"] = ApiErrorCodes.PreconditionRequired }
                },

            TooManyRequestsException tooManyRequestsException =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = tooManyRequestsException.Message,
                    Extensions = { ["code"] = tooManyRequestsException.Code }
                },

            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status412PreconditionFailed,
                Title = "Precondition Failed",
                Detail = "The resource was modified by another operation. " +
                         "Reload it and retry with the latest ETag.",
                Extensions = { ["code"] = ApiErrorCodes.PreconditionFailed }
            },

            DbUpdateException dbUpdateException when IsDuplicateEmailViolation(dbUpdateException) =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = DuplicateEmailMessage,
                    Extensions = { ["code"] = ApiErrorCodes.RegistrationRejected }
                },

            DbUpdateException dbUpdateException when IsResumeSkillDuplicate(dbUpdateException) =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = DuplicateResumeSkillMessage,
                    Extensions = { ["code"] = ApiErrorCodes.DuplicateSkill }
                },

            DbUpdateException dbUpdateException when IsProjectSkillDuplicate(dbUpdateException) =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = DuplicateProjectSkillMessage,
                    Extensions = { ["code"] = ApiErrorCodes.DuplicateSkill }
                },

            DbUpdateException dbUpdateException when IsJobPostingSkillDuplicate(dbUpdateException) =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = DuplicateJobPostingSkillMessage,
                    Extensions = { ["code"] = ApiErrorCodes.DuplicateSkill }
                },

            // Raised by the server for malformed requests (bad chunked framing,
            // timeouts, oversized headers). Use its status code but never its
            // message, which can echo request details.
            BadHttpRequestException badHttpRequestException =>
                CreateBadRequestProblemDetails(badHttpRequestException.StatusCode),

            ValidationException validationException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = validationException.Message,
                Extensions = { ["code"] = ApiErrorCodes.ValidationFailed }
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred while processing the request.",
                Extensions = { ["code"] = ApiErrorCodes.InternalError }
            }
        };

        problemDetails.Instance = httpContext.Request.Path;
        problemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? httpContext.TraceIdentifier;

        return problemDetails;
    }

    private static ProblemDetails CreateBadRequestProblemDetails(int statusCode)
    {
        // BadHttpRequestException always describes a client error; anything
        // outside 4xx would be a server bug, so fall back to a plain 400.
        var status = statusCode is >= 400 and < 500
            ? statusCode
            : StatusCodes.Status400BadRequest;
        var title = ReasonPhrases.GetReasonPhrase(status);

        return new ProblemDetails
        {
            Status = status,
            Title = string.IsNullOrEmpty(title) ? "Bad Request" : title,
            Detail = "The request could not be processed because it was malformed or incomplete.",
            Extensions = { ["code"] = ApiErrorCodes.MalformedRequest }
        };
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, NormalizedEmailIndexName);
    }

    private static bool IsResumeSkillDuplicate(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, ResumeSkillUniqueIndexName);
    }

    private static bool IsProjectSkillDuplicate(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, ProjectSkillUniqueIndexName);
    }

    private static bool IsJobPostingSkillDuplicate(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, JobPostingSkillUniqueIndexName) ||
               IsUniqueViolation(
                   exception,
                   JobPostingSkillSourceCandidateUniqueIndexName);
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               postgresException.ConstraintName == constraintName;
    }
}
