using Api.Application.JobParsing;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;
using Microsoft.Extensions.Options;

namespace API.Tests.Integration.Api;

public sealed class JobPostingParseRateLimitStartupTests : IntegrationTestBase
{
    public JobPostingParseRateLimitStartupTests(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public void InvalidParseRateLimitConfiguration_IsRejectedAtStartup()
    {
        using var application = new ResuMatchWebApplicationFactory(
            Fixture,
            new Dictionary<string, string?>
            {
                [$"{JobPostingParseDraftOptions.SectionName}:ParseRequestsPerWindow"] = "0"
            });

        var exception = Assert.ThrowsAny<Exception>(() =>
            application.CreateHttpsClient());

        Assert.True(
            ContainsOptionsValidation(exception),
            $"Expected options validation failure, received {exception.GetType().Name}.");
    }

    private static bool ContainsOptionsValidation(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is OptionsValidationException validation &&
                validation.Failures.Any(failure => failure.Contains(
                    "ParseRequestsPerWindow",
                    StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
