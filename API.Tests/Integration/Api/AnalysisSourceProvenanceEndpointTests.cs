using System.Net;
using System.Net.Http.Json;
using Api.Application.Analysis.Engine;
using Api.Application.Analysis.Engine.Models;
using Api.Application.Analysis.Queries;
using Api.Application.Common.Concurrency;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.DTOs.Analysis;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.Tests.Integration.Api;

public sealed class AnalysisSourceProvenanceEndpointTests :
    AnalysisApiTestBase
{
    private readonly ControllableAnalysisEngine _engine = new();

    public AnalysisSourceProvenanceEndpointTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    protected override ResuMatchWebApplicationFactory CreateApplication()
    {
        return new ResuMatchWebApplicationFactory(
            Fixture,
            configureTestServices: services =>
            {
                services.RemoveAll<IAnalysisEngine>();
                services.RemoveAll<IAnalyzerRegistry>();
                services.AddSingleton<IAnalysisEngine>(_engine);
                services.AddSingleton<IAnalyzerRegistry>(provider =>
                    new AnalyzerRegistry(
                        provider.GetServices<IAnalysisEngine>()));
            });
    }

    [Fact]
    public async Task Job_match_post_and_get_preserve_source_provenance()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedManualRequirementAsync(cancellation.Token);
        using var client = AuthenticatedClients.CreateForExistingUser(seed.User);

        using var post = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = seed.Resume.Id,
                JobPostingId = seed.Posting.Id
            },
            cancellation.Token);
        var created = await post.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(seed.Posting.Version, created.SourceJobPostingVersion);
        Assert.Equal(
            SourceInputHashCalculator.Compute(
                seed.Posting.Title,
                seed.Posting.Description),
            created.SourceJobPostingInputHash);
        Assert.Equal(
            JobPostingSkillSnapshotHashCalculator.Compute(
            [
                new JobPostingSkillFingerprintItem(
                    seed.Skill.Id,
                    seed.Requirement.RequirementType,
                    seed.Requirement.ImportanceScore,
                    seed.Skill.Name,
                    seed.Skill.NormalizedName)
            ]),
            created.SourceJobPostingSkillSetHash);
        Assert.Matches("^[0-9a-f]{64}$", created.SourceJobPostingInputHash!);
        Assert.Matches("^[0-9a-f]{64}$", created.SourceJobPostingSkillSetHash!);

        using var get = await client.GetAsync(
            $"/api/analyses/{created.Id:D}",
            cancellation.Token);
        var fetched = await get.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.NotNull(fetched);
        Assert.Equal(created.SourceJobPostingVersion,
            fetched.SourceJobPostingVersion);
        Assert.Equal(created.SourceJobPostingInputHash,
            fetched.SourceJobPostingInputHash);
        Assert.Equal(created.SourceJobPostingSkillSetHash,
            fetched.SourceJobPostingSkillSetHash);
    }

    [Fact]
    public async Task Ats_only_post_keeps_all_source_provenance_null()
    {
        using var cancellation = CreateCancellation();
        var user = CreateUser(101, "source-ats@example.com");
        var resume = CreateResume(102, user.Id);
        await using (var context = CreateDbContext())
        {
            context.AddRange(user, resume);
            await context.SaveChangesAsync(cancellation.Token);
        }
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest { ResumeId = resume.Id },
            cancellation.Token);
        var body = await response.Content.ReadFromJsonAsync<ResumeAnalysisResponse>(
            ApiJson.Options,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Null(body.SourceJobPostingVersion);
        Assert.Null(body.SourceJobPostingInputHash);
        Assert.Null(body.SourceJobPostingSkillSetHash);
    }

    [Fact]
    public async Task Source_update_after_snapshot_returns_412_and_persists_nothing()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedManualRequirementAsync(cancellation.Token);
        using var analysisClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        using var updateClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        _engine.BlockNext();

        var analysisTask = analysisClient.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = seed.Resume.Id,
                JobPostingId = seed.Posting.Id
            },
            cancellation.Token);
        await _engine.WaitUntilBlockedAsync(cancellation.Token);
        try
        {
            using var updateRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/job-postings/{seed.Posting.Id:D}")
            {
                Content = JsonContent.Create(new UpdateJobPostingRequest
                {
                    Title = seed.Posting.Title,
                    CompanyName = seed.Posting.CompanyName,
                    Description = seed.Posting.Description + " changed"
                })
            };
            updateRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                EntityTagCodec.Format(seed.Posting.Version));
            using var update = await updateClient.SendAsync(
                updateRequest,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }
        finally
        {
            _engine.Release();
        }

        using var analysis = await analysisTask;
        Assert.Equal(HttpStatusCode.PreconditionFailed, analysis.StatusCode);
        var problem = await analysis.Content.ReadAsStringAsync(
            cancellation.Token);
        Assert.DoesNotContain(seed.Posting.Title, problem, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SourceInputHashCalculator.Compute(
                seed.Posting.Title,
                seed.Posting.Description),
            problem,
            StringComparison.Ordinal);
        Assert.Equal(1, _engine.CallCount);
        await AssertNoAnalysesAsync(cancellation.Token);
    }

    [Fact]
    public async Task Stale_skill_adoption_after_snapshot_returns_412()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedStaleRequirementAsync(cancellation.Token);
        using var analysisClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        using var adoptionClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        _engine.BlockNext();

        var analysisTask = analysisClient.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = seed.Resume.Id,
                JobPostingId = seed.Posting.Id
            },
            cancellation.Token);
        await _engine.WaitUntilBlockedAsync(cancellation.Token);
        try
        {
            using var adoptRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/job-postings/{seed.Posting.Id:D}/skills/" +
                $"{seed.Skill.Id:D}/adopt");
            adoptRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                EntityTagCodec.Format(seed.Posting.Version));
            using var adoption = await adoptionClient.SendAsync(
                adoptRequest,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, adoption.StatusCode);
        }
        finally
        {
            _engine.Release();
        }

        using var analysis = await analysisTask;
        Assert.Equal(HttpStatusCode.PreconditionFailed, analysis.StatusCode);
        await AssertNoAnalysesAsync(cancellation.Token);
    }

    [Fact]
    public async Task Confirmation_after_snapshot_returns_412()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var analysisClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        using var confirmationClient =
            AuthenticatedClients.CreateForExistingUser(seed.User);
        _engine.BlockNext();

        var analysisTask = analysisClient.PostAsJsonAsync(
            "/api/analyses",
            new AnalyzeResumeRequest
            {
                ResumeId = seed.Resume.Id,
                JobPostingId = seed.Posting.Id
            },
            cancellation.Token);
        await _engine.WaitUntilBlockedAsync(cancellation.Token);
        try
        {
            using var confirmRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/job-postings/{seed.Posting.Id:D}/parse-drafts/" +
                $"{seed.Draft.Id:D}/confirm")
            {
                Content = JsonContent.Create(
                    new ConfirmJobPostingParseDraftRequest
                    {
                        SelectedSkills =
                        [
                            new SelectedJobPostingParseDraftSkillRequest
                            {
                                DraftSkillId = seed.Candidate.Id
                            }
                        ]
                    })
            };
            confirmRequest.Headers.TryAddWithoutValidation(
                "If-Match",
                EntityTagCodec.Format(seed.Draft.Version));
            using var confirmation = await confirmationClient.SendAsync(
                confirmRequest,
                cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        }
        finally
        {
            _engine.Release();
        }

        using var analysis = await analysisTask;
        Assert.Equal(HttpStatusCode.PreconditionFailed, analysis.StatusCode);
        await AssertNoAnalysesAsync(cancellation.Token);
    }

    private async Task<SourceSeed> SeedManualRequirementAsync(
        CancellationToken cancellationToken)
    {
        var user = CreateUser(1, $"source-{Guid.NewGuid():N}@example.com");
        var resume = CreateResume(2, user.Id);
        var posting = CreateJobPosting(3, user.Id);
        var skill = CreateSkill(4, "PostgreSQL", "postgresql");
        var requirement = new JobPostingSkill
        {
            Id = Id(5),
            JobPostingId = posting.Id,
            SkillId = skill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 90,
            ProvenanceStatus = JobPostingSkillProvenanceStatus.Manual,
            IsAiDetected = false,
            CreatedAt = SeedTime
        };
        await using var context = CreateDbContext();
        context.AddRange(user, resume, posting, skill, requirement);
        await context.SaveChangesAsync(cancellationToken);
        return new SourceSeed(user, resume, posting, skill, requirement);
    }

    private async Task<SourceSeed> SeedStaleRequirementAsync(
        CancellationToken cancellationToken)
    {
        var user = CreateUser(11, $"stale-{Guid.NewGuid():N}@example.com");
        var resume = CreateResume(12, user.Id);
        var posting = CreateJobPosting(13, user.Id);
        var skill = CreateSkill(14, "Docker", "docker");
        await using var context = CreateDbContext();
        context.AddRange(user, resume, posting, skill);
        await context.SaveChangesAsync(cancellationToken);

        var draft = CreateDraft(user, posting, JobPostingParseDraftStatus.Confirmed);
        draft.ConfirmedAt = SeedTime.AddMinutes(2);
        var candidate = CreateCandidate(skill);
        draft.Skills.Add(candidate);
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        var requirement = new JobPostingSkill
        {
            Id = Id(15),
            JobPostingId = posting.Id,
            SkillId = skill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ImportanceScore = 90,
            IsAiDetected = true,
            ProvenanceStatus = JobPostingSkillProvenanceStatus.AiStale,
            SourceParseDraftId = draft.Id,
            SourceParseDraftSkillId = candidate.Id,
            InvalidatedAt = SeedTime.AddMinutes(3),
            CreatedAt = SeedTime
        };
        context.JobPostingSkills.Add(requirement);
        await context.SaveChangesAsync(cancellationToken);
        return new SourceSeed(user, resume, posting, skill, requirement);
    }

    private async Task<DraftSeed> SeedReadyDraftAsync(
        CancellationToken cancellationToken)
    {
        var user = CreateUser(21, $"confirm-{Guid.NewGuid():N}@example.com");
        var resume = CreateResume(22, user.Id);
        var posting = CreateJobPosting(23, user.Id);
        var skill = CreateSkill(24, "Kubernetes", "kubernetes");
        await using var context = CreateDbContext();
        context.AddRange(user, resume, posting, skill);
        await context.SaveChangesAsync(cancellationToken);

        var draft = CreateDraft(user, posting, JobPostingParseDraftStatus.Ready);
        draft.SkillCatalogHash = SkillCatalogHashCalculator.Compute(
        [
            new SkillCatalogItem(
                skill.Id,
                skill.Name,
                skill.NormalizedName,
                skill.Category)
        ]);
        var candidate = CreateCandidate(skill);
        draft.Skills.Add(candidate);
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);
        return new DraftSeed(user, resume, posting, skill, draft, candidate);
    }

    private static JobPostingParseDraft CreateDraft(
        User user,
        JobPosting posting,
        JobPostingParseDraftStatus status) => new()
    {
        Id = Guid.NewGuid(),
        JobPostingId = posting.Id,
        UserId = user.Id,
        SourceJobPostingVersion = posting.Version,
        SourceTitle = posting.Title,
        SourceDescription = posting.Description,
        SourceDescriptionHash = DescriptionHashCalculator.Compute(
            posting.Description),
        SourceInputHash = SourceInputHashCalculator.Compute(
            posting.Title,
            posting.Description),
        ParserVersion = "job-parser-v1",
        Status = status,
        RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt = SeedTime
    };

    private static JobPostingParseDraftSkill CreateCandidate(Skill skill) => new()
    {
        Id = Guid.NewGuid(),
        CandidateName = skill.Name,
        CanonicalKey = skill.NormalizedName!,
        ResolvedSkillId = skill.Id,
        RequirementType = JobSkillRequirementType.Required,
        ProposedImportanceScore = 90,
        Confidence = 0.9500m,
        SortOrder = 0,
        ResolutionStatus = SkillResolutionStatus.Exact,
        CreatedAt = SeedTime
    };

    private async Task AssertNoAnalysesAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateDbContext();
        Assert.False(await context.ResumeAnalyses
            .AsNoTracking()
            .AnyAsync(cancellationToken));
        Assert.False(await context.AnalysisIssues
            .AsNoTracking()
            .AnyAsync(cancellationToken));
        Assert.False(await context.KeywordMatches
            .AsNoTracking()
            .AnyAsync(cancellationToken));
        Assert.False(await context.AnalysisRecommendations
            .AsNoTracking()
            .AnyAsync(cancellationToken));
    }

    private sealed record SourceSeed(
        User User,
        Resume Resume,
        JobPosting Posting,
        Skill Skill,
        JobPostingSkill Requirement);

    private sealed record DraftSeed(
        User User,
        Resume Resume,
        JobPosting Posting,
        Skill Skill,
        JobPostingParseDraft Draft,
        JobPostingParseDraftSkill Candidate);

    private sealed class ControllableAnalysisEngine : IAnalysisEngine
    {
        private readonly AnalysisEngineV1 _inner = new();
        private TaskCompletionSource? _entered;
        private TaskCompletionSource? _release;
        private int _callCount;

        public string AnalyzerVersion => AnalysisEngineV1.Version;
        public int CallCount => Volatile.Read(ref _callCount);

        public AnalysisResult Analyze(AnalysisInput input)
        {
            Interlocked.Increment(ref _callCount);
            var entered = Volatile.Read(ref _entered);
            var release = Volatile.Read(ref _release);
            if (entered is not null && release is not null)
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
            }

            return _inner.Analyze(input);
        }

        public void BlockNext()
        {
            _entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitUntilBlockedAsync(CancellationToken cancellationToken) =>
            (_entered ?? throw new InvalidOperationException(
                "The engine was not configured to block."))
            .Task.WaitAsync(cancellationToken);

        public void Release()
        {
            Interlocked.Exchange(ref _release, null)?.TrySetResult();
            Interlocked.Exchange(ref _entered, null);
        }
    }
}
