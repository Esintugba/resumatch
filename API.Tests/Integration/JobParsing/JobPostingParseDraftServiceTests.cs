using System.Collections.Immutable;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftServiceTests : IntegrationTestBase
{
    private const string Title = "  Backend Engineer  ";
    private const string Description =
        "  Strong experience with C# and ASP.NET Core is required.\r\n";
    private const string ParserVersion = "job-parser-v1";
    private const string NormalizedEmailIndex = "IX_Users_NormalizedEmail";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(12);

    public JobPostingParseDraftServiceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RequestParseAsync_OwnerAndCurrentVersion_CreatesPendingImmutableDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        await using var serviceContext = Fixture.DbContextFactory.CreateDbContext();
        var parser = new CountingParser();
        var service = CreateService(serviceContext, seeded.UserId, parser);

        var response = await service.RequestParseAsync(
            seeded.PostingId,
            seeded.Version,
            cancellation.Token);

        Assert.False(response.IsReused);
        Assert.Equal(JobPostingParseDraftStatus.Pending, response.Status);
        Assert.Equal(ParserVersion, response.ParserVersion);
        Assert.NotEqual(0u, response.DraftVersion);
        Assert.Equal(Now, response.RequestedAt);
        Assert.Equal(Now.Add(DraftLifetime), response.ExpiresAt);
        Assert.Equal(0, parser.CallCount);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .Include(item => item.Skills)
            .SingleAsync(item => item.Id == response.DraftId, cancellation.Token);

        Assert.Equal(seeded.UserId, draft.UserId);
        Assert.Equal(seeded.PostingId, draft.JobPostingId);
        Assert.Equal(seeded.Version, draft.SourceJobPostingVersion);
        Assert.Equal(Title, draft.SourceTitle);
        Assert.Equal(Description, draft.SourceDescription);
        Assert.Equal(
            DescriptionHashCalculator.Compute(Description),
            draft.SourceDescriptionHash);
        Assert.Equal(
            SourceInputHashCalculator.Compute(Title, Description),
            draft.SourceInputHash);
        Assert.Null(draft.SkillCatalogHash);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Null(draft.FailureCode);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
        Assert.Null(draft.LastAttemptAt);
        Assert.Null(draft.NextAttemptAt);
        Assert.Null(draft.CompletedAt);
        Assert.Null(draft.ConfirmedAt);
        Assert.Empty(draft.Skills);
        Assert.False(await verification.JobPostingSkills.AnyAsync(
            cancellation.Token));

        var posting = await verification.JobPostings
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.PostingId, cancellation.Token);
        Assert.Equal(Description, posting.Description);
        Assert.Null(posting.ParsedSummary);
    }

    [Fact]
    public async Task RequestParseAsync_CrossUserPosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_MissingPosting_ReturnsNotFound()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.RequestParseAsync(
                Guid.NewGuid(),
                1,
                cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_StalePostingVersion_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                DifferentVersion(seeded.Version),
                cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_StaleVersion_DoesNotReuseExistingDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(
            JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                DifferentVersion(seeded.Version),
                cancellation.Token));
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Pending)]
    [InlineData(JobPostingParseDraftStatus.Processing)]
    [InlineData(JobPostingParseDraftStatus.Ready)]
    public async Task RequestParseAsync_ReusableActiveDraft_ReturnsSameSnapshot(
        JobPostingParseDraftStatus status)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(status);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var response = await service.RequestParseAsync(
            seeded.PostingId,
            seeded.Version,
            cancellation.Token);

        Assert.True(response.IsReused);
        Assert.Equal(seeded.DraftId, response.DraftId);
        Assert.Equal(status, response.Status);
        Assert.Equal(1, await context.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_ExpiredReadyDraft_ExpiresOldAndCreatesPending()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(
            JobPostingParseDraftStatus.Ready,
            expiresAt: Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var response = await service.RequestParseAsync(
            seeded.PostingId,
            seeded.Version,
            cancellation.Token);

        Assert.False(response.IsReused);
        Assert.NotEqual(seeded.DraftId, response.DraftId);
        Assert.Equal(JobPostingParseDraftStatus.Pending, response.Status);
        var drafts = await context.JobPostingParseDrafts
            .AsNoTracking()
            .OrderBy(item => item.RequestedAt)
            .ToListAsync(cancellation.Token);
        Assert.Equal(2, drafts.Count);
        Assert.Equal(
            JobPostingParseDraftStatus.Expired,
            drafts.Single(item => item.Id == seeded.DraftId).Status);
    }

    [Theory]
    [InlineData(JobPostingParseDraftStatus.Failed)]
    [InlineData(JobPostingParseDraftStatus.Confirmed)]
    [InlineData(JobPostingParseDraftStatus.Superseded)]
    [InlineData(JobPostingParseDraftStatus.Expired)]
    [InlineData(JobPostingParseDraftStatus.Cancelled)]
    public async Task RequestParseAsync_TerminalHistory_CreatesNewPendingDraft(
        JobPostingParseDraftStatus terminalStatus)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(terminalStatus);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var response = await service.RequestParseAsync(
            seeded.PostingId,
            seeded.Version,
            cancellation.Token);

        Assert.False(response.IsReused);
        Assert.NotEqual(seeded.DraftId, response.DraftId);
        Assert.Equal(JobPostingParseDraftStatus.Pending, response.Status);
    }

    [Fact]
    public async Task RequestParseAsync_RecentTerminalFingerprint_IsRateLimitedByCooldown()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(
            JobPostingParseDraftStatus.Cancelled,
            requestedAt: Now.AddMinutes(-5));
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var exception = await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token));

        Assert.Equal(TimeSpan.FromMinutes(5), exception.RetryAfter);
        Assert.Equal(1, await context.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_CooldownElapsed_CreatesNewPendingDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(
            JobPostingParseDraftStatus.Cancelled,
            requestedAt: Now.AddMinutes(-11));
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);

        var response = await service.RequestParseAsync(
            seeded.PostingId,
            seeded.Version,
            cancellation.Token);

        Assert.False(response.IsReused);
        Assert.Equal(JobPostingParseDraftStatus.Pending, response.Status);
        Assert.Equal(2, await context.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_ConcurrentRecentTerminalRequests_CannotBypassCooldown()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingWithDraftAsync(
            JobPostingParseDraftStatus.Cancelled,
            requestedAt: Now.AddMinutes(-1));
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext();
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext();
        var firstService = CreateService(firstContext, seeded.UserId);
        var secondService = CreateService(secondContext, seeded.UserId);

        var results = await Task.WhenAll(
            CaptureCooldownAsync(firstService, seeded, cancellation.Token),
            CaptureCooldownAsync(secondService, seeded, cancellation.Token));

        Assert.All(results, Assert.True);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await verification.JobPostingParseDrafts.CountAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_TitleChange_ProducesNewFingerprint()
    {
        var (first, second) = await RequestBeforeAndAfterPostingChangeAsync(
            posting => posting.Title = "Platform Engineer");

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.SourceInputHash, second.SourceInputHash);
        Assert.Equal(first.SourceDescriptionHash, second.SourceDescriptionHash);
    }

    [Fact]
    public async Task RequestParseAsync_DescriptionChange_ProducesNewFingerprint()
    {
        var (first, second) = await RequestBeforeAndAfterPostingChangeAsync(
            posting => posting.Description = "Go and Kubernetes are required.");

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.SourceInputHash, second.SourceInputHash);
        Assert.NotEqual(first.SourceDescriptionHash, second.SourceDescriptionHash);
    }

    [Fact]
    public async Task RequestParseAsync_ConcurrentRequests_ReturnSameWinningDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        var barrier = new DraftInsertBarrier(2);
        await using var firstContext = Fixture.DbContextFactory.CreateDbContext(barrier);
        await using var secondContext = Fixture.DbContextFactory.CreateDbContext(barrier);
        var firstService = CreateService(firstContext, seeded.UserId);
        var secondService = CreateService(secondContext, seeded.UserId);

        var responses = await Task.WhenAll(
            firstService.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token),
            secondService.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token));

        Assert.Equal(responses[0].DraftId, responses[1].DraftId);
        Assert.Single(responses, item => !item.IsReused);
        Assert.Single(responses, item => item.IsReused);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(1, await verification.JobPostingParseDrafts
            .CountAsync(cancellation.Token));
    }

    [Fact]
    public async Task RequestParseAsync_UnrelatedUniqueViolation_IsNotSwallowed()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        context.Users.Add(CreateUser(seeded.Email.ToUpperInvariant()));
        var service = CreateService(context, seeded.UserId);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token));

        var postgres = FindPostgresException(exception);
        Assert.NotNull(postgres);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal(NormalizedEmailIndex, postgres.ConstraintName);
    }

    [Fact]
    public async Task RequestParseAsync_CancelledToken_PropagatesCancellation()
    {
        var seeded = await SeedPostingAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateService(context, seeded.UserId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token));
    }

    private async Task<(JobPostingParseDraft First, JobPostingParseDraft Second)>
        RequestBeforeAndAfterPostingChangeAsync(Action<JobPosting> change)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedPostingAsync();
        await using (var firstContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var service = CreateService(firstContext, seeded.UserId);
            await service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellation.Token);
        }

        uint updatedVersion;
        await using (var updateContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var posting = await updateContext.JobPostings.SingleAsync(
                item => item.Id == seeded.PostingId,
                cancellation.Token);
            change(posting);
            await updateContext.SaveChangesAsync(cancellation.Token);
            updatedVersion = posting.Version;
        }

        await using (var secondContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var service = CreateService(secondContext, seeded.UserId);
            await service.RequestParseAsync(
                seeded.PostingId,
                updatedVersion,
                cancellation.Token);
        }

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var drafts = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellation.Token);
        Assert.Equal(2, drafts.Count);
        return (drafts[0], drafts[1]);
    }

    private async Task<SeededPosting> SeedPostingAsync()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);
        return new SeededPosting(
            user.Id,
            user.Email,
            posting.Id,
            posting.Version,
            null);
    }

    private async Task<SeededPosting> SeedPostingWithDraftAsync(
        JobPostingParseDraftStatus status,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? requestedAt = null)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);
        var draft = CreateDraft(posting, status, expiresAt, requestedAt);
        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellation.Token);
        return new SeededPosting(
            user.Id,
            user.Email,
            posting.Id,
            posting.Version,
            draft.Id);
    }

    private static JobPostingParseDraftService CreateService(
        AppDbContext context,
        Guid userId,
        CountingParser? parser = null)
    {
        parser ??= new CountingParser();
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

    private static User CreateUser(string? email = null) => new()
    {
        FirstName = "Parse",
        LastName = "Owner",
        Email = email ?? $"parse-{Guid.NewGuid():N}@example.com",
        PasswordHash = "integration-test-hash",
        IsActive = true
    };

    private static JobPosting CreatePosting(Guid userId) => new()
    {
        UserId = userId,
        Title = Title,
        Description = Description,
        Status = JobPostingStatus.Draft
    };

    private static JobPostingParseDraft CreateDraft(
        JobPosting posting,
        JobPostingParseDraftStatus status,
        DateTimeOffset? expiresAt,
        DateTimeOffset? requestedAt = null) 
    {
        var draft = new JobPostingParseDraft
        {
            JobPostingId = posting.Id,
            UserId = posting.UserId,
            SourceJobPostingVersion = posting.Version,
            SourceTitle = Title,
            SourceDescription = Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(Description),
            SourceInputHash = SourceInputHashCalculator.Compute(Title, Description),
            ParserVersion = ParserVersion,
            Status = status,
            RequestedAt = requestedAt ?? Now.AddMinutes(-15),
            ExpiresAt = expiresAt ?? Now.AddHours(1)
        };

        if (status == JobPostingParseDraftStatus.Processing)
        {
            draft.LeaseOwner = "worker-1";
            draft.LastAttemptAt = Now.AddMinutes(-2);
            draft.LeaseExpiresAt = Now.AddMinutes(2);
        }

        else if (status == JobPostingParseDraftStatus.Ready)
        {
            draft.CompletedAt = Now.AddMinutes(-15);
        }

        return draft;
    }

    private static async Task<bool> CaptureCooldownAsync(
        JobPostingParseDraftService service,
        SeededPosting seeded,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.RequestParseAsync(
                seeded.PostingId,
                seeded.Version,
                cancellationToken);
            return false;
        }
        catch (TooManyRequestsException)
        {
            return true;
        }
    }

    private static uint DifferentVersion(uint version) =>
        version == uint.MaxValue ? version - 1 : version + 1;

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeededPosting(
        Guid UserId,
        string Email,
        Guid PostingId,
        uint Version,
        Guid? DraftId);

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "parse-test@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => UserId!.Value;
    }

    private sealed class TestParserRegistry : IJobParserRegistry
    {
        public TestParserRegistry(CountingParser parser)
        {
            Current = parser;
        }

        public IJobDescriptionParser Current { get; }

        public IJobDescriptionParser GetRequired(string parserVersion)
        {
            if (!string.Equals(
                    parserVersion,
                    Current.ParserVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Parser was not found.");
            }

            return Current;
        }
    }

    private sealed class CountingParser : IJobDescriptionParser
    {
        public string ParserVersion => JobPostingParseDraftServiceTests.ParserVersion;
        public int CallCount { get; private set; }

        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException(
                "RequestParseAsync must not invoke the parser.");
        }
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

    private sealed class DraftInsertBarrier : SaveChangesInterceptor
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _allArrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public DraftInsertBarrier(int participants)
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
                    .Any(entry => entry.State == EntityState.Added))
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
