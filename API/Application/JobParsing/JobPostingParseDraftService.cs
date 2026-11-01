using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Data;
using Api.DTOs.Common;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Api.Application.JobParsing;

public sealed class JobPostingParseDraftService : IJobPostingParseDraftService
{
    private const int MaximumPageSize = 100;
    private const int MaximumTitleLength = 250;
    private const int MaximumParserVersionLength = 100;
    private const string ActiveFingerprintIndex =
        "UX_JobPostingParseDrafts_ActiveFingerprint";

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJobParserRegistry _parserRegistry;
    private readonly JobDescriptionPreprocessor _preprocessor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _draftLifetime;
    private readonly TimeSpan _parseCooldown;
    private readonly ILogger<JobPostingParseDraftService> _logger;

    public JobPostingParseDraftService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IJobParserRegistry parserRegistry,
        JobDescriptionPreprocessor preprocessor,
        IOptions<JobPostingParseDraftOptions> options,
        TimeProvider timeProvider,
        ILogger<JobPostingParseDraftService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _currentUserService = currentUserService ??
            throw new ArgumentNullException(nameof(currentUserService));
        _parserRegistry = parserRegistry ??
            throw new ArgumentNullException(nameof(parserRegistry));
        _preprocessor = preprocessor ??
            throw new ArgumentNullException(nameof(preprocessor));
        _timeProvider = timeProvider ??
            throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? NullLogger<JobPostingParseDraftService>.Instance;

        ArgumentNullException.ThrowIfNull(options);
        _draftLifetime = options.Value.Lifetime;
        _parseCooldown = options.Value.ParseCooldown;
        if (_draftLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Job posting parse draft lifetime must be greater than zero.");
        }

        if (_parseCooldown < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Job posting parse cooldown cannot be negative.");
        }
    }

    public async Task<JobPostingParseDraftResponse> RequestParseAsync(
        Guid jobPostingId,
        uint expectedJobPostingVersion,
        CancellationToken cancellationToken)
    {
        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.request");
        ValidateIdentifier(jobPostingId);
        cancellationToken.ThrowIfCancellationRequested();

        var userId = _currentUserService.GetRequiredUserId();
        var requestedAt = _timeProvider.GetUtcNow();

        try
        {
            return await RequestWithinTransactionAsync(
                jobPostingId,
                userId,
                expectedJobPostingVersion,
                requestedAt,
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsActiveFingerprintViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();

            return await RequestWithinTransactionAsync(
                jobPostingId,
                userId,
                expectedJobPostingVersion,
                requestedAt,
                cancellationToken);
        }
    }

    public async Task<JobPostingParseDraftDetailResponse> GetDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(jobPostingId);
        ValidateDraftIdentifier(draftId);
        var userId = _currentUserService.GetRequiredUserId();

        var draft = await _dbContext.JobPostingParseDrafts
            .AsNoTracking()
            .AsSplitQuery()
            .Where(item => item.Id == draftId &&
                item.JobPostingId == jobPostingId &&
                item.UserId == userId &&
                item.JobPosting.UserId == userId)
            .Select(item => new
            {
                item.Id,
                item.JobPostingId,
                item.Status,
                item.ParserVersion,
                item.Version,
                item.RequestedAt,
                item.CompletedAt,
                item.ConfirmedAt,
                item.ExpiresAt,
                item.AttemptCount,
                item.FailureCode,
                item.ProviderName,
                item.ProviderModel,
                Skills = item.Skills.Select(skill => new
                {
                    skill.Id,
                    skill.CandidateName,
                    skill.CanonicalKey,
                    skill.ResolvedSkillId,
                    skill.RequirementType,
                    skill.ProposedImportanceScore,
                    skill.Confidence,
                    skill.SortOrder,
                    skill.ResolutionStatus,
                    Evidence = skill.Evidence.Select(evidence => new
                    {
                        evidence.Text,
                        evidence.Start,
                        evidence.Length
                    }).ToList()
                }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (draft is null)
        {
            throw DraftNotFound();
        }

        var skills = draft.Skills
            .OrderBy(skill => skill.SortOrder)
            .ThenBy(skill => skill.Id)
            .Select(skill => new JobPostingParseDraftSkillResponse(
                skill.Id,
                skill.CandidateName,
                skill.CanonicalKey,
                skill.ResolvedSkillId,
                skill.RequirementType,
                skill.ProposedImportanceScore,
                skill.Confidence,
                skill.SortOrder,
                skill.ResolutionStatus,
                skill.Evidence
                    .OrderBy(evidence => evidence.Start)
                    .ThenBy(evidence => evidence.Length)
                    .ThenBy(evidence => evidence.Text, StringComparer.Ordinal)
                    .Select(evidence =>
                        new JobPostingParseDraftSkillEvidenceResponse(
                            evidence.Text,
                            evidence.Start,
                            evidence.Length))
                    .ToList()))
            .ToList();

        return new JobPostingParseDraftDetailResponse(
            draft.Id,
            draft.JobPostingId,
            draft.Status,
            draft.ParserVersion,
            draft.Version,
            draft.RequestedAt,
            draft.CompletedAt,
            draft.ConfirmedAt,
            draft.ExpiresAt,
            draft.AttemptCount,
            draft.FailureCode,
            draft.ProviderName,
            draft.ProviderModel,
            skills);
    }

    public async Task<PagedResponse<JobPostingParseDraftListItemResponse>>
        GetDraftHistoryAsync(
            Guid jobPostingId,
            int page,
            int pageSize,
            JobPostingParseDraftStatus? status,
            CancellationToken cancellationToken)
    {
        ValidateIdentifier(jobPostingId);
        var skip = ValidatePagination(page, pageSize);
        if (status.HasValue && !Enum.IsDefined(status.Value))
        {
            throw new ValidationException("The draft status is invalid.");
        }

        var userId = _currentUserService.GetRequiredUserId();
        var ownsPosting = await _dbContext.JobPostings
            .AsNoTracking()
            .AnyAsync(
                posting => posting.Id == jobPostingId &&
                    posting.UserId == userId,
                cancellationToken);
        if (!ownsPosting)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        var query = _dbContext.JobPostingParseDrafts
            .AsNoTracking()
            .Where(draft => draft.JobPostingId == jobPostingId &&
                draft.UserId == userId &&
                draft.JobPosting.UserId == userId);

        if (status.HasValue)
        {
            query = query.Where(draft => draft.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(draft => draft.RequestedAt)
            .ThenByDescending(draft => draft.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(draft => new JobPostingParseDraftListItemResponse(
                draft.Id,
                draft.Status,
                draft.ParserVersion,
                draft.Version,
                draft.RequestedAt,
                draft.CompletedAt,
                draft.ExpiresAt,
                draft.AttemptCount,
                draft.FailureCode))
            .ToListAsync(cancellationToken);

        return new PagedResponse<JobPostingParseDraftListItemResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<JobPostingParseDraftResponse> CancelDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        uint expectedDraftVersion,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(jobPostingId);
        ValidateDraftIdentifier(draftId);
        cancellationToken.ThrowIfCancellationRequested();
        var userId = _currentUserService.GetRequiredUserId();

        var draft = await _dbContext.JobPostingParseDrafts
            .SingleOrDefaultAsync(
                item => item.Id == draftId &&
                    item.JobPostingId == jobPostingId &&
                    item.UserId == userId &&
                    item.JobPosting.UserId == userId,
                cancellationToken);

        if (draft is null)
        {
            throw DraftNotFound();
        }

        if (draft.Version != expectedDraftVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The job posting parse draft was modified by another operation.");
        }

        _dbContext.Entry(draft)
            .Property(item => item.Version)
            .OriginalValue = expectedDraftVersion;

        if (!JobPostingParseDraftTransitionPolicy.CanTransition(
                draft.Status,
                JobPostingParseDraftStatus.Cancelled))
        {
            throw new ConflictException(
                "The job posting parse draft cannot be cancelled from its current status.",
                ApiErrorCodes.ParseDraftStateInvalid);
        }

        draft.Status = JobPostingParseDraftStatus.Cancelled;
        draft.LeaseOwner = null;
        draft.LeaseExpiresAt = null;
        draft.NextAttemptAt = null;
        draft.UpdatedAt = _timeProvider.GetUtcNow();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(draft, isReused: true);
    }

    public async Task<JobPostingParseDraftResponse> ConfirmDraftAsync(
        Guid jobPostingId,
        Guid draftId,
        uint expectedDraftVersion,
        ConfirmJobPostingParseDraftRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = JobParsingTelemetry.StartActivity(
            "job_parser.confirm");
        activity?.SetTag("draft.status", "ready");
        ValidateIdentifier(jobPostingId);
        ValidateDraftIdentifier(draftId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var selections = request.SelectedSkills ??
            throw new ValidationException("SelectedSkills is required.");
        ValidateSelections(selections);

        var userId = _currentUserService.GetRequiredUserId();
        var confirmedAt = _timeProvider.GetUtcNow().ToUniversalTime();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var source = await LockOwnedJobPostingSourceAsync(
            transaction,
            jobPostingId,
            userId,
            cancellationToken);
        if (source is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        var draft = await _dbContext.JobPostingParseDrafts
            .Include(item => item.Skills)
            .SingleOrDefaultAsync(
                item => item.Id == draftId &&
                    item.JobPostingId == jobPostingId &&
                    item.UserId == userId &&
                    item.JobPosting.UserId == userId,
                cancellationToken);
        if (draft is null)
        {
            throw DraftNotFound();
        }

        if (draft.Version != expectedDraftVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The job posting parse draft was modified by another operation.");
        }

        _dbContext.Entry(draft)
            .Property(item => item.Version)
            .OriginalValue = expectedDraftVersion;

        if (draft.Status != JobPostingParseDraftStatus.Ready)
        {
            throw new ConflictException(
                "Only a ready job posting parse draft can be confirmed.",
                ApiErrorCodes.ParseDraftStateInvalid);
        }

        if (draft.ExpiresAt is null || draft.ExpiresAt <= confirmedAt)
        {
            JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
                draft.Status,
                JobPostingParseDraftStatus.Expired);
            draft.Status = JobPostingParseDraftStatus.Expired;
            draft.UpdatedAt = confirmedAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ConflictException(
                "The job posting parse draft has expired.",
                ApiErrorCodes.ParseDraftStateInvalid);
        }

        EnsureParserVersionIsSupported(draft.ParserVersion);
        ValidatePersistedSnapshot(draft);
        if (!SourceMatches(draft, source))
        {
            JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
                draft.Status,
                JobPostingParseDraftStatus.Superseded);
            draft.Status = JobPostingParseDraftStatus.Superseded;
            draft.FailureCode = JobParseFailureClassifier.StaleSourceCode;
            draft.UpdatedAt = confirmedAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DbUpdateConcurrencyException(
                "The job posting source changed after the draft was created.");
        }

        var selectedCandidates = ResolveSelectedCandidates(draft, selections);
        var selectedSkillIds = selectedCandidates
            .Select(item => item.Candidate.ResolvedSkillId!.Value)
            .ToArray();
        if (selectedSkillIds.Distinct().Count() != selectedSkillIds.Length)
        {
            throw new ValidationException(
                "Selected candidates cannot resolve to the same skill more than once.");
        }

        var lockedSkills = await LockSelectedSkillsAsync(
            transaction,
            selectedSkillIds,
            cancellationToken);
        ValidateSelectedSkillIdentity(selectedCandidates, lockedSkills);
        if (selectedCandidates.Count > 0)
        {
            await ValidateSkillCatalogAsync(draft, cancellationToken);
        }

        var existingSkills = await _dbContext.JobPostingSkills
            .Where(item => item.JobPostingId == jobPostingId &&
                selectedSkillIds.Contains(item.SkillId))
            .ToDictionaryAsync(item => item.SkillId, cancellationToken);

        foreach (var selected in selectedCandidates)
        {
            var skillId = selected.Candidate.ResolvedSkillId!.Value;
            if (existingSkills.TryGetValue(skillId, out var existingSkill))
            {
                ReactivateIfEligible(existingSkill, selected, draft.Id, confirmedAt);
                continue;
            }

            _dbContext.JobPostingSkills.Add(new JobPostingSkill
            {
                JobPostingId = jobPostingId,
                SkillId = skillId,
                RequirementType = selected.Candidate.RequirementType,
                ImportanceScore = selected.Selection.ImportanceScore ??
                    selected.Candidate.ProposedImportanceScore,
                IsAiDetected = true,
                ProvenanceStatus = JobPostingSkillProvenanceStatus.AiActive,
                SourceParseDraftId = draft.Id,
                SourceParseDraftSkillId = selected.Candidate.Id,
                InvalidatedAt = null
            });
        }

        JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
            draft.Status,
            JobPostingParseDraftStatus.Confirmed);
        draft.Status = JobPostingParseDraftStatus.Confirmed;
        draft.ConfirmedAt = confirmedAt;
        draft.UpdatedAt = confirmedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        activity?.SetTag("draft.status", "confirmed");
        return ToResponse(draft, isReused: true);
    }

    private static void ReactivateIfEligible(
        JobPostingSkill existingSkill,
        SelectedCandidate selected,
        Guid draftId,
        DateTimeOffset confirmedAt)
    {
        if (existingSkill.ProvenanceStatus is not
                (JobPostingSkillProvenanceStatus.AiStale or
                 JobPostingSkillProvenanceStatus.LegacyAiUnknown))
        {
            return;
        }

        existingSkill.RequirementType = selected.Candidate.RequirementType;
        existingSkill.ImportanceScore = selected.Selection.ImportanceScore ??
            selected.Candidate.ProposedImportanceScore;
        existingSkill.IsAiDetected = true;
        existingSkill.ProvenanceStatus = JobPostingSkillProvenanceStatus.AiActive;
        existingSkill.SourceParseDraftId = draftId;
        existingSkill.SourceParseDraftSkillId = selected.Candidate.Id;
        existingSkill.InvalidatedAt = null;
        existingSkill.UpdatedAt = confirmedAt;
    }

    private static void ValidateSelections(
        IReadOnlyList<SelectedJobPostingParseDraftSkillRequest> selections)
    {
        if (selections.Count > 100)
        {
            throw new ValidationException(
                "No more than 100 draft skills can be selected.");
        }

        if (selections.Any(item => item is null))
        {
            throw new ValidationException("SelectedSkills cannot contain null items.");
        }

        if (selections.Any(item => item.DraftSkillId == Guid.Empty))
        {
            throw new ValidationException(
                "A selected draft skill identifier cannot be empty.");
        }

        if (selections.Any(item =>
                item.ImportanceScore is < 0 or > 100))
        {
            throw new ValidationException(
                "ImportanceScore must be between 0 and 100.");
        }

        if (selections.Select(item => item.DraftSkillId).Distinct().Count() !=
            selections.Count)
        {
            throw new ValidationException(
                "A draft skill cannot be selected more than once.");
        }
    }

    private static IReadOnlyList<SelectedCandidate> ResolveSelectedCandidates(
        JobPostingParseDraft draft,
        IReadOnlyList<SelectedJobPostingParseDraftSkillRequest> selections)
    {
        var candidates = draft.Skills.ToDictionary(item => item.Id);
        var selected = new List<SelectedCandidate>(selections.Count);
        foreach (var selection in selections)
        {
            if (!candidates.TryGetValue(selection.DraftSkillId, out var candidate))
            {
                throw new ValidationException(
                    "A selected skill does not belong to this parse draft.");
            }

            if (candidate.ResolutionStatus is not
                    (SkillResolutionStatus.Exact or SkillResolutionStatus.Alias) ||
                candidate.ResolvedSkillId is null)
            {
                throw new ValidationException(
                    "Only an exact or alias-resolved draft skill can be confirmed.");
            }

            if (string.IsNullOrWhiteSpace(candidate.CanonicalKey) ||
                !Enum.IsDefined(candidate.RequirementType) ||
                candidate.ProposedImportanceScore is < 0 or > 100)
            {
                throw new ValidationException(
                    "A selected draft skill contains invalid persisted metadata.");
            }

            selected.Add(new SelectedCandidate(selection, candidate));
        }

        return selected;
    }

    private async Task<IReadOnlyDictionary<Guid, LockedSkill>>
        LockSelectedSkillsAsync(
            IDbContextTransaction transaction,
            IReadOnlyCollection<Guid> skillIds,
            CancellationToken cancellationToken)
    {
        if (skillIds.Count == 0)
        {
            return new Dictionary<Guid, LockedSkill>();
        }

        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT skill."Id", skill."NormalizedName", skill."Category"
            FROM "Skills" AS skill
            WHERE skill."Id" = ANY(@skillIds)
            ORDER BY skill."Id"
            FOR SHARE;
            """;
        AddParameter(command, "skillIds", skillIds.Order().ToArray());

        var result = new Dictionary<Guid, LockedSkill>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var skill = new LockedSkill(
                reader.GetGuid(0),
                reader.GetString(1),
                (SkillCategory)reader.GetInt32(2));
            result.Add(skill.Id, skill);
        }

        return result;
    }

    private static void ValidateSelectedSkillIdentity(
        IReadOnlyList<SelectedCandidate> selectedCandidates,
        IReadOnlyDictionary<Guid, LockedSkill> skills)
    {
        foreach (var selected in selectedCandidates)
        {
            var skillId = selected.Candidate.ResolvedSkillId!.Value;
            if (!skills.TryGetValue(skillId, out var skill))
            {
                throw new ValidationException(
                    "A selected skill no longer exists in the skill dictionary.");
            }

            if (!string.Equals(
                    skill.NormalizedName,
                    selected.Candidate.CanonicalKey,
                    StringComparison.Ordinal) ||
                !Enum.IsDefined(skill.Category))
            {
                throw new ConflictException(
                    "A selected skill identity changed; request a new parse draft.",
                    ApiErrorCodes.ParseDraftOutdated);
            }
        }
    }

    private async Task ValidateSkillCatalogAsync(
        JobPostingParseDraft draft,
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
        var currentHash = SkillCatalogHashCalculator.Compute(catalog);
        if (string.IsNullOrWhiteSpace(draft.SkillCatalogHash) ||
            !string.Equals(
                draft.SkillCatalogHash,
                currentHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The skill dictionary changed; request a new parse draft.",
                ApiErrorCodes.ParseDraftOutdated);
        }
    }

    private void EnsureParserVersionIsSupported(string parserVersion)
    {
        try
        {
            var parser = _parserRegistry.GetRequired(parserVersion);
            if (!string.Equals(
                    parser.ParserVersion,
                    parserVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException)
        {
            throw new ConflictException(
                "The parse draft uses an unsupported parser version.",
                ApiErrorCodes.ParseDraftOutdated);
        }
    }

    private static void ValidatePersistedSnapshot(JobPostingParseDraft draft)
    {
        if (!string.Equals(
                DescriptionHashCalculator.Compute(draft.SourceDescription),
                draft.SourceDescriptionHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                SourceInputHashCalculator.Compute(
                    draft.SourceTitle,
                    draft.SourceDescription),
                draft.SourceInputHash,
                StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The persisted parse draft snapshot is invalid.",
                ApiErrorCodes.ParseDraftOutdated);
        }
    }

    private static bool SourceMatches(
        JobPostingParseDraft draft,
        LockedJobPostingSource source)
    {
        return source.Version == draft.SourceJobPostingVersion &&
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

    private async Task<LockedJobPostingSource?> LockOwnedJobPostingSourceAsync(
        IDbContextTransaction transaction,
        Guid jobPostingId,
        Guid userId,
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
            FOR UPDATE;
            """;
        AddParameter(command, "jobPostingId", jobPostingId);
        AddParameter(command, "userId", userId);

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

    private async Task<JobPostingParseDraftResponse> RequestWithinTransactionAsync(
        Guid jobPostingId,
        Guid userId,
        uint expectedJobPostingVersion,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var lockedVersion = await LockOwnedJobPostingAsync(
            transaction,
            jobPostingId,
            userId,
            cancellationToken);

        if (lockedVersion is null)
        {
            throw new NotFoundException("Job posting was not found.");
        }

        if (lockedVersion.Value != expectedJobPostingVersion)
        {
            throw new DbUpdateConcurrencyException(
                "The job posting was modified by another operation.");
        }

        var jobPosting = await _dbContext.JobPostings
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == jobPostingId && item.UserId == userId,
                cancellationToken);

        ValidateTitle(jobPosting.Title);
        var source = ValidateAndPreprocessDescription(jobPosting.Description);
        var parserVersion = GetCurrentParserVersion();
        var sourceInputHash = SourceInputHashCalculator.Compute(
            jobPosting.Title,
            source.OriginalDescription);

        var activeDraft = await _dbContext.JobPostingParseDrafts
            .SingleOrDefaultAsync(
                draft => draft.JobPostingId == jobPostingId &&
                    draft.UserId == userId &&
                    draft.SourceInputHash == sourceInputHash &&
                    draft.ParserVersion == parserVersion &&
                    (draft.Status == JobPostingParseDraftStatus.Pending ||
                     draft.Status == JobPostingParseDraftStatus.Processing ||
                     draft.Status == JobPostingParseDraftStatus.Ready),
                cancellationToken);

        if (activeDraft is not null)
        {
            if (activeDraft.Status != JobPostingParseDraftStatus.Ready ||
                activeDraft.ExpiresAt > requestedAt)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToResponse(activeDraft, isReused: true);
            }

            JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
                activeDraft.Status,
                JobPostingParseDraftStatus.Expired);
            activeDraft.Status = JobPostingParseDraftStatus.Expired;
            activeDraft.UpdatedAt = requestedAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (_parseCooldown > TimeSpan.Zero)
        {
            var lastTerminalAt = await _dbContext.JobPostingParseDrafts
                .AsNoTracking()
                .Where(draft => draft.JobPostingId == jobPostingId &&
                    draft.UserId == userId &&
                    draft.SourceInputHash == sourceInputHash &&
                    draft.ParserVersion == parserVersion &&
                    draft.Status != JobPostingParseDraftStatus.Pending &&
                    draft.Status != JobPostingParseDraftStatus.Processing &&
                    draft.Status != JobPostingParseDraftStatus.Ready)
                .Select(draft =>
                    draft.Status == JobPostingParseDraftStatus.Confirmed
                        ? draft.ConfirmedAt ?? draft.UpdatedAt ??
                            draft.CompletedAt ?? draft.RequestedAt
                        : draft.Status == JobPostingParseDraftStatus.Cancelled
                            ? draft.UpdatedAt ?? draft.RequestedAt
                            : draft.Status == JobPostingParseDraftStatus.Expired
                                ? draft.CompletedAt ?? draft.RequestedAt
                                : draft.CompletedAt ?? draft.UpdatedAt ??
                                    draft.RequestedAt)
                .OrderByDescending(timestamp => timestamp)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastTerminalAt != default)
            {
                var availableAt = lastTerminalAt.Add(_parseCooldown);
                if (availableAt > requestedAt)
                {
                    var retryAfter = availableAt - requestedAt;
                    _logger.LogWarning(
                        "Job parse request rejected by source cooldown. " +
                        "RetryAfterSeconds={RetryAfterSeconds}",
                        Math.Ceiling(retryAfter.TotalSeconds));
                    throw new TooManyRequestsException(
                        "A recent parse already used this job posting content. " +
                        "Retry after the cooldown period.",
                        retryAfter,
                        ApiErrorCodes.ParseCooldown);
                }
            }
        }

        var draft = new JobPostingParseDraft
        {
            JobPostingId = jobPosting.Id,
            UserId = userId,
            SourceJobPostingVersion = lockedVersion.Value,
            SourceTitle = jobPosting.Title,
            SourceDescription = source.OriginalDescription,
            SourceDescriptionHash = source.DescriptionHash,
            SourceInputHash = sourceInputHash,
            SkillCatalogHash = null,
            ParserVersion = parserVersion,
            Status = JobPostingParseDraftStatus.Pending,
            FailureCode = null,
            AttemptCount = 0,
            LeaseOwner = null,
            RequestedAt = requestedAt,
            LeaseExpiresAt = null,
            LastAttemptAt = null,
            NextAttemptAt = null,
            CompletedAt = null,
            ConfirmedAt = null,
            ExpiresAt = requestedAt.Add(_draftLifetime)
        };

        _dbContext.JobPostingParseDrafts.Add(draft);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(draft, isReused: false);
    }

    private async Task<uint?> LockOwnedJobPostingAsync(
        IDbContextTransaction transaction,
        Guid jobPostingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            SELECT posting.xmin
            FROM "JobPostings" AS posting
            WHERE posting."Id" = @jobPostingId
              AND posting."UserId" = @userId
            FOR SHARE;
            """;

        var jobPostingParameter = command.CreateParameter();
        jobPostingParameter.ParameterName = "jobPostingId";
        jobPostingParameter.Value = jobPostingId;
        command.Parameters.Add(jobPostingParameter);

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "userId";
        userParameter.Value = userId;
        command.Parameters.Add(userParameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (uint)result;
    }

    private JobDescriptionPreprocessingResult ValidateAndPreprocessDescription(
        string description)
    {
        try
        {
            return _preprocessor.Process(description);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException(exception.Message);
        }
    }

    private string GetCurrentParserVersion()
    {
        var parserVersion = _parserRegistry.Current.ParserVersion;
        if (string.IsNullOrWhiteSpace(parserVersion) ||
            parserVersion.Length > MaximumParserVersionLength)
        {
            throw new InvalidOperationException(
                "The current job parser has an invalid version identifier.");
        }

        return parserVersion;
    }

    private static void ValidateIdentifier(Guid jobPostingId)
    {
        if (jobPostingId == Guid.Empty)
        {
            throw new ValidationException(
                "Job posting identifier cannot be empty.");
        }
    }

    private static void ValidateDraftIdentifier(Guid draftId)
    {
        if (draftId == Guid.Empty)
        {
            throw new ValidationException(
                "Job posting parse draft identifier cannot be empty.");
        }
    }

    private static int ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ValidationException("Page must be greater than zero.");
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ValidationException(
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            throw new ValidationException(
                "The requested page is outside the supported range.");
        }

        return (int)skip;
    }

    private static NotFoundException DraftNotFound() =>
        new("Job posting parse draft was not found.");

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ValidationException("Job posting title is required.");
        }

        if (title.Length > MaximumTitleLength)
        {
            throw new ValidationException(
                $"Job posting title cannot exceed {MaximumTitleLength} characters.");
        }
    }

    private static JobPostingParseDraftResponse ToResponse(
        JobPostingParseDraft draft,
        bool isReused)
    {
        return new JobPostingParseDraftResponse(
            draft.Id,
            draft.JobPostingId,
            draft.Status,
            draft.ParserVersion,
            draft.Version,
            draft.RequestedAt,
            draft.ExpiresAt,
            isReused);
    }

    private static bool IsActiveFingerprintViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ActiveFingerprintIndex
        };
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

    private sealed record SelectedCandidate(
        SelectedJobPostingParseDraftSkillRequest Selection,
        JobPostingParseDraftSkill Candidate);

    private sealed record LockedSkill(
        Guid Id,
        string NormalizedName,
        SkillCategory Category);

    private sealed record LockedJobPostingSource(
        string Title,
        string Description,
        uint Version);
}
