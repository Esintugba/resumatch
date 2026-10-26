using Api.Application.JobParsing;
using Api.Infrastructure.Authentication;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace API.Tests.Security;

public sealed class RateLimitPolicyResolverTests
{
    private const string Unknown = "unknown";

    private static readonly string[] AllowedValues =
    [
        AuthenticationRateLimitPolicyNames.Login,
        AuthenticationRateLimitPolicyNames.Register,
        JobParsingRateLimitPolicyNames.RequestParse,
        Unknown
    ];

    [Theory]
    [InlineData("/api/auth/login", AuthenticationRateLimitPolicyNames.Login)]
    [InlineData("/api/auth/register", AuthenticationRateLimitPolicyNames.Register)]
    [InlineData(
        "/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse",
        JobParsingRateLimitPolicyNames.RequestParse)]
    public void KnownPaths_ResolveToRegisteredPolicyNames(
        string path,
        string expected)
    {
        Assert.Equal(expected, RateLimitPolicyResolver.Resolve(new PathString(path)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/api/auth")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/auth/loginx")]
    [InlineData("/api/auth/registered")]
    [InlineData("/api/resumes")]
    [InlineData("/api/job-postings")]
    [InlineData("/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10")]
    [InlineData("/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse-drafts")]
    [InlineData("/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parsex")]
    [InlineData("/health/live")]
    [InlineData("/other/api/auth/login")]
    public void UnrelatedPaths_FallBackToUnknown(string path)
    {
        Assert.Equal(Unknown, RateLimitPolicyResolver.Resolve(new PathString(path)));
    }

    [Theory]
    [InlineData("/API/Auth/Login", AuthenticationRateLimitPolicyNames.Login)]
    [InlineData("/API/AUTH/REGISTER", AuthenticationRateLimitPolicyNames.Register)]
    [InlineData(
        "/API/Job-Postings/0B6F3C61-2C1F-4D43-9A43-6C2F0F6F3A10/PARSE",
        JobParsingRateLimitPolicyNames.RequestParse)]
    public void PathMatching_IsCaseInsensitive(string path, string expected)
    {
        Assert.Equal(expected, RateLimitPolicyResolver.Resolve(new PathString(path)));
    }

    [Theory]
    [InlineData("/api/auth/login/", AuthenticationRateLimitPolicyNames.Login)]
    [InlineData("/api/auth/register/", AuthenticationRateLimitPolicyNames.Register)]
    [InlineData(
        "/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse/",
        JobParsingRateLimitPolicyNames.RequestParse)]
    [InlineData(
        "/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse//",
        JobParsingRateLimitPolicyNames.RequestParse)]
    public void TrailingSlash_ResolvesToSamePolicy(string path, string expected)
    {
        Assert.Equal(expected, RateLimitPolicyResolver.Resolve(new PathString(path)));
    }

    [Theory]
    [InlineData(
        "https://localhost/api/auth/login?returnUrl=%2Fapi%2Fauth%2Fregister",
        AuthenticationRateLimitPolicyNames.Login)]
    [InlineData(
        "https://localhost/api/auth/register?x=1",
        AuthenticationRateLimitPolicyNames.Register)]
    [InlineData(
        "https://localhost/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse?force=true",
        JobParsingRateLimitPolicyNames.RequestParse)]
    [InlineData(
        "https://localhost/api/resumes?next=/api/auth/login",
        Unknown)]
    public void QueryString_IsNotPartOfRequestPathAndDoesNotAffectResolution(
        string url,
        string expected)
    {
        // HttpRequest.Path never contains the query string; build it the same
        // way the server does so the query cannot influence the label.
        var path = PathString.FromUriComponent(new Uri(url));

        Assert.Equal(expected, RateLimitPolicyResolver.Resolve(path));
    }

    [Fact]
    public void ParsePath_ResolvesToSameLabelForDifferentIdentifiers()
    {
        var labels = Enumerable.Range(0, 25)
            .Select(_ => RateLimitPolicyResolver.Resolve(
                new PathString($"/api/job-postings/{Guid.NewGuid():D}/parse")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([JobParsingRateLimitPolicyNames.RequestParse], labels);
    }

    [Theory]
    [InlineData("/api/job-postings/not-a-guid/parse")]
    [InlineData("/api/job-postings/12345/parse")]
    [InlineData("/api/job-postings/parse")]
    public void ParsePath_WithNonGuidIdentifier_StillResolvesToParseLabel(string path)
    {
        // Current behavior: the resolver does not validate the identifier.
        // Routing uses a {jobPostingId:guid} constraint, so such a request never
        // reaches the parse rate limiter and OnRejected never sees this path.
        Assert.Equal(
            JobParsingRateLimitPolicyNames.RequestParse,
            RateLimitPolicyResolver.Resolve(new PathString(path)));
    }

    [Theory]
    [InlineData("/api/job-postings/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10/parse")]
    [InlineData("/api/job-postings/user@example.com/parse")]
    [InlineData("/api/auth/login/user@example.com")]
    [InlineData("/api/resumes/0b6f3c61-2c1f-4d43-9a43-6c2f0f6f3a10")]
    [InlineData("/random/very/long/path/with/identifiers/123456789")]
    public void Result_IsAlwaysBoundedAndNeverEchoesPathContent(string path)
    {
        var label = RateLimitPolicyResolver.Resolve(new PathString(path));

        Assert.Contains(label, AllowedValues);
        Assert.DoesNotContain("0b6f3c61", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", label, StringComparison.Ordinal);
    }
}
