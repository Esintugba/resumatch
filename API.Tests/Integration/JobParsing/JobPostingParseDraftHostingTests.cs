using Api.Application.JobParsing;
using Api.Infrastructure.JobParsing;
using Api.Infrastructure.JobParsing.OpenAI;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.JobParsing;

public sealed class JobPostingParseDraftHostingTests : IntegrationTestBase
{
    public JobPostingParseDraftHostingTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public void Production_registrations_use_scoped_workers_and_safe_provider()
    {
        using var factory = new ResuMatchWebApplicationFactory(Fixture);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();

        var firstClaim = firstScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftClaimService>();
        var sameScopeClaim = firstScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftClaimService>();
        var secondClaim = secondScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftClaimService>();
        var firstExecution = firstScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftExecutionService>();
        var secondExecution = secondScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftExecutionService>();
        var firstDraftService = firstScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftService>();
        var secondDraftService = secondScope.ServiceProvider
            .GetRequiredService<IJobPostingParseDraftService>();

        Assert.Same(firstClaim, sameScopeClaim);
        Assert.NotSame(firstClaim, secondClaim);
        Assert.NotSame(firstExecution, secondExecution);
        Assert.NotSame(firstDraftService, secondDraftService);
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IJobParserRegistry>(),
            secondScope.ServiceProvider.GetRequiredService<IJobParserRegistry>());
        Assert.IsType<UnavailableJobDescriptionExtractionProvider>(
            firstScope.ServiceProvider
                .GetRequiredService<IJobDescriptionExtractionProvider>());
        Assert.False(firstScope.ServiceProvider
            .GetRequiredService<IOptions<JobPostingParseDraftOptions>>()
            .Value.WorkerEnabled);
        Assert.Equal(
            JobDescriptionExtractionProviderKind.Unavailable,
            firstScope.ServiceProvider
                .GetRequiredService<
                    IOptions<OpenAiJobDescriptionExtractionOptions>>()
                .Value.Provider);
        Assert.DoesNotContain(
            factory.Services.GetServices<IJobDescriptionExtractionProvider>(),
            provider => provider.GetType().Name.Contains(
                "Fake",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            service => service is JobPostingParseDraftBackgroundService);
    }
}
