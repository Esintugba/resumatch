using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Application.JobParsing.Protection;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftExecutionService :
    IJobPostingParseDraftExecutionService
{
    private readonly AppDbContext _dbContext;
    private readonly IJobParserRegistry _parserRegistry;
    private readonly IJobPostingParseDraftClaimService _claimService;
    private readonly JobDescriptionParseResultValidator _resultValidator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _providerTimeout;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _finalizationSafetyMargin;
    private readonly TimeSpan _retryBaseDelay;
    private readonly TimeSpan _retryMaximumDelay;
    private readonly TimeSpan _retryJitterMaximum;
    private readonly ILogger<JobPostingParseDraftExecutionService> _logger;
    private readonly IAiUsageBudgetService _budgetService;
    private readonly IAiProviderConcurrencyService _providerConcurrency;
    private readonly IAiProviderCircuitBreaker _circuitBreaker;
    private readonly TimeSpan _providerCapacityRetryDelay;

    public JobPostingParseDraftExecutionService(
        AppDbContext dbContext,
        IJobParserRegistry parserRegistry,
        IJobPostingParseDraftClaimService claimService,
        JobDescriptionParseResultValidator resultValidator,
        IOptions<JobPostingParseDraftOptions> options,
        TimeProvider timeProvider,
        ILogger<JobPostingParseDraftExecutionService>? logger = null,
        IAiUsageBudgetService? budgetService = null,
        IAiProviderConcurrencyService? providerConcurrency = null,
        IAiProviderCircuitBreaker? circuitBreaker = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _parserRegistry = parserRegistry ??
            throw new ArgumentNullException(nameof(parserRegistry));
        _claimService = claimService ??
            throw new ArgumentNullException(nameof(claimService));
        _resultValidator = resultValidator ??
            throw new ArgumentNullException(nameof(resultValidator));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ??
            NullLogger<JobPostingParseDraftExecutionService>.Instance;
        _budgetService = budgetService ?? NullAiUsageBudgetService.Instance;
        _providerConcurrency = providerConcurrency ??
            NullAiProviderConcurrencyService.Instance;
        _circuitBreaker = circuitBreaker ?? NullAiProviderCircuitBreaker.Instance;

        ArgumentNullException.ThrowIfNull(options);
        _providerTimeout = options.Value.ProviderTimeout;
        _leaseDuration = options.Value.LeaseDuration;
        _finalizationSafetyMargin = options.Value.FinalizationSafetyMargin;
        _retryBaseDelay = options.Value.RetryBaseDelay;
        _retryMaximumDelay = options.Value.RetryMaximumDelay;
        _retryJitterMaximum = options.Value.RetryJitterMaximum;
        _providerCapacityRetryDelay = options.Value.ProviderCapacityRetryDelay;
        ValidateOptions();
    }

    public async Task ExecuteAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken)
    {
        JobPostingParseDraftClaimGuard.ValidateToken(claim);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.worker.execute");
        activity?.SetTag("parser.version", claim.ParserVersion);
        activity?.SetTag("draft.status", "processing");
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await ExecuteCoreAsync(claim, cancellationToken);
            _logger.LogInformation(
                "Job parse draft execution completed. AttemptCount={AttemptCount} " +
                "ParserVersion={ParserVersion} ElapsedMilliseconds={ElapsedMilliseconds}",
                claim.AttemptCount,
                claim.ParserVersion,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("failure.code", "cancelled");
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("failure.code", "concurrency_conflict");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await PersistFailureAsync(claim, exception, cancellationToken);
        }
    }

    private async Task ExecuteCoreAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken)
    {
        var now = GetUtcNow();
        var draft = await _dbContext.JobPostingParseDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == claim.DraftId,
                cancellationToken);
        if (draft is null)
        {
            throw JobPostingParseDraftClaimGuard.InvalidClaim();
        }

        JobPostingParseDraftClaimGuard.Validate(draft, claim, now);
        if (claim.LeaseExpiresAt - now <=
            _providerTimeout + _finalizationSafetyMargin)
        {
            throw JobPostingParseDraftClaimGuard.InvalidClaim();
        }

        ValidateSnapshotHashes(draft);

        var currentSourceIsValid = await IsCurrentSourceAsync(
            draft,
            cancellationToken);
        if (!currentSourceIsValid)
        {
            await PersistSupersededAsync(claim, cancellationToken);
            return;
        }

        var catalog = await LoadSkillCatalogAsync(cancellationToken);
        var catalogHash = SkillCatalogHashCalculator.Compute(catalog);
        var parser = _parserRegistry.GetRequired(draft.ParserVersion);
        if (!string.Equals(
                parser.ParserVersion,
                draft.ParserVersion,
                StringComparison.Ordinal))
        {
            throw new ParserValidationException(
                "parser_version_mismatch",
                "The parser registry returned a different parser version.");
        }

        var input = new JobDescriptionParseInput(
            draft.JobPostingId,
            draft.SourceTitle,
            draft.SourceDescription,
            draft.SourceDescriptionHash,
            draft.SourceJobPostingVersion,
            now,
            catalog);

        var circuitPermit = await _circuitBreaker.TryAcquirePermissionAsync(
            cancellationToken);
        if (!circuitPermit.Allowed)
        {
            _dbContext.ChangeTracker.Clear();
            await _claimService.ReleaseForInfrastructureWaitAsync(
                claim,
                JobPostingParseDraftClaimService.ProviderCircuitOpenCode,
                circuitPermit.RetryAt ?? GetUtcNow().Add(_providerCapacityRetryDelay),
                cancellationToken);
            return;
        }

        AiUsageReservationResult reservation;
        try
        {
            reservation = await _budgetService.ReserveAsync(
                new AiUsageReservationRequest(
                    draft.UserId,
                    draft.Id,
                    claim.AttemptCount),
                cancellationToken);
        }
        catch
        {
            await ReleaseCircuitPermitAsync(circuitPermit.Token, CancellationToken.None);
            throw;
        }
        if (reservation.Outcome is AiUsageReservationOutcome.UserBudgetExceeded or
            AiUsageReservationOutcome.SystemBudgetExceeded)
        {
            await ReleaseCircuitPermitAsync(circuitPermit.Token, CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            await _claimService.ReleaseForBudgetWaitAsync(
                claim,
                reservation.Outcome == AiUsageReservationOutcome.UserBudgetExceeded
                    ? JobPostingParseDraftClaimService.UserBudgetExhaustedCode
                    : JobPostingParseDraftClaimService.SystemBudgetExhaustedCode,
                reservation.NextEligibleAt ?? GetUtcNow().AddDays(1),
                cancellationToken);
            return;
        }

        if (reservation.Outcome == AiUsageReservationOutcome.Duplicate)
        {
            await ReleaseCircuitPermitAsync(circuitPermit.Token, CancellationToken.None);
            return;
        }

        AiProviderConcurrencyAcquireResult concurrencyLease;
        try
        {
            concurrencyLease = await _providerConcurrency.TryAcquireAsync(
                reservation.ReservationId ?? Guid.NewGuid(),
                claim.LeaseExpiresAt,
                cancellationToken);
        }
        catch
        {
            await ReleaseReservationBeforeProviderAsync(
                reservation.ReservationId, CancellationToken.None);
            await ReleaseCircuitPermitAsync(circuitPermit.Token, CancellationToken.None);
            throw;
        }

        if (!concurrencyLease.Acquired)
        {
            await ReleaseReservationBeforeProviderAsync(
                reservation.ReservationId, CancellationToken.None);
            await ReleaseCircuitPermitAsync(circuitPermit.Token, CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            await _claimService.ReleaseForInfrastructureWaitAsync(
                claim,
                JobPostingParseDraftClaimService.ProviderCapacityUnavailableCode,
                concurrencyLease.RetryAt ?? GetUtcNow().Add(_providerCapacityRetryDelay),
                cancellationToken);
            return;
        }

        JobDescriptionParseResult result;
        var providerInvocationStarted = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(_providerTimeout);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                providerInvocationStarted = true;
                result = await parser.ParseAsync(input, timeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                timeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The job description parser exceeded its configured timeout.");
            }

            if (reservation.ReservationId is { } reservationId)
            {
                await _budgetService.SettleAsync(
                    new AiUsageSettlementRequest(
                        reservationId,
                        result.ProviderMetadata?.Usage,
                        result.ProviderMetadata?.ProviderName,
                        result.ProviderMetadata?.ModelName,
                        result.ProviderMetadata?.RequestId),
                    cancellationToken);
            }

            if (circuitPermit.Token is { } permitToken)
            {
                await _circuitBreaker.RecordSuccessAsync(
                    permitToken, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            if (reservation.ReservationId is { } reservationId)
            {
                if (providerInvocationStarted)
                {
                    await _budgetService.MarkReconciliationRequiredAsync(
                        reservationId,
                        CancellationToken.None);
                }
                else
                {
                    await _budgetService.ReleaseAsync(
                        reservationId,
                        CancellationToken.None);
                }
            }

            if (circuitPermit.Token is { } permitToken)
            {
                if (providerInvocationStarted &&
                    JobParseFailureClassifier.TryClassify(
                        exception,
                        cancellationToken,
                        out var failure) &&
                    failure is not null)
                {
                    await _circuitBreaker.RecordFailureAsync(
                        permitToken, failure, CancellationToken.None);
                }
                else
                {
                    await _circuitBreaker.ReleasePermissionAsync(
                        permitToken, CancellationToken.None);
                }
            }

            throw;
        }
        finally
        {
            if (concurrencyLease.Token is { } leaseToken)
            {
                await _providerConcurrency.ReleaseAsync(
                    leaseToken, CancellationToken.None);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _resultValidator.Validate(result, input, draft.ParserVersion);
        await PersistResultAsync(
            claim,
            input,
            result,
            catalogHash,
            cancellationToken);
    }

    private async Task<ImmutableArray<SkillCatalogItem>> LoadSkillCatalogAsync(
        CancellationToken cancellationToken)
    {
        var catalog = await _dbContext.Skills
            .AsNoTracking()
            .OrderBy(skill => skill.NormalizedName)
            .ThenBy(skill => skill.Category)
            .ThenBy(skill => skill.Id)
            .Select(skill => new SkillCatalogItem(
                skill.Id,
                skill.Name,
                skill.NormalizedName,
                skill.Category))
            .ToListAsync(cancellationToken);
        return catalog.ToImmutableArray();
    }

    private async Task PersistResultAsync(
        JobPostingParseDraftClaim claim,
        JobDescriptionParseInput input,
        JobDescriptionParseResult result,
        string catalogHash,
        CancellationToken cancellationToken)
    {
        var completedAt = GetUtcNow();
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var draft = await _dbContext.JobPostingParseDrafts
            .Include(item => item.Skills)
            .ThenInclude(skill => skill.Evidence)
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
            completedAt);
        if (!await IsCurrentSourceLockedAsync(
                draft,
                transaction,
                cancellationToken))
        {
            ApplySuperseded(draft, completedAt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            JobPostingParseDraftMetrics.Superseded.Add(1);
            return;
        }

        _resultValidator.Validate(result, input, draft.ParserVersion);
        JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
            draft.Status,
            JobPostingParseDraftStatus.Ready);

        foreach (var candidate in result.Skills)
        {
            var draftSkill = new JobPostingParseDraftSkill
            {
                CandidateName = candidate.CandidateName,
                CanonicalKey = candidate.CanonicalKey,
                ResolvedSkillId = candidate.ResolvedSkillId,
                RequirementType = candidate.RequirementType,
                ProposedImportanceScore = candidate.ProposedImportanceScore,
                Confidence = candidate.Confidence,
                SortOrder = candidate.SortOrder,
                ResolutionStatus = candidate.ResolutionStatus
            };
            foreach (var evidence in candidate.Evidence)
            {
                draftSkill.Evidence.Add(new JobPostingParseDraftSkillEvidence
                {
                    Text = evidence.Text,
                    Start = evidence.Start,
                    Length = evidence.Length
                });
            }

            draft.Skills.Add(draftSkill);
        }

        draft.SkillCatalogHash = catalogHash;
        draft.ProviderRequestId = result.ProviderMetadata.RequestId;
        draft.ProviderName = result.ProviderMetadata.ProviderName;
        draft.ProviderModel = result.ProviderMetadata.ModelName;
        draft.Status = JobPostingParseDraftStatus.Ready;
        draft.FailureCode = null;
        draft.CompletedAt = completedAt;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = null;
        draft.UpdatedAt = completedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        JobPostingParseDraftMetrics.Ready.Add(1);
    }

    private async Task PersistSupersededAsync(
        JobPostingParseDraftClaim claim,
        CancellationToken cancellationToken)
    {
        var completedAt = GetUtcNow();
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
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
            completedAt);
        await LockJobPostingAsync(draft, transaction, cancellationToken);
        ApplySuperseded(draft, completedAt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        JobPostingParseDraftMetrics.Superseded.Add(1);
    }

    private static void ApplySuperseded(
        JobPostingParseDraft draft,
        DateTimeOffset completedAt)
    {
        JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
            draft.Status,
            JobPostingParseDraftStatus.Superseded);
        draft.Status = JobPostingParseDraftStatus.Superseded;
        draft.FailureCode = JobParseFailureClassifier.StaleSourceCode;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = null;
        draft.CompletedAt = completedAt;
        draft.UpdatedAt = completedAt;
    }

    private async Task<bool> IsCurrentSourceAsync(
        JobPostingParseDraft draft,
        CancellationToken cancellationToken)
    {
        var posting = await _dbContext.JobPostings
            .AsNoTracking()
            .Where(item => item.Id == draft.JobPostingId &&
                item.UserId == draft.UserId)
            .Select(item => new
            {
                item.Title,
                item.Description,
                item.Version
            })
            .SingleOrDefaultAsync(cancellationToken);
        return posting is not null &&
            posting.Version == draft.SourceJobPostingVersion &&
            string.Equals(
                DescriptionHashCalculator.Compute(posting.Description),
                draft.SourceDescriptionHash,
                StringComparison.Ordinal) &&
            string.Equals(
                SourceInputHashCalculator.Compute(
                    posting.Title,
                    posting.Description),
                draft.SourceInputHash,
                StringComparison.Ordinal);
    }

    private async Task<bool> IsCurrentSourceLockedAsync(
        JobPostingParseDraft draft,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var source = await LockJobPostingAsync(
            draft,
            transaction,
            cancellationToken);
        return source is not null &&
            source.Version == draft.SourceJobPostingVersion &&
            string.Equals(
                DescriptionHashCalculator.Compute(source.Description),
                draft.SourceDescriptionHash,
                StringComparison.Ordinal) &&
            string.Equals(
                SourceInputHashCalculator.Compute(
                    source.Title,
                    source.Description),
                draft.SourceInputHash,
                StringComparison.Ordinal);
    }

    private async Task<LockedJobPostingSource?> LockJobPostingAsync(
        JobPostingParseDraft draft,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT posting."Title", posting."Description", posting.xmin
            FROM "JobPostings" AS posting
            WHERE posting."Id" = @jobPostingId
              AND posting."UserId" = @userId
            FOR SHARE;
            """;
        AddParameter(command, "jobPostingId", draft.JobPostingId);
        AddParameter(command, "userId", draft.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LockedJobPostingSource(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<uint>(2));
    }

    private static void ValidateSnapshotHashes(JobPostingParseDraft draft)
    {
        if (!string.Equals(
                DescriptionHashCalculator.Compute(draft.SourceDescription),
                draft.SourceDescriptionHash,
                StringComparison.Ordinal))
        {
            throw new ParserValidationException(
                "description_hash_mismatch",
                "The persisted source description hash is invalid.");
        }

        if (!string.Equals(
                SourceInputHashCalculator.Compute(
                    draft.SourceTitle,
                    draft.SourceDescription),
                draft.SourceInputHash,
                StringComparison.Ordinal))
        {
            throw new ParserValidationException(
                "source_input_hash_mismatch",
                "The persisted source input hash is invalid.");
        }
    }

    private async Task PersistFailureAsync(
        JobPostingParseDraftClaim claim,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!JobParseFailureClassifier.TryClassify(
                exception,
                cancellationToken,
                out var classification) ||
            classification is null)
        {
            throw exception;
        }

        _logger.LogWarning(
            "Job parse draft execution failed safely. AttemptCount={AttemptCount} " +
            "ParserVersion={ParserVersion} FailureCode={FailureCode}",
            claim.AttemptCount,
            claim.ParserVersion,
            classification.FailureCode);

        _dbContext.ChangeTracker.Clear();
        if (classification.IsRetryable)
        {
            var delay = JobParseRetryDelayCalculator.Calculate(
                claim.DraftId,
                claim.AttemptCount,
                _retryBaseDelay,
                _retryMaximumDelay,
                _retryJitterMaximum,
                classification.MinimumRetryDelay);
            await _claimService.ReleaseForRetryAsync(
                claim,
                classification,
                GetUtcNow().Add(delay),
                cancellationToken);
            return;
        }

        await _claimService.MarkFailedAsync(
            claim,
            classification,
            cancellationToken);
    }

    private DateTimeOffset GetUtcNow() =>
        _timeProvider.GetUtcNow().ToUniversalTime();

    private Task ReleaseReservationBeforeProviderAsync(
        Guid? reservationId,
        CancellationToken cancellationToken) =>
        reservationId is { } value
            ? _budgetService.ReleaseAsync(value, cancellationToken)
            : Task.CompletedTask;

    private Task ReleaseCircuitPermitAsync(
        AiProviderCircuitPermitToken? token,
        CancellationToken cancellationToken) =>
        token is { } value
            ? _circuitBreaker.ReleasePermissionAsync(value, cancellationToken)
            : Task.CompletedTask;

    private void ValidateOptions()
    {
        if (_providerTimeout <= TimeSpan.Zero ||
            _finalizationSafetyMargin <= TimeSpan.Zero ||
            _leaseDuration <= _providerTimeout ||
            _leaseDuration - _providerTimeout <= _finalizationSafetyMargin ||
            _retryBaseDelay <= TimeSpan.Zero ||
            _retryMaximumDelay < _retryBaseDelay ||
            _retryJitterMaximum < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Job posting parse execution options are invalid.");
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

    private sealed record LockedJobPostingSource(
        string Title,
        string Description,
        uint Version);
}
