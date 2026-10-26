using Api.Application.JobParsing;
using Api.Application.JobParsing.Budget;
using Api.Infrastructure.Observability;
using Api.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace API.Tests.Security;

/// <summary>
/// Keeps appsettings.Production.example.json in sync with the options
/// validators so the documented example never drifts into an invalid shape.
/// </summary>
public sealed class ProductionExampleConfigurationTests
{
    private static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .AddJsonFile(
            Path.Combine(FindRepositoryRoot(), "API", "appsettings.Production.example.json"),
            optional: false,
            reloadOnChange: false)
        .Build();

    [Fact]
    public void Example_DeclaresAllowedHostsAndHttpSecurity()
    {
        Assert.False(string.IsNullOrWhiteSpace(Configuration["AllowedHosts"]));
        var section = Configuration.GetSection(HttpSecurityOptions.SectionName);
        Assert.True(section.Exists());

        var declared = section.GetChildren()
            .Select(child => child.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = typeof(HttpSecurityOptions).GetProperties()
            .Select(property => property.Name);
        Assert.All(expected, name => Assert.Contains(name, declared));
    }

    [Fact]
    public void HttpSecurity_PassesValidatorAndProductionPolicy()
    {
        var options = Bind<HttpSecurityOptions>(HttpSecurityOptions.SectionName);

        AssertValid(new HttpSecurityOptionsValidator().Validate(null, options));
        Assert.Empty(ProductionSecurityPolicy.Validate(
            isProduction: true,
            Configuration["AllowedHosts"],
            options));
        Assert.NotEmpty(options.AllowedOrigins);
        Assert.NotEmpty(options.TrustedProxies);
        Assert.NotEmpty(options.TrustedNetworks);
    }

    [Fact]
    public void OtherDeclaredOptions_PassTheirValidators()
    {
        AssertValid(new RequestLimitOptionsValidator().Validate(
            null,
            Bind<RequestLimitOptions>(RequestLimitOptions.SectionName)));
        AssertValid(new AuthenticationRateLimitOptionsValidator().Validate(
            null,
            Bind<AuthenticationRateLimitOptions>(
                AuthenticationRateLimitOptions.SectionName)));
        AssertValid(new ObservabilityOptionsValidator().Validate(
            null,
            Bind<ObservabilityOptions>(ObservabilityOptions.SectionName)));
        AssertValid(new JobPostingParseDraftOptionsValidator().Validate(
            null,
            Bind<JobPostingParseDraftOptions>(JobPostingParseDraftOptions.SectionName)));
        AssertValid(new AiUsageBudgetOptionsValidator().Validate(
            null,
            Bind<AiUsageBudgetOptions>(AiUsageBudgetOptions.SectionName)));
    }

    private static T Bind<T>(string sectionName)
        where T : new() =>
        Configuration.GetSection(sectionName).Get<T>() ?? new T();

    private static void AssertValid(ValidateOptionsResult result) =>
        Assert.True(result.Succeeded, result.FailureMessage);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ResuMatch.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (ResuMatch.slnx).");
    }
}
