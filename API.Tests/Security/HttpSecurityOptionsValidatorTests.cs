using Api.Infrastructure.Security;

namespace API.Tests.Security;

public sealed class HttpSecurityOptionsValidatorTests
{
    private const string OriginFailure =
        "Each CORS origin must be an absolute HTTP or HTTPS origin without wildcard, credentials, query, or fragment.";

    private readonly HttpSecurityOptionsValidator _validator = new();

    [Fact]
    public void DefaultOptions_AreValid()
    {
        var result = _validator.Validate(null, new HttpSecurityOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://app.example.com")]
    [InlineData("http://localhost:4200")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("https://app.example.com/")]
    [InlineData("HTTPS://APP.EXAMPLE.COM")]
    [InlineData("Http://localhost:4200")]
    [InlineData("hTtPs://app.example.com")]
    [InlineData("https://[::1]:5001")]
    [InlineData("http://127.0.0.1:3000")]
    public void ValidOrigins_AreAccepted(string origin)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowedOrigins = [origin] });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData("ftp://app.example.com")]
    [InlineData("FTP://app.example.com")]
    [InlineData("ws://app.example.com")]
    [InlineData("wss://app.example.com")]
    [InlineData("file:///")]
    [InlineData("chrome-extension://abcdefghijklmnop")]
    [InlineData("javascript:alert(1)")]
    [InlineData("httpx://app.example.com")]
    public void NonHttpSchemes_AreRejected(string origin)
    {
        // These differ from a valid origin only by scheme, so they exercise the
        // HTTP/HTTPS comparison in IsValidOrigin directly.
        AssertOriginRejected(origin);
    }

    [Theory]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com/path/")]
    [InlineData("https://app.example.com//")]
    [InlineData("https://app.example.com/index.html")]
    public void OriginsWithPath_AreRejected(string origin)
    {
        AssertOriginRejected(origin);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void EmptyOrWhitespaceOrigins_AreRejected(string origin)
    {
        AssertOriginRejected(origin);
    }

    [Fact]
    public void NullOrigin_IsRejected()
    {
        AssertOriginRejected(null!);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://app.example.com*")]
    public void WildcardOrigins_AreRejected(string origin)
    {
        AssertOriginRejected(origin);
    }

    [Theory]
    [InlineData("https://user:secret@app.example.com")]
    [InlineData("https://user@app.example.com")]
    [InlineData("https://app.example.com/?x=1")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("https://app.example.com/#fragment")]
    [InlineData("app.example.com")]
    [InlineData("//app.example.com")]
    [InlineData("/relative")]
    public void OriginsWithCredentialsQueryFragmentOrNoScheme_AreRejected(
        string origin)
    {
        AssertOriginRejected(origin);
    }

    [Fact]
    public void OneInvalidOriginAmongValidOnes_ReportsSingleOriginFailure()
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions
            {
                AllowedOrigins =
                [
                    "https://app.example.com",
                    "ftp://files.example.com",
                    "https://*.example.com"
                ]
            });

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Failures!.Count(failure => failure == OriginFailure));
    }

    [Fact]
    public void MoreThanFiftyOrigins_AreRejected()
    {
        var origins = Enumerable.Range(0, 51)
            .Select(index => $"https://app{index}.example.com")
            .ToArray();

        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowedOrigins = origins });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "AllowedOrigins cannot contain more than 50 entries.",
            result.Failures!);
    }

    [Fact]
    public void AllowCredentials_RequiresExplicitOrigin()
    {
        var withoutOrigin = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowCredentials = true });
        var withOrigin = _validator.Validate(
            null,
            new HttpSecurityOptions
            {
                AllowCredentials = true,
                AllowedOrigins = ["https://app.example.com"]
            });

        Assert.False(withoutOrigin.Succeeded);
        Assert.Contains(
            "CORS credentials require at least one explicitly allowed origin.",
            withoutOrigin.Failures!);
        Assert.True(withOrigin.Succeeded, withOrigin.FailureMessage);
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.0.0", true)]
    [InlineData("10.0.0.256", false)]
    [InlineData("proxy.internal", false)]
    [InlineData("10.0.0.0/8", false)]
    public void TrustedProxies_MustBeIpAddresses(string proxy, bool expectedValid)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedProxies = [proxy] });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("fd00::/8", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("not-a-network", false)]
    public void TrustedNetworks_MustUseCidrNotation(string network, bool expectedValid)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedNetworks = [network] });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Fact]
    public void MoreThanFiftyTrustedProxies_AreRejected()
    {
        var proxies = Enumerable.Range(1, 51)
            .Select(index => $"10.0.0.{index}")
            .ToArray();

        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedProxies = proxies });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "Trusted proxy and network lists cannot exceed 50 entries each.",
            result.Failures!);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void ForwardLimit_MustBeBetweenOneAndFive(int forwardLimit, bool expectedValid)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { ForwardLimit = forwardLimit });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void HstsMaxAge_MustBeBetweenOneDayAndOneYear(double days, bool expectedValid)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { HstsMaxAge = TimeSpan.FromDays(days) });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    [Theory]
    [InlineData(true, 365, true)]
    [InlineData(false, 365, false)]
    [InlineData(true, 364, false)]
    public void HstsPreload_RequiresSubDomainsAndOneYearMaxAge(
        bool includeSubDomains,
        int maxAgeDays,
        bool expectedValid)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions
            {
                HstsPreload = true,
                HstsIncludeSubDomains = includeSubDomains,
                HstsMaxAge = TimeSpan.FromDays(maxAgeDays)
            });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    private void AssertOriginRejected(string origin)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowedOrigins = [origin] });

        Assert.False(result.Succeeded);
        Assert.Equal([OriginFailure], result.Failures!);
    }
}
