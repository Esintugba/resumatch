using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace API.Tests.Security;

public sealed class CsrfRefererSchemeTests
{
    [Theory]
    [InlineData("http://app.example.com/page", true, "http://app.example.com")]
    [InlineData("http://app.example.com/page", false, null)]
    [InlineData("https://app.example.com/page", false, "https://app.example.com")]
    public void RefererFallback_AcceptsHttpOnlyOutsideProduction(
        string referer,
        bool allowHttpReferer,
        string? expectedOrigin)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Referer = referer;

        Assert.Equal(
            expectedOrigin,
            RequireCsrfProtectionAttribute.RequestOrigin(context.Request, allowHttpReferer));
    }
}
