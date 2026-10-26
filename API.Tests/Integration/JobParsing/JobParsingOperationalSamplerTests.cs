using System.Diagnostics.Metrics;
using Api.Application.JobParsing.Diagnostics;
using Api.Application.JobParsing.Engine.Preprocessing;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Api.Infrastructure.Observability;
using API.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class JobParsingOperationalSamplerTests(PostgreSqlFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SampleOnce_EmitsBoundedQueueAndReconciliationGauges()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await SeedQueuesAsync(cancellation.Token);
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        await using var provider = CreateServices();
        var sampler = new JobParsingOperationalSampler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            Options.Create(new ObservabilityOptions
            {
                Enabled = true,
                MetricsEnabled = true,
                OperationalSamplerEnabled = true
            }),
            NullLogger<JobParsingOperationalSampler>.Instance);

        await sampler.SampleOnceAsync(cancellation.Token);
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, item =>
            item.Name == "resumatch.job_parsing.queue.backlog" &&
            item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "resumatch.job_parsing.queue.oldest_age" &&
            item.Value == 600);
        Assert.Contains(measurements, item =>
            item.Name ==
                "resumatch.job_parsing.budget.reconciliation.queue.backlog" &&
            item.Value == 1 &&
            item.Tags.Contains("reason=MissingUsage", StringComparer.Ordinal));
        Assert.Contains(measurements, item =>
            item.Name ==
                "resumatch.job_parsing.budget.reconciliation.queue.oldest_age" &&
            item.Value == 300);
        Assert.All(measurements.SelectMany(item => item.Tags), tag =>
        {
            Assert.DoesNotContain("UserId", tag, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DraftId", tag, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ReservationId", tag, StringComparison.OrdinalIgnoreCase);
        });
    }

    private async Task SeedQueuesAsync(CancellationToken cancellationToken)
    {
        await using var context = Fixture.DbContextFactory.CreateDbContext();
        var user = new User
        {
            FirstName = "Metrics",
            LastName = "Owner",
            Email = $"metrics-{Guid.NewGuid():N}@example.com",
            PasswordHash = "integration-test-hash",
            IsActive = true
        };
        var posting = new JobPosting
        {
            UserId = user.Id,
            Title = "Backend Engineer",
            Description = "C# is required.",
            Status = JobPostingStatus.Draft
        };
        var draft = new JobPostingParseDraft
        {
            JobPostingId = posting.Id,
            UserId = user.Id,
            SourceJobPostingVersion = 1,
            SourceTitle = posting.Title,
            SourceDescription = posting.Description,
            SourceDescriptionHash = DescriptionHashCalculator.Compute(
                posting.Description),
            SourceInputHash = SourceInputHashCalculator.Compute(
                posting.Title,
                posting.Description),
            ParserVersion = "job-parser-v1",
            Status = JobPostingParseDraftStatus.Pending,
            RequestedAt = Now.AddMinutes(-10),
            ExpiresAt = Now.AddHours(1)
        };
        var reservation = new AiUsageReservation
        {
            UserId = user.Id,
            DraftId = draft.Id,
            AttemptCount = 1,
            AttemptKey = $"{draft.Id:N}:1",
            ExpiresAt = Now.AddMinutes(30),
            ReservedRequestCount = 1,
            ReservedInputTokens = 100,
            ReservedOutputTokens = 50,
            ReservedTotalTokens = 150,
            Status = AiUsageReservationStatus.ReconciliationRequired,
            ReconciliationRequiredAt = Now.AddMinutes(-5),
            ReconciliationReason = AiUsageReconciliationReason.MissingUsage,
            CreatedAt = Now.AddMinutes(-20)
        };
        context.AddRange(user, posting, draft, reservation);
        await context.SaveChangesAsync(cancellationToken);
    }

    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(Fixture.ConnectionString));
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
    }

    private static MeterListener CreateListener(
        ICollection<MetricMeasurement> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JobPostingParseDraftMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(
                instrument.Name,
                value,
                tags.ToArray().Select(item => $"{item.Key}={item.Value}").ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new(
                instrument.Name,
                value,
                tags.ToArray().Select(item => $"{item.Key}={item.Value}").ToArray())));
        listener.Start();
        return listener;
    }

    private sealed record MetricMeasurement(
        string Name,
        double Value,
        string[] Tags);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
