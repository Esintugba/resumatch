using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Analysis;
using Api.DTOs.Common;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Application.Analysis;

public sealed class ResumeAnalysisService : IResumeAnalysisService
{
    private const int MaximumPageSize = 100;
    private const int MaximumRecommendationRuleCodeLength = 100;

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly AnalysisInputLoader _inputLoader;
    private readonly IAnalyzerRegistry _analyzerRegistry;

    public ResumeAnalysisService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        AnalysisInputLoader inputLoader,
        IAnalyzerRegistry analyzerRegistry)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _inputLoader = inputLoader;
        _analyzerRegistry = analyzerRegistry;
    }

    public async Task<ResumeAnalysisResponse> AnalyzeAsync(
        AnalyzeResumeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var currentUserId = _currentUserService.GetRequiredUserId();
        var asOf = DateTimeOffset.UtcNow;

        var input = await _inputLoader.LoadAsync(
            request.ResumeId,
            request.JobPostingId,
            currentUserId,
            asOf,
            cancellationToken);

        var analyzer = _analyzerRegistry.Current;
        var result = analyzer.Analyze(input);
        ValidateAnalyzerResult(analyzer, input, result);

        var analysis = CreateAggregate(
            request,
            currentUserId,
            asOf,
            input.JobPosting,
            result);

        if (input.JobPosting is null)
        {
            // No entity is tracked before the engine succeeds. A single EF
            // Core SaveChanges call persists the complete ATS-only aggregate.
            _dbContext.ResumeAnalyses.Add(analysis);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await PersistJobMatchAnalysisAsync(
                analysis,
                input.JobPosting,
                currentUserId,
                cancellationToken);
        }

        return analysis.ToResponse();
    }

    private async Task PersistJobMatchAnalysisAsync(
        ResumeAnalysis analysis,
        JobPostingSnapshot snapshot,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        ValidateSourceProvenance(snapshot);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var currentSource = await LockAndReadCurrentSourceAsync(
            snapshot.Id,
            currentUserId,
            cancellationToken);
        if (currentSource is null)
        {
            throw CreateStaleSnapshotException();
        }

        var currentInputHash = SourceInputHashCalculator.Compute(
            currentSource.Title,
            currentSource.Description);
        var currentSkills = await _dbContext.JobPostingSkills
            .AsNoTracking()
            .Where(skill =>
                skill.JobPostingId == snapshot.Id &&
                (skill.ProvenanceStatus ==
                    JobPostingSkillProvenanceStatus.Manual ||
                 skill.ProvenanceStatus ==
                    JobPostingSkillProvenanceStatus.AiActive ||
                 skill.ProvenanceStatus ==
                    JobPostingSkillProvenanceStatus.AiManuallyAdopted))
            .OrderBy(skill => skill.SkillId)
            .Select(skill => new JobPostingSkillFingerprintItem(
                skill.SkillId,
                skill.RequirementType,
                skill.ImportanceScore,
                skill.Skill.Name,
                skill.Skill.NormalizedName))
            .ToListAsync(cancellationToken);
        var currentSkillSetHash =
            JobPostingSkillSnapshotHashCalculator.Compute(currentSkills);

        // xmin is retained as audit provenance. Semantic validity is decided
        // by the source and eligible-skill hashes, so metadata-only posting
        // updates do not reject a still-valid analysis snapshot.
        if (!string.Equals(
                snapshot.SourceInputHash,
                currentInputHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                snapshot.SkillSetHash,
                currentSkillSetHash,
                StringComparison.Ordinal))
        {
            throw CreateStaleSnapshotException();
        }

        _dbContext.ResumeAnalyses.Add(analysis);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<CurrentJobPostingSource?> LockAndReadCurrentSourceAsync(
        Guid jobPostingId,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction!
            .GetDbTransaction();
        command.CommandText =
            """
            SELECT posting.xmin, posting."Title", posting."Description"
            FROM "JobPostings" AS posting
            WHERE posting."Id" = @jobPostingId
              AND posting."UserId" = @userId
            FOR SHARE;
            """;
        AddParameter(command, "jobPostingId", jobPostingId);
        AddParameter(command, "userId", currentUserId);

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CurrentJobPostingSource(
            reader.GetFieldValue<uint>(0),
            reader.GetString(1),
            reader.GetString(2));
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

    private static DbUpdateConcurrencyException CreateStaleSnapshotException()
    {
        return new DbUpdateConcurrencyException(
            "The job posting analysis snapshot is no longer current.");
    }

    public async Task<ResumeAnalysisResponse> GetByIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(analysisId, "Analysis identifier");
        var currentUserId = _currentUserService.GetRequiredUserId();

        var analysis = await _dbContext.ResumeAnalyses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Issues)
            .Include(item => item.KeywordMatches)
            .Include(item => item.Recommendations)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == analysisId &&
                    item.UserId == currentUserId,
                cancellationToken);

        if (analysis is null)
        {
            throw new NotFoundException(
                "Resume analysis was not found.");
        }

        return analysis.ToResponse();
    }

    public async Task<PagedResponse<AnalysisListItemResponse>> GetHistoryAsync(
        Guid resumeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidateRequiredId(resumeId, "Resume identifier");
        var skip = ValidatePagination(page, pageSize);
        var currentUserId = _currentUserService.GetRequiredUserId();

        var resumeExists = await _dbContext.Resumes
            .AsNoTracking()
            .AnyAsync(
                resume =>
                    resume.Id == resumeId &&
                    resume.UserId == currentUserId,
                cancellationToken);

        if (!resumeExists)
        {
            throw new NotFoundException(
                "Resume was not found.");
        }

        var query = _dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(analysis =>
                analysis.ResumeId == resumeId &&
                analysis.UserId == currentUserId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(analysis => analysis.AnalyzedAt)
            .ThenByDescending(analysis => analysis.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(analysis => new AnalysisListItemResponse
            {
                Id = analysis.Id,
                ResumeId = analysis.ResumeId,
                JobPostingId = analysis.JobPostingId,
                Type = analysis.Type,
                AtsScore = analysis.AtsScore,
                JobMatchScore = analysis.JobMatchScore,
                AnalyzedAt = analysis.AnalyzedAt,
                AnalyzerVersion = analysis.AnalyzerVersion
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<AnalysisListItemResponse>
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

    private static ResumeAnalysis CreateAggregate(
        AnalyzeResumeRequest request,
        Guid currentUserId,
        DateTimeOffset asOf,
        JobPostingSnapshot? jobPostingSnapshot,
        AnalysisResult result)
    {
        var analysis = new ResumeAnalysis
        {
            UserId = currentUserId,
            ResumeId = request.ResumeId,
            JobPostingId = request.JobPostingId,
            JobPostingUserId = request.JobPostingId.HasValue
                ? currentUserId
                : null,
            SourceJobPostingVersion = jobPostingSnapshot?.SourceVersion,
            SourceJobPostingInputHash = jobPostingSnapshot?.SourceInputHash,
            SourceJobPostingSkillSetHash = jobPostingSnapshot?.SkillSetHash,
            Type = request.JobPostingId.HasValue
                ? AnalysisType.JobMatch
                : AnalysisType.AtsOnly,
            AtsScore = result.AtsScore,
            JobMatchScore = result.JobMatchScore,
            StructureScore = result.StructureScore,
            FormattingScore = result.FormattingScore,
            ContentScore = result.ContentScore,
            KeywordScore = result.KeywordScore,
            Summary = null,
            AnalyzedAt = asOf,
            AnalyzerVersion = result.AnalyzerVersion,
            CreatedAt = asOf
        };

        foreach (var issue in result.Issues)
        {
            analysis.Issues.Add(new AnalysisIssue
            {
                ResumeAnalysisId = analysis.Id,
                Type = issue.Type,
                Severity = issue.Severity,
                Code = issue.Code,
                Title = issue.Title,
                Description = issue.Description,
                ResumeSection = issue.ResumeSection,
                CreatedAt = asOf
            });
        }

        foreach (var keywordMatch in result.KeywordMatches)
        {
            analysis.KeywordMatches.Add(new KeywordMatch
            {
                ResumeAnalysisId = analysis.Id,
                SkillId = keywordMatch.SkillId,
                Keyword = keywordMatch.Keyword,
                Status = keywordMatch.Status,
                Weight = keywordMatch.Weight,
                ResumeSection = keywordMatch.ResumeSection,
                CreatedAt = asOf
            });
        }

        for (var index = 0; index < result.Recommendations.Length; index++)
        {
            var recommendation = result.Recommendations[index];
            analysis.Recommendations.Add(new AnalysisRecommendation
            {
                ResumeAnalysisId = analysis.Id,
                RuleCode = recommendation.RuleCode,
                SortOrder = index,
                Type = recommendation.Type,
                Priority = recommendation.Priority,
                Title = recommendation.Title,
                Description = recommendation.Description,
                TargetSection = recommendation.TargetSection,
                OriginalContent = recommendation.OriginalContent,
                SuggestedContent = recommendation.SuggestedContent,
                IsAiGenerated = recommendation.IsAiGenerated,
                Status = recommendation.Status,
                CreatedAt = asOf
            });
        }

        return analysis;
    }

    private static void ValidateRequest(AnalyzeResumeRequest request)
    {
        if (request.ResumeId == Guid.Empty)
        {
            throw new ValidationException(
                "Resume identifier cannot be empty.");
        }

        if (request.JobPostingId == Guid.Empty)
        {
            throw new ValidationException(
                "Job posting identifier cannot be empty.");
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

    private static void ValidateRequiredId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException($"{fieldName} cannot be empty.");
        }
    }

    private static void ValidateAnalyzerResult(
        IAnalysisEngine analyzer,
        AnalysisInput input,
        AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (string.IsNullOrWhiteSpace(result.AnalyzerVersion) ||
            !string.Equals(
                result.AnalyzerVersion,
                analyzer.AnalyzerVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The analyzer returned an invalid version identifier.");
        }

        if (input.JobPosting is null &&
            (result.JobMatchScore.HasValue || result.KeywordMatches.Length > 0))
        {
            throw new InvalidOperationException(
                "An ATS-only analysis cannot contain job-match results.");
        }

        foreach (var recommendation in result.Recommendations)
        {
            if (string.IsNullOrWhiteSpace(recommendation.RuleCode))
            {
                throw new InvalidOperationException(
                    "An analysis recommendation must contain a rule code.");
            }

            if (recommendation.RuleCode.Length >
                MaximumRecommendationRuleCodeLength)
            {
                throw new InvalidOperationException(
                    $"An analysis recommendation rule code cannot exceed " +
                    $"{MaximumRecommendationRuleCodeLength} characters.");
            }
        }
    }

    private static void ValidateSourceProvenance(JobPostingSnapshot snapshot)
    {
        if (snapshot.Id == Guid.Empty ||
            snapshot.SourceVersion == 0 ||
            !IsSha256(snapshot.SourceInputHash) ||
            !IsSha256(snapshot.SkillSetHash))
        {
            throw new InvalidOperationException(
                "A Job Match analysis requires complete source provenance.");
        }
    }

    private static bool IsSha256(string value)
    {
        return value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private sealed record CurrentJobPostingSource(
        uint Version,
        string Title,
        string Description);
}
