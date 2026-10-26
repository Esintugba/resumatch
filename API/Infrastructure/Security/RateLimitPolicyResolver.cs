using Api.Application.JobParsing;
using Api.Infrastructure.Authentication;

namespace Api.Infrastructure.Security;

internal static class RateLimitPolicyResolver
{
    public static string Resolve(PathString path)
    {
        if (path.StartsWithSegments(
                "/api/auth/login",
                StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationRateLimitPolicyNames.Login;
        }

        if (path.StartsWithSegments(
                "/api/auth/register",
                StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationRateLimitPolicyNames.Register;
        }

        if (path.StartsWithSegments(
                "/api/job-postings",
                StringComparison.OrdinalIgnoreCase,
                out var remaining) &&
            remaining.Value?.TrimEnd('/').EndsWith(
                "/parse",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return JobParsingRateLimitPolicyNames.RequestParse;
        }

        return "unknown";
    }
}
