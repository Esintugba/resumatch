using API.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Api.Infrastructure;

/// <summary>
/// Hosts the API in the Production environment. ProductionSecurityPolicy
/// rejects the wildcard AllowedHosts from appsettings.json, so an explicit
/// trusted host is configured and clients must address it.
/// </summary>
public static class ProductionApplication
{
    public const string AllowedHost = "api.resumatch.test";

    public static ResuMatchWebApplicationFactory Create(
        PostgreSqlFixture fixture,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        var overrides = new Dictionary<string, string?>(
            configurationOverrides ?? new Dictionary<string, string?>())
        {
            ["AllowedHosts"] = AllowedHost
        };

        return new ResuMatchWebApplicationFactory(
            fixture,
            overrides,
            configureTestServices: configureTestServices,
            environmentName: "Production");
    }

    public static HttpClient CreateClient(
        ResuMatchWebApplicationFactory application,
        string scheme = "https") =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"{scheme}://{AllowedHost}")
        });
}
