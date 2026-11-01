using System.Net;
using System.Net.Http.Json;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using Api.Infrastructure.JobParsing;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.Api;

public sealed class JobPostingParseDraftConfirmationEndpointTests :
    ApiIntegrationTestBase
{
    private const string ParserVersion = "job-parser-v1";
    private const string Description =
        "C# and ASP.NET Core are required. Docker is preferred.";

    public JobPostingParseDraftConfirmationEndpointTests(
        PostgreSqlFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Confirm_without_authentication_returns_unauthorized()
    {
        using var cancellation = CreateCancellation();
        using var client = Application.CreateHttpsClient();
        using var request = CreateConfirmRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new ConfirmJobPostingParseDraftRequest());

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("malformed", 400)]
    [InlineData("W/\"00000001\"", 400)]
    public async Task Confirm_requires_valid_draft_etag(
        string? ifMatch,
        int expectedStatus)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var client = CreateClient(seed.User);
        using var request = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            ifMatch,
            new ConfirmJobPostingParseDraftRequest());

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task Confirm_selected_skills_persists_only_selection_with_default_and_override()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            candidateCount: 3,
            includeAnalysis: true);
        var initialAnalysisScore = seed.Analysis!.AtsScore;
        var requestBody = new ConfirmJobPostingParseDraftRequest
        {
            SelectedSkills =
            [
                new() { DraftSkillId = seed.Candidates[0].Id },
                new()
                {
                    DraftSkillId = seed.Candidates[1].Id,
                    ImportanceScore = 42
                }
            ]
        };
        using var client = CreateClient(seed.User);
        using var request = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(seed.Draft.Version),
            requestBody);

        using var response = await client.SendAsync(request, cancellation.Token);
        var body = await response.Content
            .ReadFromJsonAsync<JobPostingParseDraftResponse>(ApiJson.Options, cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(JobPostingParseDraftStatus.Confirmed, body.Status);
        Assert.NotEqual(seed.Draft.Version, body.DraftVersion);
        Assert.Equal(EntityTagCodec.Format(body.DraftVersion), response.Headers.ETag?.Tag);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var skills = await verification.JobPostingSkills
            .AsNoTracking()
            .Where(item => item.JobPostingId == seed.Posting.Id)
            .OrderBy(item => item.SkillId)
            .ToListAsync(cancellation.Token);
        Assert.Equal(2, skills.Count);
        Assert.Contains(skills, item =>
            item.SkillId == seed.Skills[0].Id &&
            item.ImportanceScore == seed.Candidates[0].ProposedImportanceScore &&
            item.IsAiDetected &&
            item.ProvenanceStatus == JobPostingSkillProvenanceStatus.AiActive &&
            item.SourceParseDraftId == seed.Draft.Id &&
            item.SourceParseDraftSkillId == seed.Candidates[0].Id &&
            item.InvalidatedAt == null);
        Assert.Contains(skills, item =>
            item.SkillId == seed.Skills[1].Id &&
            item.ImportanceScore == 42 &&
            item.IsAiDetected &&
            item.ProvenanceStatus == JobPostingSkillProvenanceStatus.AiActive &&
            item.SourceParseDraftId == seed.Draft.Id &&
            item.SourceParseDraftSkillId == seed.Candidates[1].Id &&
            item.InvalidatedAt == null);
        Assert.DoesNotContain(skills, item => item.SkillId == seed.Skills[2].Id);

        var persistedCandidateIds = await verification.JobPostingParseDraftSkills
            .AsNoTracking()
            .Where(item => item.JobPostingParseDraftId == seed.Draft.Id)
            .Select(item => item.Id)
            .ToListAsync(cancellation.Token);
        Assert.Equal(seed.Candidates.Select(item => item.Id).Order(),
            persistedCandidateIds.Order());

        var persistedDraft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.NotNull(persistedDraft.ConfirmedAt);
        var analysis = await verification.ResumeAnalyses
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Analysis.Id, cancellation.Token);
        Assert.Equal(initialAnalysisScore, analysis.AtsScore);
        Assert.IsType<UnavailableJobDescriptionExtractionProvider>(
            Application.Services.GetRequiredService<
                global::Api.Application.JobParsing.IJobDescriptionExtractionProvider>());
    }

    [Theory]
    [InlineData(SkillResolutionStatus.Ambiguous)]
    [InlineData(SkillResolutionStatus.Unresolved)]
    public async Task Confirm_rejects_non_resolved_candidate(
        SkillResolutionStatus resolutionStatus)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            resolutionStatus: resolutionStatus);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new() { DraftSkillId = seed.Candidates[0].Id }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Fact]
    public async Task Confirm_rejects_duplicate_draft_skill_identifier()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var selected = new SelectedJobPostingParseDraftSkillRequest
        {
            DraftSkillId = seed.Candidates[0].Id
        };
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [selected, selected],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Fact]
    public async Task Confirm_rejects_skill_from_another_draft()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var other = await SeedReadyDraftAsync(
            cancellation.Token,
            userPrefix: "other-draft");
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new() { DraftSkillId = other.Candidates[0].Id }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Confirm_rejects_invalid_importance_score(int score)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new()
            {
                DraftSkillId = seed.Candidates[0].Id,
                ImportanceScore = score
            }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Fact]
    public async Task Empty_selection_confirms_without_deleting_manual_skill()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            includeManualSkill: true);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(cancellation.Token);
        Assert.Equal(seed.Skills[0].Id, persisted.SkillId);
        Assert.Equal(17, persisted.ImportanceScore);
        Assert.Equal(JobSkillRequirementType.Mentioned, persisted.RequirementType);
        Assert.False(persisted.IsAiDetected);
        Assert.Equal(JobPostingSkillProvenanceStatus.Manual, persisted.ProvenanceStatus);
        Assert.Null(persisted.SourceParseDraftId);
        Assert.Null(persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Existing_manual_skill_is_skipped_without_overwrite_or_duplicate()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            includeManualSkill: true);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new()
            {
                DraftSkillId = seed.Candidates[0].Id,
                ImportanceScore = 99
            }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .Where(item => item.JobPostingId == seed.Posting.Id)
            .ToListAsync(cancellation.Token);
        Assert.Single(persisted);
        Assert.Equal(17, persisted[0].ImportanceScore);
        Assert.Equal(JobSkillRequirementType.Mentioned, persisted[0].RequirementType);
        Assert.False(persisted[0].IsAiDetected);
        Assert.Equal(JobPostingSkillProvenanceStatus.Manual, persisted[0].ProvenanceStatus);
        Assert.Null(persisted[0].SourceParseDraftId);
        Assert.Null(persisted[0].SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Existing_ai_active_skill_is_preserved_without_repointing_provenance()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var existing = await SeedExistingRequirementAsync(
            seed,
            JobPostingSkillProvenanceStatus.AiActive,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new()
            {
                DraftSkillId = seed.Candidates[0].Id,
                ImportanceScore = 99
            }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .Where(item => item.JobPostingId == seed.Posting.Id)
            .ToListAsync(cancellation.Token);
        Assert.Single(persisted);
        Assert.Equal(existing.Requirement.Id, persisted[0].Id);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive, persisted[0].ProvenanceStatus);
        Assert.Equal(JobSkillRequirementType.Mentioned, persisted[0].RequirementType);
        Assert.Equal(17, persisted[0].ImportanceScore);
        Assert.Equal(existing.Draft.Id, persisted[0].SourceParseDraftId);
        Assert.Equal(existing.Candidate.Id, persisted[0].SourceParseDraftSkillId);
        Assert.NotEqual(seed.Candidates[0].Id, persisted[0].SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Existing_ai_stale_skill_is_reactivated_with_current_confirmation_values()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var existing = await SeedExistingRequirementAsync(
            seed,
            JobPostingSkillProvenanceStatus.AiStale,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new()
            {
                DraftSkillId = seed.Candidates[0].Id,
                ImportanceScore = 42
            }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.JobPostingId == seed.Posting.Id,
                cancellation.Token);
        Assert.Equal(existing.Requirement.Id, persisted.Id);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive, persisted.ProvenanceStatus);
        Assert.True(persisted.IsAiDetected);
        Assert.Equal(JobSkillRequirementType.Required, persisted.RequirementType);
        Assert.Equal(42, persisted.ImportanceScore);
        Assert.Equal(seed.Draft.Id, persisted.SourceParseDraftId);
        Assert.Equal(seed.Candidates[0].Id, persisted.SourceParseDraftSkillId);
        Assert.Null(persisted.InvalidatedAt);
    }

    [Fact]
    public async Task Legacy_ai_unknown_skill_requires_explicit_selection_to_reactivate()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var existing = await SeedExistingRequirementAsync(
            seed,
            JobPostingSkillProvenanceStatus.LegacyAiUnknown,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.Id == existing.Requirement.Id,
                cancellation.Token);
        Assert.Equal(JobPostingSkillProvenanceStatus.LegacyAiUnknown,
            persisted.ProvenanceStatus);
        Assert.Equal(JobSkillRequirementType.Mentioned, persisted.RequirementType);
        Assert.Equal(17, persisted.ImportanceScore);
        Assert.Null(persisted.SourceParseDraftId);
        Assert.Null(persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Explicit_selection_reactivates_legacy_ai_unknown_with_current_provenance()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var existing = await SeedExistingRequirementAsync(
            seed,
            JobPostingSkillProvenanceStatus.LegacyAiUnknown,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new() { DraftSkillId = seed.Candidates[0].Id }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.JobPostingId == seed.Posting.Id,
                cancellation.Token);
        Assert.Equal(existing.Requirement.Id, persisted.Id);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiActive, persisted.ProvenanceStatus);
        Assert.Equal(JobSkillRequirementType.Required, persisted.RequirementType);
        Assert.Equal(seed.Candidates[0].ProposedImportanceScore,
            persisted.ImportanceScore);
        Assert.Equal(seed.Draft.Id, persisted.SourceParseDraftId);
        Assert.Equal(seed.Candidates[0].Id, persisted.SourceParseDraftSkillId);
        Assert.Null(persisted.InvalidatedAt);
    }

    [Fact]
    public async Task Existing_ai_manually_adopted_skill_is_preserved()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var existing = await SeedExistingRequirementAsync(
            seed,
            JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            cancellation.Token);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [new()
            {
                DraftSkillId = seed.Candidates[0].Id,
                ImportanceScore = 99
            }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingSkills
            .AsNoTracking()
            .SingleAsync(item => item.JobPostingId == seed.Posting.Id,
                cancellation.Token);
        Assert.Equal(existing.Requirement.Id, persisted.Id);
        Assert.Equal(JobPostingSkillProvenanceStatus.AiManuallyAdopted,
            persisted.ProvenanceStatus);
        Assert.Equal(JobSkillRequirementType.Mentioned, persisted.RequirementType);
        Assert.Equal(17, persisted.ImportanceScore);
        Assert.Equal(existing.Draft.Id, persisted.SourceParseDraftId);
        Assert.Equal(existing.Candidate.Id, persisted.SourceParseDraftSkillId);
    }

    [Fact]
    public async Task Expired_ready_draft_is_persisted_as_expired_and_not_confirmed()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Expired, draft.Status);
        Assert.Null(draft.ConfirmedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_title_or_description_supersedes_draft(
        bool changeTitle)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var posting = await context.JobPostings.SingleAsync(
                item => item.Id == seed.Posting.Id,
                cancellation.Token);
            if (changeTitle)
            {
                posting.Title = "Changed title";
            }
            else
            {
                posting.Description = "Changed description";
            }

            await context.SaveChangesAsync(cancellation.Token);
        }

        using var client = CreateClient(seed.User);
        using var response = await SendConfirmAsync(
            client,
            seed,
            [],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Superseded, draft.Status);
        Assert.Empty(await verification.JobPostingSkills.ToListAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task Stale_draft_etag_returns_precondition_failed()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var client = CreateClient(seed.User);
        using var request = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(DifferentVersion(seed.Draft.Version)),
            new ConfirmJobPostingParseDraftRequest());

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_confirmation_allows_exactly_one_success()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var firstClient = CreateClient(seed.User);
        using var secondClient = CreateClient(seed.User);
        var body = new ConfirmJobPostingParseDraftRequest
        {
            SelectedSkills =
                [new() { DraftSkillId = seed.Candidates[0].Id }]
        };
        using var firstRequest = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(seed.Draft.Version),
            body);
        using var secondRequest = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(seed.Draft.Version),
            body);

        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest, cancellation.Token),
            secondClient.SendAsync(secondRequest, cancellation.Token));
        try
        {
            Assert.Single(responses, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(
                responses,
                item => item.StatusCode == HttpStatusCode.PreconditionFailed);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await verification.JobPostingSkills.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task Cancel_and_confirm_race_has_one_consistent_winner()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var confirmClient = CreateClient(seed.User);
        using var cancelClient = CreateClient(seed.User);
        var etag = EntityTagCodec.Format(seed.Draft.Version);
        using var confirmRequest = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            etag,
            new ConfirmJobPostingParseDraftRequest
            {
                SelectedSkills =
                    [new() { DraftSkillId = seed.Candidates[0].Id }]
            });
        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{seed.Posting.Id:D}/parse-drafts/{seed.Draft.Id:D}/cancel");
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", etag);

        var responses = await Task.WhenAll(
            confirmClient.SendAsync(confirmRequest, cancellation.Token),
            cancelClient.SendAsync(cancelRequest, cancellation.Token));
        try
        {
            Assert.Single(responses, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(
                responses,
                item => item.StatusCode == HttpStatusCode.PreconditionFailed);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.Contains(
            draft.Status,
            new[]
            {
                JobPostingParseDraftStatus.Confirmed,
                JobPostingParseDraftStatus.Cancelled
            });
        var skillCount = await verification.JobPostingSkills.CountAsync(
            cancellation.Token);
        Assert.Equal(
            draft.Status == JobPostingParseDraftStatus.Confirmed ? 1 : 0,
            skillCount);
    }

    [Theory]
    [InlineData("normalized-name")]
    [InlineData("category")]
    [InlineData("unrelated-addition")]
    public async Task Changed_skill_catalog_requires_a_new_parse(string mutation)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var skill = await context.Skills.SingleAsync(
                item => item.Id == seed.Skills[0].Id,
                cancellation.Token);
            switch (mutation)
            {
                case "normalized-name":
                    skill.NormalizedName = "changed-skill";
                    break;
                case "category":
                    skill.Category = SkillCategory.DevOps;
                    break;
                default:
                    context.Skills.Add(new Skill
                    {
                        Name = "Unrelated",
                        NormalizedName = "unrelated",
                        Category = SkillCategory.Tool
                    });
                    break;
            }

            await context.SaveChangesAsync(cancellation.Token);
        }

        using var client = CreateClient(seed.User);
        using var response = await SendConfirmAsync(
            client,
            seed,
            [new() { DraftSkillId = seed.Candidates[0].Id }],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Fact]
    public async Task Invalid_second_candidate_rolls_back_entire_confirmation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(
            cancellation.Token,
            candidateCount: 2,
            secondCandidateUnresolved: true);
        using var client = CreateClient(seed.User);

        using var response = await SendConfirmAsync(
            client,
            seed,
            [
                new() { DraftSkillId = seed.Candidates[0].Id },
                new() { DraftSkillId = seed.Candidates[1].Id }
            ],
            cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoConfirmationMutationAsync(seed, cancellation.Token);
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_skills_and_confirmed_status()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(new ConfirmationPersistenceFailureInterceptor())
            .Options;
        await using var context = new AppDbContext(dbOptions);
        var service = new JobPostingParseDraftService(
            context,
            new FixedCurrentUserService(seed.User.Id),
            Application.Services.GetRequiredService<IJobParserRegistry>(),
            new JobDescriptionPreprocessor(),
            Options.Create(new JobPostingParseDraftOptions()),
            TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ConfirmDraftAsync(
            seed.Posting.Id,
            seed.Draft.Id,
            seed.Draft.Version,
            new ConfirmJobPostingParseDraftRequest
            {
                SelectedSkills =
                    [new() { DraftSkillId = seed.Candidates[0].Id }]
            },
            cancellation.Token));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Ready, draft.Status);
        Assert.Null(draft.ConfirmedAt);
        Assert.Empty(await verification.JobPostingSkills.ToListAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task Provenance_foreign_key_failure_rolls_back_skill_and_confirmation()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(new InvalidProvenanceCandidateInterceptor())
            .Options;
        await using var context = new AppDbContext(dbOptions);
        var service = new JobPostingParseDraftService(
            context,
            new FixedCurrentUserService(seed.User.Id),
            Application.Services.GetRequiredService<IJobParserRegistry>(),
            new JobDescriptionPreprocessor(),
            Options.Create(new JobPostingParseDraftOptions()),
            TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ConfirmDraftAsync(
            seed.Posting.Id,
            seed.Draft.Id,
            seed.Draft.Version,
            new ConfirmJobPostingParseDraftRequest
            {
                SelectedSkills =
                    [new() { DraftSkillId = seed.Candidates[0].Id }]
            },
            cancellation.Token));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Ready, draft.Status);
        Assert.Null(draft.ConfirmedAt);
        Assert.Empty(await verification.JobPostingSkills.ToListAsync(
            cancellation.Token));
        Assert.True(await verification.JobPostingParseDraftSkills
            .AsNoTracking()
            .AnyAsync(item => item.Id == seed.Candidates[0].Id,
                cancellation.Token));
    }

    [Fact]
    public async Task Referenced_skill_cannot_be_deleted_from_dictionary()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var skill = await context.Skills.SingleAsync(
            item => item.Id == seed.Skills[0].Id,
            cancellation.Token);
        context.Skills.Remove(skill);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(cancellation.Token));

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.True(await verification.Skills.AnyAsync(
            item => item.Id == seed.Skills[0].Id,
            cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cross_user_or_wrong_parent_confirmation_returns_not_found(
        bool crossUser)
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        var other = await SeedReadyDraftAsync(
            cancellation.Token,
            userPrefix: "other-owner");
        var caller = crossUser ? other.User : seed.User;
        var routePosting = crossUser ? seed.Posting.Id : other.Posting.Id;
        using var client = CreateClient(caller);
        using var request = CreateConfirmRequest(
            routePosting,
            seed.Draft.Id,
            EntityTagCodec.Format(seed.Draft.Version),
            new ConfirmJobPostingParseDraftRequest());

        using var response = await client.SendAsync(request, cancellation.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmed_draft_cannot_be_confirmed_again()
    {
        using var cancellation = CreateCancellation();
        var seed = await SeedReadyDraftAsync(cancellation.Token);
        using var client = CreateClient(seed.User);
        using var firstResponse = await SendConfirmAsync(
            client,
            seed,
            [],
            cancellation.Token);
        var firstBody = await firstResponse.Content
            .ReadFromJsonAsync<JobPostingParseDraftResponse>(ApiJson.Options, cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstBody);

        using var secondRequest = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(firstBody.DraftVersion),
            new ConfirmJobPostingParseDraftRequest());
        using var secondResponse = await client.SendAsync(
            secondRequest,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private async Task<ConfirmationSeed> SeedReadyDraftAsync(
        CancellationToken cancellationToken,
        int candidateCount = 1,
        SkillResolutionStatus resolutionStatus = SkillResolutionStatus.Exact,
        bool secondCandidateUnresolved = false,
        bool includeManualSkill = false,
        bool includeAnalysis = false,
        string userPrefix = "confirm-owner",
        DateTimeOffset? expiresAt = null)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser(userPrefix);
        var posting = CreatePosting(user.Id);
        var skills = Enumerable.Range(0, candidateCount)
            .Select(index => new Skill
            {
                Name = $"{userPrefix} Skill {index}",
                NormalizedName = $"{userPrefix.ToLowerInvariant()}-skill-{index}",
                Category = index == 0
                    ? SkillCategory.Backend
                    : SkillCategory.Tool
            })
            .ToList();
        context.AddRange(user, posting);
        context.Skills.AddRange(skills);
        await context.SaveChangesAsync(cancellationToken);

        if (includeManualSkill)
        {
            context.JobPostingSkills.Add(new JobPostingSkill
            {
                JobPostingId = posting.Id,
                SkillId = skills[0].Id,
                RequirementType = JobSkillRequirementType.Mentioned,
                ImportanceScore = 17,
                IsAiDetected = false
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        var catalog = skills.Select(skill => new SkillCatalogItem(
            skill.Id,
            skill.Name,
            skill.NormalizedName,
            skill.Category));
        var draft = new JobPostingParseDraft
        {
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
            SkillCatalogHash = SkillCatalogHashCalculator.Compute(catalog),
            ParserVersion = ParserVersion,
            Status = JobPostingParseDraftStatus.Ready,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1)
        };
        for (var index = 0; index < candidateCount; index++)
        {
            var candidateStatus = index == 1 && secondCandidateUnresolved
                ? SkillResolutionStatus.Unresolved
                : resolutionStatus;
            draft.Skills.Add(new JobPostingParseDraftSkill
            {
                CandidateName = skills[index].Name,
                CanonicalKey = skills[index].NormalizedName,
                ResolvedSkillId = candidateStatus is
                    SkillResolutionStatus.Exact or SkillResolutionStatus.Alias
                        ? skills[index].Id
                        : null,
                RequirementType = index == 0
                    ? JobSkillRequirementType.Required
                    : JobSkillRequirementType.Preferred,
                ProposedImportanceScore = 80 - index * 10,
                Confidence = 0.9000m,
                SortOrder = index,
                ResolutionStatus = candidateStatus
            });
        }

        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        ResumeAnalysis? analysis = null;
        if (includeAnalysis)
        {
            var resume = new Resume
            {
                UserId = user.Id,
                Title = "Existing analyzed resume"
            };
            context.Resumes.Add(resume);
            await context.SaveChangesAsync(cancellationToken);
            analysis = new ResumeAnalysis
            {
                UserId = user.Id,
                ResumeId = resume.Id,
                JobPostingId = posting.Id,
                JobPostingUserId = user.Id,
                Type = AnalysisType.JobMatch,
                AtsScore = 73.25m,
                JobMatchScore = 51.50m,
                StructureScore = 70m,
                FormattingScore = 80m,
                ContentScore = 65m,
                KeywordScore = 75m,
                AnalyzedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                AnalyzerVersion = "ats-v1"
            };
            context.ResumeAnalyses.Add(analysis);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new ConfirmationSeed(
            user,
            posting,
            draft,
            draft.Skills.OrderBy(item => item.SortOrder).ToList(),
            skills,
            analysis);
    }

    private async Task<ExistingRequirementSeed> SeedExistingRequirementAsync(
        ConfirmationSeed seed,
        JobPostingSkillProvenanceStatus provenanceStatus,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var historicalDraft = new JobPostingParseDraft
        {
            JobPostingId = seed.Posting.Id,
            UserId = seed.User.Id,
            SourceJobPostingVersion = seed.Posting.Version,
            SourceTitle = seed.Posting.Title,
            SourceDescription = seed.Posting.Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(
                seed.Posting.Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                seed.Posting.Title,
                seed.Posting.Description),
            SkillCatalogHash = seed.Draft.SkillCatalogHash,
            ParserVersion = ParserVersion,
            Status = JobPostingParseDraftStatus.Confirmed,
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(1),
            ConfirmedAt = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        var historicalCandidate = new JobPostingParseDraftSkill
        {
            CandidateName = seed.Skills[0].Name,
            CanonicalKey = seed.Skills[0].NormalizedName!,
            ResolvedSkillId = seed.Skills[0].Id,
            RequirementType = JobSkillRequirementType.Mentioned,
            ProposedImportanceScore = 17,
            Confidence = 0.8000m,
            SortOrder = 0,
            ResolutionStatus = SkillResolutionStatus.Exact
        };
        historicalDraft.Skills.Add(historicalCandidate);
        context.JobPostingParseDrafts.Add(historicalDraft);
        await context.SaveChangesAsync(cancellationToken);

        var hasSource = provenanceStatus is
            JobPostingSkillProvenanceStatus.AiActive or
            JobPostingSkillProvenanceStatus.AiStale or
            JobPostingSkillProvenanceStatus.AiManuallyAdopted;
        var requirement = new JobPostingSkill
        {
            JobPostingId = seed.Posting.Id,
            SkillId = seed.Skills[0].Id,
            RequirementType = JobSkillRequirementType.Mentioned,
            ImportanceScore = 17,
            IsAiDetected = provenanceStatus != JobPostingSkillProvenanceStatus.Manual,
            ProvenanceStatus = provenanceStatus,
            SourceParseDraftId = hasSource ? historicalDraft.Id : null,
            SourceParseDraftSkillId = hasSource ? historicalCandidate.Id : null,
            InvalidatedAt = provenanceStatus == JobPostingSkillProvenanceStatus.AiStale
                ? DateTimeOffset.UtcNow.AddMinutes(-30)
                : null
        };
        context.JobPostingSkills.Add(requirement);
        await context.SaveChangesAsync(cancellationToken);

        return new ExistingRequirementSeed(
            requirement,
            historicalDraft,
            historicalCandidate);
    }

    private HttpClient CreateClient(User user) =>
        new AuthenticatedHttpClientFactory(Application)
            .CreateForExistingUser(user);

    private static HttpRequestMessage CreateConfirmRequest(
        Guid postingId,
        Guid draftId,
        string? ifMatch,
        ConfirmJobPostingParseDraftRequest body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/job-postings/{postingId:D}/parse-drafts/{draftId:D}/confirm")
        {
            Content = JsonContent.Create(body)
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> SendConfirmAsync(
        HttpClient client,
        ConfirmationSeed seed,
        IReadOnlyList<SelectedJobPostingParseDraftSkillRequest> selected,
        CancellationToken cancellationToken)
    {
        using var request = CreateConfirmRequest(
            seed.Posting.Id,
            seed.Draft.Id,
            EntityTagCodec.Format(seed.Draft.Version),
            new ConfirmJobPostingParseDraftRequest
            {
                SelectedSkills = selected
            });
        return await client.SendAsync(request, cancellationToken);
    }

    private async Task AssertNoConfirmationMutationAsync(
        ConfirmationSeed seed,
        CancellationToken cancellationToken)
    {
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Draft.Id, cancellationToken);
        Assert.Equal(JobPostingParseDraftStatus.Ready, draft.Status);
        Assert.Null(draft.ConfirmedAt);
        Assert.Empty(await verification.JobPostingSkills
            .Where(item => item.JobPostingId == seed.Posting.Id)
            .ToListAsync(cancellationToken));
    }

    private static User CreateUser(string prefix) => new()
    {
        FirstName = "Confirmation",
        LastName = "Owner",
        Email = $"{prefix}-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreatePosting(Guid userId) => new()
    {
        UserId = userId,
        Title = "Backend Engineer",
        Description = Description,
        Status = JobPostingStatus.Draft
    };

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record ConfirmationSeed(
        User User,
        JobPosting Posting,
        JobPostingParseDraft Draft,
        IReadOnlyList<JobPostingParseDraftSkill> Candidates,
        IReadOnlyList<Skill> Skills,
        ResumeAnalysis? Analysis);

    private sealed record ExistingRequirementSeed(
        JobPostingSkill Requirement,
        JobPostingParseDraft Draft,
        JobPostingParseDraftSkill Candidate);

    private sealed class FixedCurrentUserService(Guid userId) :
        ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => "confirmation@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => userId;
    }

    private sealed class ConfirmationPersistenceFailureInterceptor :
        SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<JobPostingSkill>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException(
                    "Controlled confirmation persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class InvalidProvenanceCandidateInterceptor :
        SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var addedRequirement = eventData.Context?.ChangeTracker
                .Entries<JobPostingSkill>()
                .SingleOrDefault(entry => entry.State == EntityState.Added);
            if (addedRequirement is not null)
            {
                addedRequirement.Entity.SourceParseDraftSkillId = Guid.NewGuid();
            }

            return ValueTask.FromResult(result);
        }
    }
}
