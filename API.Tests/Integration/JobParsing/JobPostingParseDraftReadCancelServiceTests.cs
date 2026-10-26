using System.ComponentModel.DataAnnotations;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.DTOs.JobPosting;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftReadCancelServiceTests :
    IntegrationTestBase
{
    private const string Title = "Backend Engineer";
    private const string Description =
        "Strong experience with C# and ASP.NET Core is required.";
    private const string ParserVersion = "job-parser-v1";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(12);

    public JobPostingParseDraftReadCancelServiceTests(
        PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GetDraftAsync_OwnerReceivesOrderedSafeAggregate()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDetailedDraftAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var result = await service.GetDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            cancellation.Token);

        Assert.Equal(seeded.DraftId, result.DraftId);
        Assert.Equal(seeded.PostingId, result.JobPostingId);
        Assert.Equal(JobPostingParseDraftStatus.Ready, result.Status);
        Assert.Equal("test-provider", result.ProviderName);
        Assert.Equal("test-model", result.ProviderModel);
        Assert.Equal(new[] { 0, 1 }, result.Skills.Select(item => item.SortOrder));

        var unresolved = result.Skills[0];
        Assert.Null(unresolved.ResolvedSkillId);
        Assert.Null(unresolved.Confidence);
        Assert.Equal(SkillResolutionStatus.Unresolved, unresolved.ResolutionStatus);

        var resolved = result.Skills[1];
        Assert.Equal(seeded.SkillId, resolved.ResolvedSkillId);
        Assert.Equal(0.9500m, resolved.Confidence);
        Assert.Equal(new[] { 10, 30 }, resolved.Evidence.Select(item => item.Start));

        var publicPropertyNames = typeof(JobPostingParseDraftDetailResponse)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(JobPostingParseDraftSkillResponse)
                .GetProperties()
                .Select(property => property.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("UserId", publicPropertyNames);
        Assert.DoesNotContain("SourceTitle", publicPropertyNames);
        Assert.DoesNotContain("SourceDescription", publicPropertyNames);
        Assert.DoesNotContain("ProviderRequestId", publicPropertyNames);
        Assert.DoesNotContain("LeaseOwner", publicPropertyNames);
        Assert.DoesNotContain("LeaseExpiresAt", publicPropertyNames);
    }

    [Fact]
    public async Task GetDraftAsync_CrossUserDraft_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            cancellation.Token));
    }

    [Fact]
    public async Task GetDraftAsync_DraftThroughDifferentOwnedPosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        Guid otherPostingId;
        await using (var seedContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var otherPosting = CreatePosting(seeded.UserId, "Other posting");
            seedContext.JobPostings.Add(otherPosting);
            await seedContext.SaveChangesAsync(cancellation.Token);
            otherPostingId = otherPosting.Id;
        }

        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDraftAsync(
            otherPostingId,
            seeded.DraftId,
            cancellation.Token));
    }

    [Fact]
    public async Task GetDraftHistoryAsync_CrossUserPosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Failed);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetDraftHistoryAsync(
                seeded.PostingId,
                1,
                20,
                null,
                cancellation.Token));
    }

    [Fact]
    public async Task GetDraftHistoryAsync_ReturnsBoundedOwnerHistoryWithDeterministicOrder()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedHistoryAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var firstPage = await service.GetDraftHistoryAsync(
            seeded.PostingId,
            1,
            2,
            null,
            cancellation.Token);
        var secondPage = await service.GetDraftHistoryAsync(
            seeded.PostingId,
            2,
            2,
            null,
            cancellation.Token);

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(seeded.ExpectedOrder.Take(2),
            firstPage.Items.Select(item => item.DraftId));
        Assert.Equal(seeded.ExpectedOrder.Skip(2),
            secondPage.Items.Select(item => item.DraftId));

        var historyProperties = typeof(JobPostingParseDraftListItemResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Skills", historyProperties);
        Assert.DoesNotContain("Evidence", historyProperties);
        Assert.DoesNotContain("SourceTitle", historyProperties);
        Assert.DoesNotContain("SourceDescription", historyProperties);
    }

    [Fact]
    public async Task GetDraftHistoryAsync_StatusFilter_ReturnsOnlyRequestedStatus()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedHistoryAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var result = await service.GetDraftHistoryAsync(
            seeded.PostingId,
            1,
            20,
            JobPostingParseDraftStatus.Failed,
            cancellation.Token);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.Equal(JobPostingParseDraftStatus.Failed, item.Status));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task GetDraftHistoryAsync_InvalidPagination_IsRejected(
        int page,
        int pageSize)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Failed);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetDraftHistoryAsync(
                seeded.PostingId,
                page,
                pageSize,
                null,
                cancellation.Token));
    }

    [Fact]
    public async Task GetDraftHistoryAsync_UndefinedStatus_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Failed);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetDraftHistoryAsync(
                seeded.PostingId,
                1,
                20,
                (JobPostingParseDraftStatus)999,
                cancellation.Token));
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Pending)]
    [InlineData(JobPostingParseDraftStatus.Processing)]
    [InlineData(JobPostingParseDraftStatus.Ready)]
    public async Task CancelDraftAsync_AllowedState_TransitionsToCancelled(
        JobPostingParseDraftStatus sourceStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(sourceStatus);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var response = await service.CancelDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            seeded.DraftVersion,
            cancellation.Token);

        Assert.Equal(JobPostingParseDraftStatus.Cancelled, response.Status);
        Assert.NotEqual(seeded.DraftVersion, response.DraftVersion);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Cancelled, persisted.Status);
        Assert.Equal(Now, persisted.UpdatedAt);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task CancelDraftAsync_TerminalState_IsRejected(
        JobPostingParseDraftStatus terminalStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(terminalStatus);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<ConflictException>(() => service.CancelDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            seeded.DraftVersion,
            cancellation.Token));
    }

    [Fact]
    public async Task CancelDraftAsync_StaleDraftVersion_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                DifferentVersion(seeded.DraftVersion),
                cancellation.Token));
    }

    [Fact]
    public async Task CancelDraftAsync_TwoConcurrentCalls_OnlyOneSucceeds()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        var barrier = new DraftUpdateBarrier(2);
        await using var contextA = Fixture.DbContextFactory.CreateDbContext(barrier);
        await using var contextB = Fixture.DbContextFactory.CreateDbContext(barrier);
        var serviceA = CreateService(contextA, seeded.UserId);
        var serviceB = CreateService(contextB, seeded.UserId);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => serviceA.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                seeded.DraftVersion,
                cancellation.Token)),
            CaptureAsync(() => serviceB.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                seeded.DraftVersion,
                cancellation.Token)));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(
            outcomes,
            outcome => outcome is DbUpdateConcurrencyException);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Cancelled, persisted.Status);
    }

    [Fact]
    public async Task CancelDraftAsync_ClearsLeaseAndPreservesSnapshotSkillsAndEvidence()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDetailedDraftAsync(
            JobPostingParseDraftStatus.Processing);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await service.CancelDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            seeded.DraftVersion,
            cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .Include(item => item.Skills)
            .ThenInclude(item => item.Evidence)
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);

        Assert.Null(persisted.LeaseOwner);
        Assert.Null(persisted.LeaseExpiresAt);
        Assert.Null(persisted.NextAttemptAt);
        Assert.Equal(Now.AddMinutes(-2), persisted.LastAttemptAt);
        Assert.Equal(Title, persisted.SourceTitle);
        Assert.Equal(Description, persisted.SourceDescription);
        Assert.Equal(SourceInputHashCalculator.Compute(Title, Description),
            persisted.SourceInputHash);
        Assert.Equal(2, persisted.Skills.Count);
        Assert.Equal(3, persisted.Skills.Sum(item => item.Evidence.Count));
    }

    [Fact]
    public async Task CancelDraftAsync_ThenRequestSameFingerprint_IsCooldownLimited()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await service.CancelDraftAsync(
            seeded.PostingId,
            seeded.DraftId,
            seeded.DraftVersion,
            cancellation.Token);
        var exception = await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                seeded.PostingVersion,
                cancellation.Token));

        Assert.Equal(TimeSpan.FromMinutes(10), exception.RetryAfter);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var drafts = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .Where(item => item.JobPostingId == seeded.PostingId)
            .ToListAsync(cancellation.Token);
        Assert.Single(drafts);
        Assert.Equal(JobPostingParseDraftStatus.Cancelled,
            drafts.Single(item => item.Id == seeded.DraftId).Status);
        Assert.False(await verification.JobPostingSkills
            .AsNoTracking()
            .AnyAsync(item => item.JobPostingId == seeded.PostingId,
                cancellation.Token));
    }

    [Theory]
    [InlineData(CancellableOperation.Detail)]
    [InlineData(CancellableOperation.History)]
    [InlineData(CancellableOperation.Cancel)]
    public async Task DraftOperation_CancelledToken_IsPropagated(
        CancellableOperation operation)
    {
        var seeded = await SeedDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task operationTask = operation switch
        {
            CancellableOperation.Detail => service.GetDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                cancellation.Token),
            CancellableOperation.History => service.GetDraftHistoryAsync(
                seeded.PostingId,
                1,
                20,
                null,
                cancellation.Token),
            CancellableOperation.Cancel => service.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                seeded.DraftVersion,
                cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operationTask);
    }

    private async Task<SeededDraft> SeedDraftAsync(
        JobPostingParseDraftStatus status)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);

        var draft = CreateDraft(posting, status, Now.AddMinutes(-5));
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellation.Token);

        return new SeededDraft(
            user.Id,
            posting.Id,
            posting.Version,
            draft.Id,
            draft.Version,
            null);
    }

    private async Task<SeededDraft> SeedDetailedDraftAsync(
        JobPostingParseDraftStatus status = JobPostingParseDraftStatus.Ready)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        var skill = new Skill
        {
            Name = "C#",
            NormalizedName = "c#",
            Category = SkillCategory.ProgrammingLanguage
        };
        context.AddRange(user, posting, skill);
        await context.SaveChangesAsync(cancellation.Token);

        var draft = CreateDraft(posting, status, Now.AddMinutes(-5));
        draft.ProviderRequestId = "internal-provider-request";
        draft.ProviderName = "test-provider";
        draft.ProviderModel = "test-model";

        var laterSkill = new JobPostingParseDraftSkill
        {
            CandidateName = "C#",
            CanonicalKey = "c#",
            ResolvedSkillId = skill.Id,
            RequirementType = JobSkillRequirementType.Required,
            ProposedImportanceScore = 90,
            Confidence = 0.9500m,
            SortOrder = 1,
            ResolutionStatus = SkillResolutionStatus.Exact
        };
        laterSkill.Evidence.Add(new JobPostingParseDraftSkillEvidence
        {
            Text = "later evidence",
            Start = 30,
            Length = 14
        });
        laterSkill.Evidence.Add(new JobPostingParseDraftSkillEvidence
        {
            Text = "early evidence",
            Start = 10,
            Length = 14
        });

        var firstSkill = new JobPostingParseDraftSkill
        {
            CandidateName = "Unknown SDK",
            CanonicalKey = "unknown sdk",
            ResolvedSkillId = null,
            RequirementType = JobSkillRequirementType.Mentioned,
            ProposedImportanceScore = 20,
            Confidence = null,
            SortOrder = 0,
            ResolutionStatus = SkillResolutionStatus.Unresolved
        };
        firstSkill.Evidence.Add(new JobPostingParseDraftSkillEvidence
        {
            Text = "unknown sdk",
            Start = 45,
            Length = 11
        });

        draft.Skills.Add(laterSkill);
        draft.Skills.Add(firstSkill);
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellation.Token);

        return new SeededDraft(
            user.Id,
            posting.Id,
            posting.Version,
            draft.Id,
            draft.Version,
            skill.Id);
    }

    private async Task<SeededHistory> SeedHistoryAsync()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);

        var drafts = new[]
        {
            CreateDraft(posting, JobPostingParseDraftStatus.Failed,
                Now.AddMinutes(-4), Guid.Parse("00000000-0000-0000-0000-000000000001")),
            CreateDraft(posting, JobPostingParseDraftStatus.Cancelled,
                Now.AddMinutes(-3), Guid.Parse("00000000-0000-0000-0000-000000000002")),
            CreateDraft(posting, JobPostingParseDraftStatus.Confirmed,
                Now.AddMinutes(-2), Guid.Parse("00000000-0000-0000-0000-000000000003")),
            CreateDraft(posting, JobPostingParseDraftStatus.Failed,
                Now.AddMinutes(-2), Guid.Parse("00000000-0000-0000-0000-000000000004"))
        };
        context.JobPostingParseDrafts.AddRange(drafts);
        await context.SaveChangesAsync(cancellation.Token);

        return new SeededHistory(
            user.Id,
            posting.Id,
            new[] { drafts[3].Id, drafts[2].Id, drafts[1].Id, drafts[0].Id });
    }

    private static JobPostingParseDraftService CreateService(
        AppDbContext context,
        Guid userId)
    {
        var parser = new NonInvokableParser();
        return new JobPostingParseDraftService(
            context,
            new TestCurrentUserService(userId),
            new TestParserRegistry(parser),
            new JobDescriptionPreprocessor(),
            Options.Create(new JobPostingParseDraftOptions
            {
                Lifetime = DraftLifetime
            }),
            new FixedTimeProvider(Now));
    }

    private static User CreateUser() => new()
    {
        FirstName = "Draft",
        LastName = "Owner",
        Email = $"draft-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreatePosting(Guid userId, string? title = null) =>
        new()
        {
            UserId = userId,
            Title = title ?? Title,
            Description = Description,
            Status = JobPostingStatus.Draft
        };

    private static JobPostingParseDraft CreateDraft(
        JobPosting posting,
        JobPostingParseDraftStatus status,
        DateTimeOffset requestedAt,
        Guid? id = null)
    {
        var sourceTitle = posting.Title;
        var draft = new JobPostingParseDraft
        {
            Id = id ?? Guid.NewGuid(),
            JobPostingId = posting.Id,
            UserId = posting.UserId,
            SourceJobPostingVersion = posting.Version,
            SourceTitle = sourceTitle,
            SourceDescription = Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                sourceTitle,
                Description),
            ParserVersion = ParserVersion,
            Status = status,
            RequestedAt = requestedAt,
            ExpiresAt = Now.AddHours(1)
        };

        if (status == JobPostingParseDraftStatus.Processing)
        {
            draft.AttemptCount = 1;
            draft.LeaseOwner = "worker-1";
            draft.LastAttemptAt = Now.AddMinutes(-2);
            draft.LeaseExpiresAt = Now.AddMinutes(2);
            draft.NextAttemptAt = null;
        }

        return draft;
    }

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeededDraft(
        Guid UserId,
        Guid PostingId,
        uint PostingVersion,
        Guid DraftId,
        uint DraftVersion,
        Guid? SkillId);

    private sealed record SeededHistory(
        Guid UserId,
        Guid PostingId,
        IReadOnlyList<Guid> ExpectedOrder);

    public enum CancellableOperation
    {
        Detail,
        History,
        Cancel
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "draft-test@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => UserId!.Value;
    }

    private sealed class TestParserRegistry : IJobParserRegistry
    {
        public TestParserRegistry(IJobDescriptionParser parser)
        {
            Current = parser;
        }

        public IJobDescriptionParser Current { get; }

        public IJobDescriptionParser GetRequired(string parserVersion) =>
            string.Equals(parserVersion, Current.ParserVersion,
                StringComparison.Ordinal)
                ? Current
                : throw new InvalidOperationException("Parser was not found.");
    }

    private sealed class NonInvokableParser : IJobDescriptionParser
    {
        public string ParserVersion =>
            JobPostingParseDraftReadCancelServiceTests.ParserVersion;

        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Draft application operations must not invoke the parser.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class DraftUpdateBarrier : SaveChangesInterceptor
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _allArrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public DraftUpdateBarrier(int participants)
        {
            _participants = participants;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null &&
                eventData.Context.ChangeTracker
                    .Entries<JobPostingParseDraft>()
                    .Any(entry => entry.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref _arrived) == _participants)
                {
                    _allArrived.TrySetResult();
                }

                await _allArrived.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
