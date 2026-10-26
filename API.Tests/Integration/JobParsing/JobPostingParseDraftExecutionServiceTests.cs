using System.Collections.Immutable;
using System.Text.Json;
using Api.Application.Common.Interfaces;
using Api.Application.JobParsing;
using Api.Application.JobParsing.Budget;
using Api.Application.JobParsing.Protection;
using Api.Application.JobParsing.Engine;
using Api.Application.JobParsing.Engine.Canonicalization;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Application.JobParsing.Engine.Validation;
using Api.Application.JobParsing.Errors;
using Api.Application.JobParsing.Models;
using Api.Application.JobParsing.Policies;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using API.Tests.Integration.Infrastructure;
using API.Tests.JobParsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftExecutionServiceTests :
    IntegrationTestBase
{
    private const string Title = "Backend Engineer";
    private const string Description = "C# and Docker are required.";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public JobPostingParseDraftExecutionServiceTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ExecuteAsync_ValidClaim_PersistsCompleteReadyAggregate()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new ObservingProvider(
            ValidPayload(),
            () => context.Database.CurrentTransaction is not null);
        var parser = CreateParser(provider);
        var registry = new TestParserRegistry(parser, new NoOpParser("job-parser-v2"));
        var service = CreateExecutionService(context, registry);

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.False(provider.ObservedOpenTransaction);
        Assert.Equal(Title, provider.LastRequest!.Title);
        Assert.Equal(Description, provider.LastRequest.Description);
        Assert.Equal(JobParserVersions.V1, provider.LastRequest.ParserVersion);
        Assert.Equal(JobParserVersions.V1, registry.LastRequestedVersion);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .Include(item => item.Skills)
            .ThenInclude(skill => skill.Evidence)
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Ready, draft.Status);
        Assert.Equal(Now, draft.CompletedAt);
        Assert.Null(draft.LeaseOwner);
        Assert.Null(draft.LeaseExpiresAt);
        Assert.Null(draft.NextAttemptAt);
        Assert.Null(draft.FailureCode);
        Assert.Equal("request-1", draft.ProviderRequestId);
        Assert.Equal("test-provider", draft.ProviderName);
        Assert.Equal("test-model", draft.ProviderModel);

        var expectedCatalog = await verification.Skills
            .AsNoTracking()
            .OrderBy(item => item.NormalizedName)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Id)
            .Select(item => new SkillCatalogItem(
                item.Id,
                item.Name,
                item.NormalizedName,
                item.Category))
            .ToListAsync(cancellation.Token);
        Assert.Equal(
            SkillCatalogHashCalculator.Compute(expectedCatalog),
            draft.SkillCatalogHash);

        var skills = draft.Skills.OrderBy(item => item.SortOrder).ToArray();
        Assert.Equal(new[] { 0, 1 }, skills.Select(item => item.SortOrder));
        Assert.Equal(new[] { "c#", "docker" },
            skills.Select(item => item.CanonicalKey));
        Assert.All(skills, skill => Assert.NotNull(skill.ResolvedSkillId));
        Assert.Equal(2, skills.Sum(skill => skill.Evidence.Count));
        Assert.Contains(skills.SelectMany(skill => skill.Evidence),
            evidence => evidence.Text == "C#" && evidence.Start == 0);
        Assert.Contains(skills.SelectMany(skill => skill.Evidence),
            evidence => evidence.Text == "Docker" && evidence.Start == 7);
        Assert.False(await verification.JobPostingSkills.AnyAsync(
            cancellation.Token));
        Assert.False(await verification.ResumeAnalyses.AnyAsync(
            cancellation.Token));
    }

    [Theory]
    [InlineData(InvalidSnapshotHash.Description)]
    [InlineData(InvalidSnapshotHash.Input)]
    public async Task ExecuteAsync_InvalidSnapshotHash_BlocksProviderAndFailsSafely(
        InvalidSnapshotHash invalidHash)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token, invalidHash: invalidHash);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new ObservingProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.Equal(0, provider.CallCount);
        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Failed,
            JobParseFailureClassifier.StaleSourceCode,
            cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderTimeout_ReleasesForRetry()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            SimulateCancellation = true
        };
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            providerTimeout: TimeSpan.FromMilliseconds(50));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(JobParseFailureClassifier.ProviderTimeoutCode,
            draft.FailureCode);
        Assert.True(draft.NextAttemptAt > Now);
        Assert.Null(draft.LeaseOwner);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderUnavailable_ReleasesForRetry()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            ExceptionToThrow = new HttpRequestException("provider unavailable secret")
        };
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Pending,
            JobParseFailureClassifier.ProviderUnavailableCode,
            cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_BudgetEnabled_ReservesCallsProviderAndSettlesUsage()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            Usage = new ProviderTokenUsage(40, 20, 60)
        };
        var budget = CreateBudgetService(context);
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: budget);

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.Equal(1, provider.CallCount);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var reservation = await verification.AiUsageReservations.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageReservationStatus.Settled, reservation.Status);
        Assert.Equal(60, reservation.ActualTotalTokens);
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period =>
            {
                Assert.Equal(0, period.ReservedTokens);
                Assert.Equal(60, period.ConsumedTotalTokens);
            });
    }

    [Fact]
    public async Task ExecuteAsync_BudgetDenied_DoesNotCallProviderOrConsumeAttempt()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var budget = CreateBudgetService(context, requestLimit: 1);
        await budget.ReserveAsync(
            new AiUsageReservationRequest(seeded.UserId, Guid.NewGuid(), 1),
            cancellation.Token);
        context.ChangeTracker.Clear();
        var protectionOptions = Options.Create(CreateOptions(null, 3));
        var clock = new FixedTimeProvider(Now);
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: budget,
            providerConcurrency: new AiProviderConcurrencyService(
                context, clock, protectionOptions),
            circuitBreaker: new AiProviderCircuitBreaker(
                context, clock, protectionOptions));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.Equal(0, provider.CallCount);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Equal(JobPostingParseDraftClaimService.UserBudgetExhaustedCode,
            draft.FailureCode);
        Assert.False(await verification.AiProviderConcurrencyLeases.AnyAsync(
            cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_MissingUsage_PersistsReadyButKeepsReservationForReconciliation()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: CreateBudgetService(context));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(JobPostingParseDraftStatus.Ready,
            (await verification.JobPostingParseDrafts.AsNoTracking()
                .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token)).Status);
        Assert.Equal(AiUsageReservationStatus.ReconciliationRequired,
            (await verification.AiUsageReservations.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderTimeout_KeepsReservationForReconciliation()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            SimulateCancellation = true
        };
        var protectionOptions = Options.Create(CreateOptions(
            TimeSpan.FromMilliseconds(50), 3));
        var clock = new FixedTimeProvider(Now);
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            providerTimeout: TimeSpan.FromMilliseconds(50),
            budgetService: CreateBudgetService(context),
            providerConcurrency: new AiProviderConcurrencyService(
                context, clock, protectionOptions),
            circuitBreaker: new AiProviderCircuitBreaker(
                context, clock, protectionOptions));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(AiUsageReservationStatus.ReconciliationRequired,
            (await verification.AiUsageReservations.AsNoTracking().SingleAsync()).Status);
        Assert.All(await verification.AiUsageBudgetPeriods.AsNoTracking().ToListAsync(),
            period => Assert.Equal(150, period.ReservedTokens));
        Assert.Equal(AiProviderConcurrencyLeaseStatus.Released,
            (await verification.AiProviderConcurrencyLeases.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1,
            (await verification.AiProviderCircuitStates.AsNoTracking().SingleAsync())
            .ConsecutiveFailures);
    }

    [Fact]
    public async Task ExecuteAsync_DistributedCapacityDenied_ReleasesBudgetAndDoesNotCallProvider()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        var protectionOptions = Options.Create(CreateOptions(null, 3));
        var clock = new FixedTimeProvider(Now);
        await using var blockerContext = Fixture.DbContextFactory.CreateDbContext();
        var blocker = new AiProviderConcurrencyService(
            blockerContext, clock, protectionOptions);
        var activeLease = await blocker.TryAcquireAsync(
            Guid.NewGuid(), seeded.Claim.LeaseExpiresAt, cancellation.Token);
        Assert.True(activeLease.Acquired);

        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload());
        var budget = CreateBudgetService(context);
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: budget,
            providerConcurrency: new AiProviderConcurrencyService(
                context, clock, protectionOptions),
            circuitBreaker: new AiProviderCircuitBreaker(
                context, clock, protectionOptions));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.Equal(0, provider.CallCount);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.Equal(AiUsageReservationStatus.Released,
            (await verification.AiUsageReservations.AsNoTracking().SingleAsync()).Status);
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Equal(JobPostingParseDraftClaimService.ProviderCapacityUnavailableCode,
            draft.FailureCode);
    }

    [Fact]
    public async Task ExecuteAsync_OpenCircuit_BlocksProviderBeforeBudgetAndDoesNotConsumeAttempt()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        var optionsValue = CreateOptions(null, 3, circuitThreshold: 1);
        var options = Options.Create(optionsValue);
        var clock = new FixedTimeProvider(Now);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var circuit = new AiProviderCircuitBreaker(context, clock, options);
        var openingPermit = await circuit.TryAcquirePermissionAsync(cancellation.Token);
        await circuit.RecordFailureAsync(
            openingPermit.Token!,
            new JobParseFailureClassification(
                JobParseFailureClassifier.ProviderTimeoutCode, true),
            cancellation.Token);
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: CreateBudgetService(context),
            providerConcurrency: new AiProviderConcurrencyService(
                context, clock, options),
            circuitBreaker: circuit,
            optionsOverride: optionsValue);

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        Assert.Equal(0, provider.CallCount);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await verification.AiUsageReservations.AnyAsync(cancellation.Token));
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(0, draft.AttemptCount);
        Assert.Equal(JobPostingParseDraftClaimService.ProviderCircuitOpenCode,
            draft.FailureCode);
    }

    [Fact]
    public async Task ExecuteAsync_RateLimit_UsesProviderMinimumRetryDelay()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            ExceptionToThrow = new ProviderExtractionException(
                ProviderExtractionErrorKind.ProviderRateLimited,
                TimeSpan.FromSeconds(45))
        };
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobPostingParseDraftStatus.Pending, draft.Status);
        Assert.Equal(
            JobParseFailureClassifier.ProviderRateLimitedCode,
            draft.FailureCode);
        Assert.Equal(Now.AddSeconds(45), draft.NextAttemptAt);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_MarksFailed()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider("not-json");
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Failed,
            JobParseFailureClassifier.InvalidProviderResponseCode,
            cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidEvidence_MarksSchemaFailure()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(
            Payload(new Candidate("C#", "required", "XX", 0)));
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Failed,
            JobParseFailureClassifier.SchemaValidationFailedCode,
            cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_PropagatesWithoutFailureMutation()
    {
        using var setupCancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(setupCancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new BlockingProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));
        using var callerCancellation = new CancellationTokenSource();

        var execution = service.ExecuteAsync(
            seeded.Claim,
            callerCancellation.Token);
        await provider.Started.Task.WaitAsync(setupCancellation.Token);
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Processing,
            null,
            setupCancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownCancellation_PreservesUnknownCostAndReleasesDistributedSlot()
    {
        using var setupCancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(setupCancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new BlockingProvider(ValidPayload());
        var optionsValue = CreateOptions(null, 3);
        var options = Options.Create(optionsValue);
        var clock = new FixedTimeProvider(Now);
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            budgetService: CreateBudgetService(context),
            providerConcurrency: new AiProviderConcurrencyService(
                context, clock, options),
            circuitBreaker: new AiProviderCircuitBreaker(
                context, clock, options),
            optionsOverride: optionsValue);
        using var shutdown = new CancellationTokenSource();

        var execution = service.ExecuteAsync(seeded.Claim, shutdown.Token);
        await provider.Started.Task.WaitAsync(setupCancellation.Token);
        shutdown.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts.AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, setupCancellation.Token);
        var reservation = await verification.AiUsageReservations.AsNoTracking()
            .SingleAsync(setupCancellation.Token);
        var lease = await verification.AiProviderConcurrencyLeases.AsNoTracking()
            .SingleAsync(setupCancellation.Token);

        Assert.Equal(JobPostingParseDraftStatus.Processing, draft.Status);
        Assert.Null(draft.FailureCode);
        Assert.Equal(
            AiUsageReservationStatus.ReconciliationRequired,
            reservation.Status);
        Assert.Equal(AiProviderConcurrencyLeaseStatus.Released, lease.Status);
        Assert.All(
            await verification.AiUsageBudgetPeriods.AsNoTracking()
                .ToListAsync(setupCancellation.Token),
            period => Assert.Equal(150, period.ReservedTokens));
    }

    [Fact]
    public async Task ExecuteAsync_UserCancelsDuringProvider_OldResultIsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var executionContext = Fixture.DbContextFactory.CreateDbContext();
        var provider = new BlockingProvider(ValidPayload());
        var executionService = CreateExecutionService(
            executionContext,
            new TestParserRegistry(CreateParser(provider)));
        var execution = executionService.ExecuteAsync(
            seeded.Claim,
            cancellation.Token);
        await provider.Started.Task.WaitAsync(cancellation.Token);

        await using (var cancelContext = Fixture.DbContextFactory.CreateDbContext())
        {
            await CreateDraftService(cancelContext, seeded.UserId)
                .CancelDraftAsync(
                    seeded.PostingId,
                    seeded.DraftId,
                    seeded.Claim.DraftVersion,
                    cancellation.Token);
        }

        provider.Release();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution);
        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Cancelled,
            null,
            cancellation.Token);
        await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_DraftReclaimedDuringProvider_OldWorkerIsRejected()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var executionContext = Fixture.DbContextFactory.CreateDbContext();
        var provider = new BlockingProvider(ValidPayload());
        var execution = CreateExecutionService(
                executionContext,
                new TestParserRegistry(CreateParser(provider)))
            .ExecuteAsync(seeded.Claim, cancellation.Token);
        await provider.Started.Task.WaitAsync(cancellation.Token);

        await using var reclaimContext = Fixture.DbContextFactory.CreateDbContext();
        var reclaimed = await CreateClaimService(
                reclaimContext,
                Now.Add(LeaseDuration).AddSeconds(1))
            .ClaimNextAsync("worker-b", cancellation.Token);
        Assert.NotNull(reclaimed);

        provider.Release();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution);
        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Processing,
            null,
            cancellation.Token);
        await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredLease_DoesNotCallProviderOrPersistReady()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new ObservingProvider(ValidPayload());
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            now: Now.Add(LeaseDuration));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.ExecuteAsync(seeded.Claim, cancellation.Token));
        Assert.Equal(0, provider.CallCount);
        await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
    }

    [Theory]
    [InlineData(SourceChange.Title)]
    [InlineData(SourceChange.Description)]
    public async Task ExecuteAsync_SourceChangesDuringProvider_MarksSuperseded(
        SourceChange sourceChange)
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new BlockingProvider(ValidPayload());
        var execution = CreateExecutionService(
                context,
                new TestParserRegistry(CreateParser(provider)))
            .ExecuteAsync(seeded.Claim, cancellation.Token);
        await provider.Started.Task.WaitAsync(cancellation.Token);

        await UpdatePostingAsync(
            seeded.PostingId,
            sourceChange,
            cancellation.Token);
        provider.Release();
        await execution;

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Superseded,
            JobParseFailureClassifier.StaleSourceCode,
            cancellation.Token);
        await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ResultPersistenceFailure_RollsBackWholeAggregate()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await InstallEvidenceFailureTriggerAsync(cancellation.Token);

        try
        {
            await using var context = Fixture.DbContextFactory.CreateDbContext();
            var provider = new ObservingProvider(ValidPayload());
            var service = CreateExecutionService(
                context,
                new TestParserRegistry(CreateParser(provider)));

            await service.ExecuteAsync(seeded.Claim, cancellation.Token);

            await AssertDraftStatusAsync(
                seeded.DraftId,
                JobPostingParseDraftStatus.Failed,
                JobParseFailureClassifier.UnexpectedFailureCode,
                cancellation.Token);
            await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
        }
        finally
        {
            await RemoveEvidenceFailureTriggerAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidResolvedSkillId_FailsWithoutPartialAggregate()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var maliciousParser = new DelegateParser(
            JobParserVersions.V1,
            input => InvalidResolutionResult(input));
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(maliciousParser));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Failed,
            JobParseFailureClassifier.SchemaValidationFailedCode,
            cancellation.Token);
        await AssertNoDraftChildrenAsync(seeded.DraftId, cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_RetryLimitReached_MarksRetryExhausted()
    {
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(
            cancellation.Token,
            initialAttemptCount: 1,
            maximumAttempts: 2);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            ExceptionToThrow = new HttpRequestException("temporary")
        };
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)),
            maximumAttempts: 2);

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await AssertDraftStatusAsync(
            seeded.DraftId,
            JobPostingParseDraftStatus.Failed,
            JobParseFailureClassifier.RetryExhaustedCode,
            cancellation.Token);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProviderFailure_DoesNotPersistRawMessage()
    {
        const string secretMessage = "provider-secret-payload-must-not-persist";
        using var cancellation = CreateCancellation();
        var seeded = await SeedAndClaimAsync(cancellation.Token);
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var provider = new FakeJobDescriptionExtractionProvider(ValidPayload())
        {
            ExceptionToThrow = new InvalidOperationException(secretMessage)
        };
        var service = CreateExecutionService(
            context,
            new TestParserRegistry(CreateParser(provider)));

        await service.ExecuteAsync(seeded.Claim, cancellation.Token);

        await using var verification = Fixture.DbContextFactory.CreateDbContext();
        var draft = await verification.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == seeded.DraftId, cancellation.Token);
        Assert.Equal(JobParseFailureClassifier.UnexpectedFailureCode,
            draft.FailureCode);
        Assert.DoesNotContain(secretMessage, draft.FailureCode!);
        Assert.Null(draft.ProviderRequestId);
    }

    private async Task<SeededExecution> SeedAndClaimAsync(
        CancellationToken cancellationToken,
        InvalidSnapshotHash invalidHash = InvalidSnapshotHash.None,
        int initialAttemptCount = 0,
        int maximumAttempts = 3)
    {
        Guid userId;
        Guid postingId;
        Guid draftId;
        await using (var context = Fixture.DbContextFactory.CreateDbContext())
        {
            var user = CreateUser();
            var posting = CreatePosting(user.Id);
            var csharp = new Skill
            {
                Name = "C#",
                NormalizedName = "c#",
                Category = SkillCategory.ProgrammingLanguage
            };
            var docker = new Skill
            {
                Name = "Docker",
                NormalizedName = "docker",
                Category = SkillCategory.DevOps
            };
            context.AddRange(user, posting, csharp, docker);
            await context.SaveChangesAsync(cancellationToken);

            var draft = new JobPostingParseDraft
            {
                JobPostingId = posting.Id,
                UserId = user.Id,
                SourceJobPostingVersion = posting.Version,
                SourceTitle = Title,
                SourceDescription = Description,
                SourceDescriptionHash = invalidHash == InvalidSnapshotHash.Description
                    ? new string('a', 64)
                    : DescriptionHashCalculator.Compute(Description),
                SourceInputHash = invalidHash == InvalidSnapshotHash.Input
                    ? new string('b', 64)
                    : SourceInputHashCalculator.Compute(Title, Description),
                ParserVersion = JobParserVersions.V1,
                Status = JobPostingParseDraftStatus.Pending,
                AttemptCount = initialAttemptCount,
                RequestedAt = Now.AddMinutes(-1),
                ExpiresAt = Now.AddHours(12)
            };
            context.JobPostingParseDrafts.Add(draft);
            await context.SaveChangesAsync(cancellationToken);
            userId = user.Id;
            postingId = posting.Id;
            draftId = draft.Id;
        }

        await using var claimContext = Fixture.DbContextFactory.CreateDbContext();
        var claim = await CreateClaimService(
                claimContext,
                Now,
                maximumAttempts)
            .ClaimNextAsync("worker-a", cancellationToken);
        Assert.NotNull(claim);
        return new SeededExecution(userId, postingId, draftId, claim);
    }

    private static JobPostingParseDraftExecutionService CreateExecutionService(
        AppDbContext context,
        IJobParserRegistry registry,
        TimeSpan? providerTimeout = null,
        DateTimeOffset? now = null,
        int maximumAttempts = 3,
        IAiUsageBudgetService? budgetService = null,
        IAiProviderConcurrencyService? providerConcurrency = null,
        IAiProviderCircuitBreaker? circuitBreaker = null,
        JobPostingParseDraftOptions? optionsOverride = null)
    {
        var options = optionsOverride ?? CreateOptions(providerTimeout, maximumAttempts);
        var clock = new FixedTimeProvider(now ?? Now);
        var claimService = new JobPostingParseDraftClaimService(
            context,
            Options.Create(options),
            clock);
        return new JobPostingParseDraftExecutionService(
            context,
            registry,
            claimService,
            new JobDescriptionParseResultValidator(),
            Options.Create(options),
            clock,
            budgetService: budgetService,
            providerConcurrency: providerConcurrency,
            circuitBreaker: circuitBreaker);
    }

    private static AiUsageBudgetService CreateBudgetService(
        AppDbContext context,
        long requestLimit = 10) =>
        new(
            context,
            Options.Create(new AiUsageBudgetOptions
            {
                Enabled = true,
                User = new AiUsageBudgetLimitOptions
                {
                    DailyRequestLimit = requestLimit,
                    MonthlyRequestLimit = requestLimit,
                    DailyTokenLimit = 10_000,
                    MonthlyTokenLimit = 10_000
                },
                System = new AiUsageBudgetLimitOptions
                {
                    DailyRequestLimit = 100,
                    MonthlyRequestLimit = 100,
                    DailyTokenLimit = 100_000,
                    MonthlyTokenLimit = 100_000
                },
                Reservation = new AiUsageReservationOptions
                {
                    EstimatedInputTokens = 100,
                    MaximumOutputTokens = 50,
                    Lifetime = TimeSpan.FromMinutes(30)
                }
            }),
            new FixedTimeProvider(Now));

    private static JobPostingParseDraftClaimService CreateClaimService(
        AppDbContext context,
        DateTimeOffset now,
        int maximumAttempts = 3) =>
        new(
            context,
            Options.Create(CreateOptions(null, maximumAttempts)),
            new FixedTimeProvider(now));

    private static JobPostingParseDraftOptions CreateOptions(
        TimeSpan? providerTimeout,
        int maximumAttempts,
        int circuitThreshold = 3) => new()
    {
        Lifetime = TimeSpan.FromHours(12),
        LeaseDuration = LeaseDuration,
        MaximumAttempts = maximumAttempts,
        ProviderTimeout = providerTimeout ?? TimeSpan.FromSeconds(10),
        RetryBaseDelay = TimeSpan.FromSeconds(10),
        RetryMaximumDelay = TimeSpan.FromMinutes(2),
        RetryJitterMaximum = TimeSpan.FromSeconds(2),
        CircuitBreakerFailureThreshold = circuitThreshold
    };

    private static JobDescriptionParserV1 CreateParser(
        IJobDescriptionExtractionProvider provider) =>
        new(
            provider,
            new SkillCanonicalizerV1(),
            new JobDescriptionPreprocessor(),
            new ProviderResponseValidator(),
            new ParsedSkillDuplicateResolverV1());

    private static JobPostingParseDraftService CreateDraftService(
        AppDbContext context,
        Guid userId)
    {
        var parser = new NoOpParser(JobParserVersions.V1);
        return new JobPostingParseDraftService(
            context,
            new TestCurrentUserService(userId),
            new TestParserRegistry(parser),
            new JobDescriptionPreprocessor(),
            Options.Create(CreateOptions(null, 3)),
            new FixedTimeProvider(Now));
    }

    private async Task UpdatePostingAsync(
        Guid postingId,
        SourceChange change,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var posting = await context.JobPostings.SingleAsync(
            item => item.Id == postingId,
            cancellationToken);
        if (change == SourceChange.Title)
        {
            posting.Title = "Platform Engineer";
        }
        else
        {
            posting.Description = "Go and Kubernetes are required.";
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task AssertDraftStatusAsync(
        Guid draftId,
        JobPostingParseDraftStatus status,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var draft = await context.JobPostingParseDrafts
            .AsNoTracking()
            .SingleAsync(item => item.Id == draftId, cancellationToken);
        Assert.Equal(status, draft.Status);
        Assert.Equal(failureCode, draft.FailureCode);
    }

    private async Task AssertNoDraftChildrenAsync(
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        Assert.False(await context.JobPostingParseDraftSkills
            .AsNoTracking()
            .AnyAsync(item => item.JobPostingParseDraftId == draftId,
                cancellationToken));
        Assert.False(await context.JobPostingParseDraftSkillEvidences
            .AsNoTracking()
            .AnyAsync(item =>
                item.JobPostingParseDraftSkill.JobPostingParseDraftId == draftId,
                cancellationToken));
    }

    private async Task InstallEvidenceFailureTriggerAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION fail_test_draft_evidence_insert()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'forced evidence persistence failure';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER fail_test_draft_evidence_insert_trigger
            BEFORE INSERT ON "JobPostingParseDraftSkillEvidences"
            FOR EACH ROW EXECUTE FUNCTION fail_test_draft_evidence_insert();
            """,
            cancellationToken);
    }

    private async Task RemoveEvidenceFailureTriggerAsync(
        CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS fail_test_draft_evidence_insert_trigger
                ON "JobPostingParseDraftSkillEvidences";
            DROP FUNCTION IF EXISTS fail_test_draft_evidence_insert();
            """,
            cancellationToken);
    }

    private static JobDescriptionParseResult InvalidResolutionResult(
        JobDescriptionParseInput input)
    {
        var evidence = ImmutableArray.Create(new SkillEvidence("C#", 0, 2));
        var candidate = new ParsedSkillCandidate(
            0,
            "C#",
            "c#",
            Guid.NewGuid(),
            "C#",
            SkillCategory.ProgrammingLanguage,
            JobSkillRequirementType.Required,
            100,
            0.9m,
            evidence,
            SkillResolutionStatus.Exact);
        return new JobDescriptionParseResult(
            JobParserVersions.V1,
            input.DescriptionHash,
            input.SourceJobPostingVersion,
            ImmutableArray.Create(candidate),
            ImmutableArray<string>.Empty,
            new JobDescriptionProviderMetadata(
                "malicious-request",
                "test-provider",
                "test-model"));
    }

    private static string ValidPayload() => Payload(
        new Candidate("C#", "required", "C#", 0),
        new Candidate("Docker", "required", "Docker", 7));

    private static string Payload(params Candidate[] candidates) =>
        JsonSerializer.Serialize(new
        {
            skills = candidates.Select(candidate => new
            {
                name = candidate.Name,
                requirement = candidate.Requirement,
                confidence = 0.9m,
                evidence = new[]
                {
                    new
                    {
                        text = candidate.EvidenceText,
                        start = candidate.Start,
                        length = candidate.EvidenceText.Length
                    }
                }
            })
        });

    private static User CreateUser() => new()
    {
        FirstName = "Execution",
        LastName = "Owner",
        Email = $"execution-{Guid.NewGuid():N}@example.com",
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

    private static CancellationTokenSource CreateCancellation() =>
        new(TimeSpan.FromSeconds(60));

    private sealed record SeededExecution(
        Guid UserId,
        Guid PostingId,
        Guid DraftId,
        JobPostingParseDraftClaim Claim);

    private sealed record Candidate(
        string Name,
        string Requirement,
        string EvidenceText,
        int Start);

    public enum InvalidSnapshotHash
    {
        None,
        Description,
        Input
    }

    public enum SourceChange
    {
        Title,
        Description
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(Guid userId) => UserId = userId;
        public Guid? UserId { get; }
        public string? Email => "execution@example.com";
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => UserId!.Value;
    }

    private sealed class TestParserRegistry : IJobParserRegistry
    {
        private readonly IReadOnlyDictionary<string, IJobDescriptionParser> _parsers;

        public TestParserRegistry(
            IJobDescriptionParser parser,
            IJobDescriptionParser? current = null)
        {
            _parsers = new Dictionary<string, IJobDescriptionParser>(
                StringComparer.Ordinal)
            {
                [parser.ParserVersion] = parser
            };
            Current = current ?? parser;
        }

        public IJobDescriptionParser Current { get; }
        public string? LastRequestedVersion { get; private set; }

        public IJobDescriptionParser GetRequired(string parserVersion)
        {
            LastRequestedVersion = parserVersion;
            return _parsers.TryGetValue(parserVersion, out var parser)
                ? parser
                : throw new InvalidOperationException("Parser was not found.");
        }
    }

    private sealed class NoOpParser(string version) : IJobDescriptionParser
    {
        public string ParserVersion { get; } = version;
        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Parser must not be invoked.");
    }

    private sealed class DelegateParser(
        string version,
        Func<JobDescriptionParseInput, JobDescriptionParseResult> resultFactory)
        : IJobDescriptionParser
    {
        public string ParserVersion { get; } = version;
        public Task<JobDescriptionParseResult> ParseAsync(
            JobDescriptionParseInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(resultFactory(input));
    }

    private sealed class ObservingProvider : IJobDescriptionExtractionProvider
    {
        private readonly string _payload;
        private readonly Func<bool>? _transactionObserver;

        public ObservingProvider(
            string payload,
            Func<bool>? transactionObserver = null)
        {
            _payload = payload;
            _transactionObserver = transactionObserver;
        }

        public int CallCount { get; private set; }
        public bool ObservedOpenTransaction { get; private set; }
        public ProviderExtractionRequest? LastRequest { get; private set; }

        public Task<ProviderExtractionResult> ExtractAsync(
            ProviderExtractionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            ObservedOpenTransaction = _transactionObserver?.Invoke() ?? false;
            return Task.FromResult(new ProviderExtractionResult(
                "request-1",
                "test-provider",
                "test-model",
                _payload));
        }
    }

    private sealed class BlockingProvider(string payload) :
        IJobDescriptionExtractionProvider
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<ProviderExtractionResult> ExtractAsync(
            ProviderExtractionRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new ProviderExtractionResult(
                "blocking-request",
                "test-provider",
                "test-model",
                payload);
        }
    }
}
