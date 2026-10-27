using Api.Infrastructure.Security;

namespace API.Tests.Security;

public sealed class ProductionSecurityPolicyTests
{
    private const string AllowedHostsFailure =
        "Production AllowedHosts must contain explicit trusted hosts and cannot use wildcard.";
    private const string HstsFailure = "HSTS must be enabled in production.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public void NonProduction_IsNotRestricted(string? allowedHosts)
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: false,
            allowedHosts,
            new HttpSecurityOptions { HstsEnabled = false });

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("api.example.com;www.example.com")]
    [InlineData(" api.example.com ; www.example.com ")]
    public void Production_WithExplicitHostsAndHsts_IsAccepted(string allowedHosts)
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            allowedHosts,
            new HttpSecurityOptions());

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [InlineData(" ; ; ")]
    public void Production_WithoutAllowedHosts_IsRejected(string? allowedHosts)
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            allowedHosts,
            new HttpSecurityOptions());

        Assert.Equal([AllowedHostsFailure], failures);
    }

    [Theory]
    [InlineData("*")]
    [InlineData(" * ")]
    [InlineData("api.example.com;*")]
    [InlineData("*;api.example.com")]
    public void Production_WithWildcardAllowedHost_IsRejected(string allowedHosts)
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            allowedHosts,
            new HttpSecurityOptions());

        Assert.Equal([AllowedHostsFailure], failures);
    }

    [Fact]
    public void Production_WithSubdomainWildcardHost_IsAccepted()
    {
        // Current behavior: only the bare "*" entry is treated as a wildcard.
        // Host filtering supports "*.example.com" as an explicit subdomain rule.
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            "*.example.com",
            new HttpSecurityOptions());

        Assert.Empty(failures);
    }

    [Fact]
    public void Production_WithHstsDisabled_IsRejected()
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            "api.example.com",
            new HttpSecurityOptions { HstsEnabled = false });

        Assert.Equal([HstsFailure], failures);
    }

    [Fact]
    public void Production_WithOpenApiExposed_IsRejected()
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            "api.example.com",
            new HttpSecurityOptions { ExposeOpenApi = true });

        Assert.Equal(
            ["The OpenAPI document and Swagger UI cannot be exposed in production."],
            failures);
    }

    [Fact]
    public void NonProduction_MayExposeOpenApi()
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: false,
            "*",
            new HttpSecurityOptions { ExposeOpenApi = true });

        Assert.Empty(failures);
    }

    [Fact]
    public void Production_ReportsAllFailuresTogether()
    {
        var failures = ProductionSecurityPolicy.Validate(
            isProduction: true,
            "*",
            new HttpSecurityOptions { HstsEnabled = false });

        Assert.Equal([AllowedHostsFailure, HstsFailure], failures);
    }

    [Fact]
    public void NullOptions_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductionSecurityPolicy.Validate(true, "api.example.com", null!));
    }
}
