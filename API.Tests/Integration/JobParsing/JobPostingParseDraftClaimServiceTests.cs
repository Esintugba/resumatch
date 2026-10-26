using System.ComponentModel.DataAnnotations;
using System.Data;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftClaimServiceTests :
    IntegrationTestBase
{
    private const string ParserVersion = "job-parser-v1";
    private const string Description =
        "Strong experience with C# and ASP.NET Core is required.";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public JobPostingParseDraftClaimServiceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ClaimNextAsync_PendingDraft_AtomicallyCreatesFencedLease()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        var claim = await service.ClaimNextAsync("worker-a", cancellation.Token);

        Assert.NotNull(claim);
        Assert.Equal(seeded.DraftId, claim.DraftId);
        Assert.Equal("worker-a", claim.LeaseOwner);
        Assert.Equal(1, claim.AttemptCount);
        Assert.Equal(Now, claim.ClaimedAt);
        Assert.Equal(Now.Add(LeaseDuration), claim.LeaseExpiresAt);
        Assert.NotEqual(seeded.DraftVersion, claim.DraftVersion);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var persisted = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Processing, persisted.Status);
        Assert.Equal(claim.DraftVersion, persisted.Version);
        Assert.Equal(claim.AttemptCount, persisted.AttemptCount);
        Assert.Null(persisted.NextAttemptAt);
        Assert.False(await verification.JobPostingSkills.AnyAsync(
            cancellation.Token));

        var constructorDependencies = typeof(JobPostingParseDraftClaimService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.DoesNotContain(typeof(IJobDescriptionParser), constructorDependencies);
        Assert.DoesNotContain(
            typeof(IJobDescriptionExtractionProvider),
            constructorDependencies);
        Assert.DoesNotContain(typeof(ICurrentUserService), constructorDependencies);
    }

    [Fact]
    public async Task ClaimNextAsync_FutureNextAttempt_ReturnsNoWork()
    {
        using var cancellation = CreateCancellation();
        await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Pending,
            nextAttemptAt: Now.AddMinutes(1));
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        var claim = await service.ClaimNextAsync("worker-a", cancellation.Token);

        Assert.Null(claim);
    }

    [Fact]
    public async Task ClaimNextAsync_ProviderExecutionDisabled_DoesNotConsumeAttemptOrCreateLeaks()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(
            context,
            providerExecutionEnabled: false);

        var claim = await service.ClaimNextAsync("worker-a", cancellation.Token);

        Assert.Null(claim);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
        Assert.False(await verification.AiUsageReservations.AnyAsync(
            cancellation.Token));
        Assert.False(await verification.AiProviderConcurrencyLeases.AnyAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task ClaimNextAsync_DueNextAttempt_ClaimsDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Pending,
            nextAttemptAt: Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        var claim = await service.ClaimNextAsync("worker-a", cancellation.Token);

        Assert.NotNull(claim);
        Assert.Equal(seeded.DraftId, claim.DraftId);
    }

    [Fact]
    public async Task ClaimNextAsync_MultipleDrafts_UsesRequestedAtThenIdOrder()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedOrderedDraftsAsync();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        var first = await service.ClaimNextAsync("worker-a", cancellation.Token);
        var second = await service.ClaimNextAsync("worker-a", cancellation.Token);
        var third = await service.ClaimNextAsync("worker-a", cancellation.Token);

        Assert.Equal(seeded.ExpectedOrder,
            new[] { first!.DraftId, second!.DraftId, third!.DraftId });
    }

    [Fact]
    public async Task ClaimNextAsync_TwoWorkers_OnlyOneClaimsSingleDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var contextA = Fixture.DbContextFactory.CreateDbContext();
        await using var contextB = Fixture.DbContextFactory.CreateDbContext();
        var serviceA = CreateClaimService(contextA);
        var serviceB = CreateClaimService(contextB);

        var claims = await Task.WhenAll(
            serviceA.ClaimNextAsync("worker-a", cancellation.Token),
            serviceB.ClaimNextAsync("worker-b", cancellation.Token));

        var successful = Assert.Single(claims, claim => claim is not null);
        Assert.Equal(seeded.DraftId, successful!.DraftId);
        Assert.Single(claims, claim => claim is null);
    }

    [Fact]
    public async Task ClaimNextAsync_LockedOldestDraft_SkipsItWithoutWaiting()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seeded = await SeedTwoDraftsAsync();
        await using var lockContext = Fixture.DbContextFactory.CreateDbContext();
        await using var lockTransaction = await lockContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation.Token);
        await LockDraftAsync(
            lockContext,
            lockTransaction,
            seeded.FirstDraftId,
            cancellation.Token);

        await using var claimContext = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(claimContext);
        var claim = await service.ClaimNextAsync("worker-b", cancellation.Token);

        Assert.NotNull(claim);
        Assert.Equal(seeded.SecondDraftId, claim.DraftId);
        await lockTransaction.RollbackAsync(cancellation.Token);
    }

    [Fact]
    public async Task ClaimNextAsync_ActiveProcessingLease_IsNotReclaimed()
    {
        using var cancellation = CreateCancellation();
        await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Processing,
            attemptCount: 1,
            leaseOwner: "worker-a",
            leaseExpiresAt: Now.AddMinutes(1));
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        Assert.Null(await service.ClaimNextAsync("worker-b", cancellation.Token));
    }

    [Fact]
    public async Task ClaimNextAsync_ExpiredLease_ReclaimsAndInvalidatesOldToken()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Processing,
            attemptCount: 1,
            leaseOwner: "worker-a",
            leaseExpiresAt: Now.AddSeconds(-1));
        var oldClaim = seeded.ToClaim(
            "worker-a",
            1,
            Now.AddMinutes(-5),
            Now.AddSeconds(-1));
        await using var reclaimContext = Fixture.DbContextFactory.CreateDbContext();
        var reclaimService = CreateClaimService(reclaimContext);

        var reclaimed = await reclaimService.ClaimNextAsync(
            "worker-b",
            cancellation.Token);

        Assert.NotNull(reclaimed);
        Assert.Equal(seeded.DraftId, reclaimed.DraftId);
        Assert.Equal("worker-b", reclaimed.LeaseOwner);
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.NotEqual(oldClaim.DraftVersion, reclaimed.DraftVersion);

        await using var staleContext = Fixture.DbContextFactory.CreateDbContext();
        var staleService = CreateClaimService(staleContext);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            staleService.MarkFailedAsync(
                oldClaim,
                NonRetryableFailure(),
                cancellation.Token));
    }

    [Fact]
    public async Task RenewLeaseAsync_ValidOwnerAndToken_ExtendsLeaseAndVersion()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        var renewed = await service.RenewLeaseAsync(claim, cancellation.Token);

        Assert.Equal(claim.DraftId, renewed.DraftId);
        Assert.Equal(claim.AttemptCount, renewed.AttemptCount);
        Assert.Equal(claim.LeaseOwner, renewed.LeaseOwner);
        Assert.True(renewed.LeaseExpiresAt > claim.LeaseExpiresAt);
        Assert.NotEqual(claim.DraftVersion, renewed.DraftVersion);
    }

    [Fact]
    public async Task RenewLeaseAsync_DifferentOwner_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.RenewLeaseAsync(
                claim with { LeaseOwner = "worker-b" },
                cancellation.Token));
    }

    [Fact]
    public async Task RenewLeaseAsync_StaleXmin_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);
        await service.RenewLeaseAsync(claim, cancellation.Token);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.RenewLeaseAsync(claim, cancellation.Token));
    }

    [Fact]
    public async Task RenewLeaseAsync_ExpiredLease_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Processing,
            attemptCount: 1,
            leaseOwner: "worker-a",
            leaseExpiresAt: Now);
        var claim = seeded.ToClaim("worker-a", 1, Now.AddMinutes(-5), Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.RenewLeaseAsync(claim, cancellation.Token));
    }

    [Fact]
    public async Task ReleaseForRetryAsync_ValidClaim_ReturnsPendingAndSchedulesRetry()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);
        var nextAttempt = Now.AddMinutes(2);

        await service.ReleaseForRetryAsync(
            claim,
            RetryableFailure(),
            nextAttempt,
            cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == claim.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(claim.AttemptCount, draft.AttemptCount);
        Assert.Equal(nextAttempt, draft.NextAttemptAt);
        Assert.Equal(JobParseFailureClassifier.ProviderTimeoutCode,
            draft.FailureCode);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
        Assert.Null(draft.CompletedAt);
    }

    [Fact]
    public async Task ReleaseForRetryAsync_MaximumAttempts_MarksRetryExhausted()
    {
        using var cancellation = CreateCancellation();
        await SeedSingleDraftAsync(
            JobPostingParseDraftStatus.Pending,
            attemptCount: 1);
        await using var claimContext = Fixture.DbContextFactory.CreateDbContext();
        var claimService = CreateClaimService(claimContext, maximumAttempts: 2);
        var claim = await claimService.ClaimNextAsync(
            "worker-a",
            cancellation.Token);
        Assert.NotNull(claim);

        await using var mutationContext = Fixture.DbContextFactory.CreateDbContext();
        var mutationService = CreateClaimService(
            mutationContext,
            maximumAttempts: 2);
        await mutationService.ReleaseForRetryAsync(
            claim,
            RetryableFailure(),
            Now.AddMinutes(2),
            cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == claim.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Failed, draft.Status);
        Assert.Equal(JobParseFailureClassifier.RetryExhaustedCode,
            draft.FailureCode);
        Assert.Equal(Now, draft.CompletedAt);
        Assert.Null(draft.NextAttemptAt);
        Assert.Null(draft.LeaseOwner);
    }

    [Fact]
    public async Task ReleaseForBudgetWaitAsync_ReturnsPendingWithoutConsumingAttempt()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context, maximumAttempts: 1);
        var nextAttempt = Now.AddDays(1);

        await service.ReleaseForBudgetWaitAsync(
            claim,
            JobPostingParseDraftClaimService.UserBudgetExhaustedCode,
            nextAttempt,
            cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == claim.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Equal(nextAttempt, draft.NextAttemptAt);
        Assert.Equal(JobPostingParseDraftClaimService.UserBudgetExhaustedCode,
            draft.FailureCode);
    }

    [Fact]
    public async Task ReleaseForRetryAsync_ForgedRetryClassification_IsRejected()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);
        var forgedFailure = new JobParseFailureClassification(
            JobParseFailureClassifier.SchemaValidationFailedCode,
            true);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReleaseForRetryAsync(
                claim,
                forgedFailure,
                Now.AddMinutes(2),
                cancellation.Token));
    }

    [Fact]
    public async Task MarkFailedAsync_ValidClaim_CreatesTerminalFailure()
    {
        using var cancellation = CreateCancellation();
        var claim = await SeedAndClaimAsync("worker-a", cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        await service.MarkFailedAsync(
            claim,
            NonRetryableFailure(),
            cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == claim.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Failed, draft.Status);
        Assert.Equal(JobParseFailureClassifier.SchemaValidationFailedCode,
            draft.FailureCode);
        Assert.Equal(Now, draft.CompletedAt);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
        Assert.Null(draft.NextAttemptAt);
    }

    [Fact]
    public async Task WorkerMutation_CancelledDraftAndOldClaim_AreRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var claimContext = Fixture.DbContextFactory.CreateDbContext();
        var claim = await CreateClaimService(claimContext).ClaimNextAsync(
            "worker-a",
            cancellation.Token);
        Assert.NotNull(claim);

        await using (var cancelContext = Fixture.DbContextFactory.CreateDbContext())
        {
            var cancelService = CreateDraftService(cancelContext, seeded.UserId);
            await cancelService.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                claim.DraftVersion,
                cancellation.Token);
        }

        await using var workerContext = Fixture.DbContextFactory.CreateDbContext();
        var workerService = CreateClaimService(workerContext);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            workerService.MarkFailedAsync(
                claim,
                NonRetryableFailure(),
                cancellation.Token));
    }

    [Fact]
    public async Task CancelAndWorkerMutation_ConcurrentRace_AllowsOnlyOneWrite()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var initialContext = Fixture.DbContextFactory.CreateDbContext();
        var claim = await CreateClaimService(initialContext).ClaimNextAsync(
            "worker-a",
            cancellation.Token);
        Assert.NotNull(claim);

        var barrier = new DraftUpdateBarrier(2);
        await using var cancelContext =
            Fixture.DbContextFactory.CreateDbContext(barrier);
        await using var workerContext =
            Fixture.DbContextFactory.CreateDbContext(barrier);
        var cancelService = CreateDraftService(cancelContext, seeded.UserId);
        var workerService = CreateClaimService(workerContext);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => cancelService.CancelDraftAsync(
                seeded.PostingId,
                seeded.DraftId,
                claim.DraftVersion,
                cancellation.Token)),
            CaptureAsync(() => workerService.MarkFailedAsync(
                claim,
                NonRetryableFailure(),
                cancellation.Token)));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes,
            outcome => outcome is DbUpdateConcurrencyException);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == claim.DraftId, cancellation.Token);
        Assert.True(draft.Status is JobPostingParseDraftStatus.Cancelled or
            JobPostingParseDraftStatus.Failed);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
    }

    [Fact]
    public async Task ClaimNextAsync_DatabaseFailure_RollsBackPendingDraft()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await InstallClaimFailureTriggerAsync(cancellation.Token);

        try
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            var service = CreateClaimService(context);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.ClaimNextAsync("worker-a", cancellation.Token));

            await using var verification = Fixture.DbContextFactory.CreateDbContext();
            var draft = await verification.JobPostingParseDrafts
                .AsNoTracking()
                .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
            Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
            Assert.Equal(0, draft.AttemptCount);
            Assert.Null(draft.LeaseOwner);
            Assert.Null(draft.LeaseExpiresAt);
        }
        finally
        {
            await RemoveClaimFailureTriggerAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ClaimNextAsync_InvalidLeaseOwner_IsRejected(string? leaseOwner)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ClaimNextAsync(leaseOwner!, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimNextAsync_CancelledToken_IsPropagated()
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var service = CreateClaimService(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ClaimNextAsync("worker-a", cancellation.Token));
    }

    private async Task<SeededDraft> SeedSingleDraftAsync(
        JobPostingParseDraftStatus status,
        DateTimeOffset? nextAttemptAt = null,
        int attemptCount = 0,
        string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null)
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);

        var draft = CreateDraft(
            posting,
            status,
            "single",
            Now.AddMinutes(-10));
        draft.NextAttemptAt = nextAttemptAt;
        draft.AttemptCount = attemptCount;
        if (status == JobPostingParseDraftStatus.Processing)
        {
            draft.LeaseOwner = leaseOwner ?? "worker-seed";
            draft.LastAttemptAt = Now.AddMinutes(-5);
            draft.LeaseExpiresAt = leaseExpiresAt ?? Now.AddMinutes(1);
        }

        context.JobPostingParseDrafts.Add(draft);
        await context.SaveChangesAsync(cancellation.Token);
        return new SeededDraft(
            user.Id,
            posting.Id,
            posting.Version,
            draft.Id,
            draft.Version);
    }

    private async Task<JobPostingParseDraftClaim> SeedAndClaimAsync(
        string worker,
        CancellationToken cancellationToken)
    {
        await SeedSingleDraftAsync(JobPostingParseDraftStatus.Pending);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var claim = await CreateClaimService(context).ClaimNextAsync(
            worker,
            cancellationToken);
        return Assert.IsType<JobPostingParseDraftClaim>(claim);
    }

    private async Task<SeededOrder> SeedOrderedDraftsAsync()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);

        var newest = CreateDraft(
            posting,
            JobPostingParseDraftStatus.Pending,
            "newest",
            Now.AddMinutes(-1),
            Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var tiedHigh = CreateDraft(
            posting,
            JobPostingParseDraftStatus.Pending,
            "tie-high",
            Now.AddMinutes(-5),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var tiedLow = CreateDraft(
            posting,
            JobPostingParseDraftStatus.Pending,
            "tie-low",
            Now.AddMinutes(-5),
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        context.AddRange(newest, tiedHigh, tiedLow);
        await context.SaveChangesAsync(cancellation.Token);

        return new SeededOrder(new[] { tiedLow.Id, tiedHigh.Id, newest.Id });
    }

    private async Task<SeededPair> SeedTwoDraftsAsync()
    {
        using var cancellation = CreateCancellation();
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = CreateUser();
        var posting = CreatePosting(user.Id);
        context.AddRange(user, posting);
        await context.SaveChangesAsync(cancellation.Token);
        var first = CreateDraft(
            posting,
            JobPostingParseDraftStatus.Pending,
            "first",
            Now.AddMinutes(-5));
        var second = CreateDraft(
            posting,
            JobPostingParseDraftStatus.Pending,
            "second",
            Now.AddMinutes(-4));
        context.AddRange(first, second);
        await context.SaveChangesAsync(cancellation.Token);
        return new SeededPair(first.Id, second.Id);
    }

    private static JobPostingParseDraftClaimService CreateClaimService(
        AppDbContext context,
        int maximumAttempts = 3,
        bool providerExecutionEnabled = true) =>
        new(
            context,
            Options.Create(new JobPostingParseDraftOptions
            {
                Lifetime = TimeSpan.FromHours(12),
                LeaseDuration = LeaseDuration,
                MaximumAttempts = maximumAttempts,
                ProviderExecutionEnabled = providerExecutionEnabled
            }),
            new FixedTimeProvider(Now));

    private static JobPostingParseDraftService CreateDraftService(
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
                Lifetime = TimeSpan.FromHours(12)
            }),
            new FixedTimeProvider(Now));
    }

    private async Task InstallClaimFailureTriggerAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION fail_test_draft_claim()
            RETURNS trigger AS $$
            BEGIN
                IF OLD."Status" = 0 AND NEW."Status" = 1 THEN
                    RAISE EXCEPTION 'forced test claim failure';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER fail_test_draft_claim_trigger
            BEFORE UPDATE ON "JobPostingParseDrafts"
            FOR EACH ROW EXECUTE FUNCTION fail_test_draft_claim();
            """,
            cancellationToken);
    }

    private async Task RemoveClaimFailureTriggerAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS fail_test_draft_claim_trigger
                ON "JobPostingParseDrafts";
            DROP FUNCTION IF EXISTS fail_test_draft_claim();
            """,
            cancellationToken);
    }

    private static async Task LockDraftAsync(
        AppDbContext context,
        IDbContextTransaction transaction,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT 1 FROM \"JobPostingParseDrafts\" " +
            "WHERE \"Id\" = @draftId FOR UPDATE;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "draftId";
        parameter.Value = draftId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static JobParseFailureClassification RetryableFailure() =>
        new(JobParseFailureClassifier.ProviderTimeoutCode, true);

    private static JobParseFailureClassification NonRetryableFailure() =>
        new(JobParseFailureClassifier.SchemaValidationFailedCode, false);

    private static User CreateUser() => new()
    {
        FirstName = "Worker",
        LastName = "Owner",
        Email = $"worker-{Guid.NewGuid():N}@example.com",
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

    private static JobPostingParseDraft CreateDraft(
        JobPosting posting,
        JobPostingParseDraftStatus status,
        string suffix,
        DateTimeOffset requestedAt,
        Guid? id = null)
    {
        var sourceTitle = $"Backend Engineer {suffix}";
        return new JobPostingParseDraft
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
    }

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
        uint DraftVersion)
    {
        public JobPostingParseDraftClaim ToClaim(
            string leaseOwner,
            int attemptCount,
            DateTimeOffset claimedAt,
            DateTimeOffset leaseExpiresAt) =>
            new(
                DraftId,
                PostingId,
                leaseOwner,
                attemptCount,
                DraftVersion,
                claimedAt,
                leaseExpiresAt);
    }

    private sealed record SeededOrder(IReadOnlyList<Guid> ExpectedOrder);
    private sealed record SeededPair(Guid FirstDraftId, Guid SecondDraftId);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId)
        {
            UserId = userId;
        }

        public Guid? UserId { get; }
        public string? Email => "worker-test@example.com";
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
        public IJobDescriptionParser GetRequired(string parserVersion) => Current;
    }

    private sealed class NonInvokableParser : IJobDescriptionParser
    {
        public string ParserVersion =>
            JobPostingParseDraftClaimServiceTests.ParserVersion;

        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Parser must not be invoked.");
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
