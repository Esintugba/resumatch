using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftClaimService :
    IJobPostingParseDraftClaimService
{
    private const int MaximumLeaseOwnerLength = 200;
    private const int MaximumFailureCodeLength = 100;
    public const string UserBudgetExhaustedCode = "user_budget_exhausted";
    public const string SystemBudgetExhaustedCode = "system_budget_exhausted";
    public const string ProviderCapacityUnavailableCode =
        "provider_capacity_unavailable";
    public const string ProviderCircuitOpenCode = "provider_circuit_open";

    private static readonly HashSet<string> SafeFailureCodes =
        new(StringComparer.Ordinal)
        {
            JobParseFailureClassifier.ProviderTimeoutCode,
            JobParseFailureClassifier.ProviderUnavailableCode,
            JobParseFailureClassifier.ProviderRateLimitedCode,
            JobParseFailureClassifier.ProviderConfigurationErrorCode,
            JobParseFailureClassifier.ProviderRefusedCode,
            JobParseFailureClassifier.InvalidProviderResponseCode,
            JobParseFailureClassifier.SchemaValidationFailedCode,
            JobParseFailureClassifier.StaleSourceCode,
            JobParseFailureClassifier.RetryExhaustedCode,
            JobParseFailureClassifier.UnexpectedFailureCode
        };

    private static readonly HashSet<string> RetryableFailureCodes =
        new(StringComparer.Ordinal)
        {
            JobParseFailureClassifier.ProviderTimeoutCode,
            JobParseFailureClassifier.ProviderUnavailableCode,
            JobParseFailureClassifier.ProviderRateLimitedCode
        };

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;
    private readonly int _maximumAttempts;
    private readonly bool _providerExecutionEnabled;
    private readonly IOptionsMonitor<JobPostingParseDraftOptions>? _optionsMonitor;

    public JobPostingParseDraftClaimService(
        AppDbContext dbContext,
        IOptions<JobPostingParseDraftOptions> options,
        TimeProvider timeProvider,
        IOptionsMonitor<JobPostingParseDraftOptions>? optionsMonitor = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));

        ArgumentNullException.ThrowIfNull(options);
        _leaseDuration = options.Value.LeaseDuration;
        _maximumAttempts = options.Value.MaximumAttempts;
        _providerExecutionEnabled = options.Value.ProviderExecutionEnabled;
        _optionsMonitor = optionsMonitor;

        if (_leaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Job posting parse draft lease duration must be greater than zero.");
        }

        if (_maximumAttempts < 1)
        {
            throw new InvalidOperationException(
                "Job posting parse draft maximum attempts must be greater than zero.");
        }
    }

    public async Task<JobPostingParseDraftClaim?> ClaimNextAsync(
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        ValidateLeaseOwner(leaseOwner);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsProviderExecutionEnabled())
        {
            return null;
        }

        var claimedAt = GetUtcNow();
        var leaseExpiresAt = claimedAt.Add(_leaseDuration);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            WITH candidate AS
            (
                SELECT draft."Id", draft."Status" AS "PreviousStatus"
                FROM "JobPostingParseDrafts" AS draft
                WHERE
                    (
                    (
                        draft."Status" = @pendingStatus
                        AND
                        (
                            draft."NextAttemptAt" IS NULL
                            OR draft."NextAttemptAt" <= @claimedAt
                        )
                    )
                    OR
                    (
                        draft."Status" = @processingStatus
                        AND draft."LeaseExpiresAt" <= @claimedAt
                    )
                    )
                AND draft."AttemptCount" < @maximumAttempts
                ORDER BY draft."RequestedAt" ASC, draft."Id" ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE "JobPostingParseDrafts" AS draft
            SET "Status" = @processingStatus,
                "LeaseOwner" = @leaseOwner,
                "LeaseExpiresAt" = @leaseExpiresAt,
                "LastAttemptAt" = @claimedAt,
                "NextAttemptAt" = NULL,
                "AttemptCount" = draft."AttemptCount" + 1,
                "UpdatedAt" = @claimedAt
            FROM candidate
            WHERE draft."Id" = candidate."Id"
            RETURNING
                draft."Id",
                draft."JobPostingId",
                draft."LeaseOwner",
                draft."AttemptCount",
                draft.xmin,
                draft."LastAttemptAt",
                draft."LeaseExpiresAt",
                draft."ParserVersion",
                candidate."PreviousStatus";
            """;

        AddParameter(command, "pendingStatus",
            (int)JobPostingParseDraftStatus.Pending);
        AddParameter(command, "processingStatus",
            (int)JobPostingParseDraftStatus.Processing);
        AddParameter(command, "claimedAt", claimedAt);
        AddParameter(command, "leaseExpiresAt", leaseExpiresAt);
        AddParameter(command, "leaseOwner", leaseOwner);
        AddParameter(command, "maximumAttempts", _maximumAttempts);

        JobPostingParseDraftClaim? claim = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                claim = new JobPostingParseDraftClaim(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetFieldValue<uint>(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetInt32(8) ==
                        (int)JobPostingParseDraftStatus.Processing);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        if (claim is not null)
        {
            JobPostingParseDraftMetrics.Claimed.Add(1);
        }

        return claim;
    }

    private bool IsProviderExecutionEnabled() =>
        _optionsMonitor?.CurrentValue.ProviderExecutionEnabled ??
        _providerExecutionEnabled;

    public async Task<JobPostingParseDraftClaim> RenewLeaseAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken)
    {
        var now = GetUtcNow();
        var draft = await LoadValidClaimAsync(claim, now, cancellationToken);
        var currentExpiry = draft.LeaseExpiresAt!.Value;
        var renewedExpiry = currentExpiry.Add(_leaseDuration);

        draft.LeaseExpiresAt = renewedExpiry;
        draft.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (draft.Status == JobPostingParseDraftStatus.Failed)
        {
            JobPostingParseDraftMetrics.Failed.Add(1);
        }
        else
        {
            JobPostingParseDraftMetrics.Retried.Add(1);
        }

        return ToClaim(draft);
    }

    public async Task ReleaseForRetryAsync(
        JobPostingParseDraftClaim claim,
        JobParseFailureClassification failure,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ValidateFailure(failure);
        if (!failure.IsRetryable)
        {
            throw new ValidationException(
                "A non-retryable failure cannot be released for retry.");
        }

        var now = GetUtcNow();
        ValidateNextAttempt(nextAttemptAt, now);
        var draft = await LoadValidClaimAsync(claim, now, cancellationToken);

        if (draft.AttemptCount >= _maximumAttempts)
        {
            ApplyFailed(
                draft,
                JobParseFailureClassifier.RetryExhaustedCode,
                now);
        }
        else
        {
            EnsureTransition(draft.Status, JobPostingParseDraftStatus.Pending);
            draft.Status = JobPostingParseDraftStatus.Pending;
            draft.FailureCode = failure.FailureCode;
            draft.LeaseOwner = null;
            draft.LeaseExpiresAt = null;
            draft.NextAttemptAt = nextAttemptAt;
            draft.CompletedAt = null;
            draft.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        JobPostingParseDraftClaim claim,
        JobParseFailureClassification failure,
        CancellationToken cancellationToken)
    {
        ValidateFailure(failure);
        var now = GetUtcNow();
        var draft = await LoadValidClaimAsync(claim, now, cancellationToken);

        ApplyFailed(draft, failure.FailureCode, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        JobPostingParseDraftMetrics.Failed.Add(1);
    }

    public async Task ReleaseForBudgetWaitAsync(
        JobPostingParseDraftClaim claim,
        string failureCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (failureCode is not UserBudgetExhaustedCode and
            not SystemBudgetExhaustedCode)
        {
            throw new ValidationException("The budget failure code is invalid.");
        }

        var now = GetUtcNow();
        ValidateNextAttempt(nextAttemptAt, now);
        var draft = await LoadValidClaimAsync(claim, now, cancellationToken);
        EnsureTransition(draft.Status, JobPostingParseDraftStatus.Pending);
        draft.Status = JobPostingParseDraftStatus.Pending;
        draft.AttemptCount = Math.Max(0, draft.AttemptCount - 1);
        draft.FailureCode = failureCode;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = nextAttemptAt;
        draft.CompletedAt = null;
        draft.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task ReleaseForInfrastructureWaitAsync(
        JobPostingParseDraftClaim claim,
        string failureCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (failureCode is not ProviderCapacityUnavailableCode and
            not ProviderCircuitOpenCode)
        {
            throw new ValidationException("The infrastructure wait code is invalid.");
        }

        return ReleaseWithoutConsumingAttemptAsync(
            claim, failureCode, nextAttemptAt, cancellationToken);
    }

    private async Task ReleaseWithoutConsumingAttemptAsync(
        JobPostingParseDraftClaim claim,
        string failureCode,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var now = GetUtcNow();
        ValidateNextAttempt(nextAttemptAt, now);
        var draft = await LoadValidClaimAsync(claim, now, cancellationToken);
        EnsureTransition(draft.Status, JobPostingParseDraftStatus.Pending);
        draft.Status = JobPostingParseDraftStatus.Pending;
        draft.AttemptCount = Math.Max(0, draft.AttemptCount - 1);
        draft.FailureCode = failureCode;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = nextAttemptAt;
        draft.CompletedAt = null;
        draft.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<JobPostingParseDraft> LoadValidClaimAsync(
        JobPostingParseDraftClaim claim,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        JobPostingParseDraftClaimGuard.ValidateToken(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var draft = await _dbContext.JobPostingParseDrafts
            .SingleOrDefaultAsync(
                item => item.Id == claim.DraftId,
                cancellationToken);

        if (draft is null)
        {
            throw JobPostingParseDraftClaimGuard.InvalidClaim();
        }

        JobPostingParseDraftClaimGuard.ValidateAndConfigureConcurrency(
            _dbContext,
            draft,
            claim,
            now);
        return draft;
    }

    private static void ApplyFailed(
        JobPostingParseDraft draft,
        string failureCode,
        DateTimeOffset completedAt)
    {
        EnsureTransition(draft.Status, JobPostingParseDraftStatus.Failed);
        draft.Status = JobPostingParseDraftStatus.Failed;
        draft.FailureCode = failureCode;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = null;
        draft.CompletedAt = completedAt;
        draft.UpdatedAt = completedAt;
    }

    private static void EnsureTransition(
        JobPostingParseDraftStatus current,
        JobPostingParseDraftStatus target)
    {
        if (!JobPostingParseDraftTransitionPolicy.CanTransition(current, target))
        {
            throw JobPostingParseDraftClaimGuard.InvalidClaim();
        }
    }

    private DateTimeOffset GetUtcNow() =>
        _timeProvider.GetUtcNow().ToUniversalTime();

    private static JobPostingParseDraftClaim ToClaim(
        JobPostingParseDraft draft)
    {
        return new JobPostingParseDraftClaim(
            draft.Id,
            draft.JobPostingId,
            draft.LeaseOwner!,
            draft.AttemptCount,
            draft.Version,
            draft.LastAttemptAt!.Value,
            draft.LeaseExpiresAt!.Value,
            draft.ParserVersion);
    }

    private static void ValidateLeaseOwner(string leaseOwner)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) ||
            leaseOwner.Length > MaximumLeaseOwnerLength)
        {
            throw new ValidationException(
                $"Lease owner must contain between 1 and {MaximumLeaseOwnerLength} characters.");
        }
    }

    private static void ValidateFailure(
        JobParseFailureClassification failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (string.IsNullOrWhiteSpace(failure.FailureCode) ||
            failure.FailureCode.Length > MaximumFailureCodeLength ||
            !SafeFailureCodes.Contains(failure.FailureCode) ||
            failure.IsRetryable !=
                RetryableFailureCodes.Contains(failure.FailureCode))
        {
            throw new ValidationException("The parse failure code is invalid.");
        }
    }

    private static void ValidateNextAttempt(
        DateTimeOffset nextAttemptAt,
        DateTimeOffset now)
    {
        if (nextAttemptAt.Offset != TimeSpan.Zero || nextAttemptAt <= now)
        {
            throw new ValidationException(
                "The next attempt time must be a future UTC value.");
        }
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
