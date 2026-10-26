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
    [InlineData("https://app.example.com/", "https://app.example.com")]
    [InlineData("http://localhost:4200/", "http://localhost:4200")]
    [InlineData("https://app.example.com:8443/", "https://app.example.com:8443")]
    [InlineData("HTTPS://APP.EXAMPLE.COM/", "https://app.example.com")]
    [InlineData("https://[::1]:5001/", "https://[::1]:5001")]
    [InlineData("https://app.example.com/path", "https://app.example.com")]
    [InlineData("https://app.example.com/path/", "https://app.example.com")]
    [InlineData("https://app.example.com//", "https://app.example.com")]
    [InlineData("https://app.example.com/index.html", "https://app.example.com")]
    [InlineData("https://app.example.com/.", "https://app.example.com")]
    [InlineData("https://app.example.com/./", "https://app.example.com")]
    public void OriginsWithPathOrTrailingSlash_AreRejectedWithCorrectedOrigin(
        string origin,
        string expectedOrigin)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowedOrigins = [origin] });

        Assert.False(result.Succeeded);
        Assert.Equal(
            [
                $"Origin must not contain a path or trailing slash: '{origin}'. " +
                $"Use '{expectedOrigin}'."
            ],
            result.Failures!);
    }

    [Fact]
    public void TrailingSlashMessage_ShowsOriginThatMatchesBrowserOriginHeader()
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { AllowedOrigins = ["https://app.example.com/"] });

        Assert.Equal(
            "Origin must not contain a path or trailing slash: 'https://app.example.com/'. " +
            "Use 'https://app.example.com'.",
            Assert.Single(result.Failures!));
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
    [InlineData("http:app.example.com")]
    [InlineData("https:/app.example.com")]
    [InlineData("https://app.example.com\\")]
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
    [InlineData("10.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fd00::10")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("fe80::1%1")]
    public void TrustedProxies_AcceptFullIpv4AndValidIpv6(string proxy)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedProxies = [proxy] });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData("10.0.0")]
    [InlineData("10.0")]
    [InlineData("10")]
    [InlineData("167772161")]
    [InlineData("10.0.0.256")]
    [InlineData("10.0.0.1.5")]
    [InlineData("10..0.1")]
    [InlineData("10.0.0.")]
    [InlineData(".10.0.0.1")]
    [InlineData("010.0.0.1")]
    [InlineData("10.0.0.01")]
    [InlineData("0x0A.0.0.1")]
    [InlineData("0xA000001")]
    [InlineData(" 10.0.0.1")]
    [InlineData("10.0.0.1 ")]
    [InlineData("10.0.0.0/8")]
    [InlineData("::1/128")]
    [InlineData("2001:db8::g")]
    [InlineData("2001:db8:::1")]
    [InlineData("proxy.internal")]
    [InlineData("localhost")]
    [InlineData("")]
    public void TrustedProxies_RejectIncompleteOrNonCanonicalAddresses(string proxy)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedProxies = [proxy] });

        Assert.False(result.Succeeded);
        Assert.Equal(
            ["Every TrustedProxies entry must be an IP address."],
            result.Failures!);
    }

    [Fact]
    public void TrustedProxies_OneInvalidEntryRejectsTheList()
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedProxies = ["10.0.0.1", "10.0.0"] });

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.168.0.0/16")]
    [InlineData("10.1.2.3/32")]
    [InlineData("0.0.0.0/0")]
    [InlineData("fd00::/8")]
    [InlineData("2001:db8::/32")]
    [InlineData("::1/128")]
    public void TrustedNetworks_AcceptValidCidr(string network)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedNetworks = [network] });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.0.0.0/33")]
    [InlineData("fd00::/129")]
    [InlineData("10.0/8")]
    [InlineData("10/8")]
    [InlineData("010.0.0.0/8")]
    [InlineData("10.0.0.0/08")]
    [InlineData("10.0.0.0/")]
    [InlineData("/8")]
    [InlineData("10.0.0.0/8/8")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/+8")]
    [InlineData("10.0.0.0/ 8")]
    [InlineData("not-a-network")]
    [InlineData("")]
    public void TrustedNetworks_RejectInvalidOrIncompleteCidr(string network)
    {
        var result = _validator.Validate(
            null,
            new HttpSecurityOptions { TrustedNetworks = [network] });

        Assert.False(result.Succeeded);
        Assert.Equal(
            ["Every TrustedNetworks entry must use valid CIDR notation."],
            result.Failures!);
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
