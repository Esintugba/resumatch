using Api.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

/// <summary>
/// Guards endpoints that act on the refresh token cookie. Together with
/// SameSite=Strict, a request must carry the custom header, which a cross-site
/// page cannot send without a CORS preflight, and come from an allowed origin.
/// Runs before model binding, so a rejected request is never read.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireCsrfProtectionAttribute : Attribute, IAsyncResourceFilter
{
    public const string HeaderName = "X-ResuMatch-CSRF";
    public const string HeaderValue = "1";

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var services = context.HttpContext.RequestServices;
        var allowedOrigins = services
            .GetRequiredService<IOptions<HttpSecurityOptions>>()
            .Value.AllowedOrigins;
        var allowHttpReferer = !services
            .GetRequiredService<IHostEnvironment>()
            .IsProduction();

        var reason = !HasHeader(request)
            ? "csrf_header"
            : !IsAllowedOrigin(RequestOrigin(request, allowHttpReferer), allowedOrigins)
                ? "csrf_origin"
                : null;
        if (reason is null)
        {
            await next();
            return;
        }

        ApiSecurityMetrics.RequestsRejected.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason));
        var problem = context.HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "Forbidden",
                detail: "The request did not pass cross-site request checks.",
                instance: request.Path);
        problem.Extensions["code"] = ApiErrorCodes.CsrfRejected;
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static bool HasHeader(HttpRequest request) =>
        request.Headers.TryGetValue(HeaderName, out var values) &&
        values.Count == 1 &&
        string.Equals(values[0], HeaderValue, StringComparison.Ordinal);

    // Browsers send Origin on every POST; Referer is only a fallback for
    // clients that omit it. Neither present means the request is rejected.
    // In production a credentialed origin must be HTTPS, and the fallback
    // follows the same rule.
    internal static string? RequestOrigin(HttpRequest request, bool allowHttpReferer)
    {
        var origin = request.Headers.Origin;
        if (origin.Count > 0)
        {
            return origin.Count == 1 ? origin[0] : null;
        }

        var referer = request.Headers.Referer;
        return referer.Count == 1 &&
            Uri.TryCreate(referer[0], UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps ||
                (allowHttpReferer && uri.Scheme == Uri.UriSchemeHttp))
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }

    private static bool IsAllowedOrigin(string? origin, string[] allowedOrigins) =>
        !string.IsNullOrEmpty(origin) &&
        allowedOrigins.Any(allowed =>
            string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));
}
