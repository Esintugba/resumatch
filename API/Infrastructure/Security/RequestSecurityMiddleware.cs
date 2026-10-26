using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

public sealed class RequestSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestLimitOptions _options;

    public RequestSecurityMiddleware(
        RequestDelegate next,
        IOptions<RequestLimitOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var bodyLimit = context.Request.Path.StartsWithSegments(
            "/api/auth",
            StringComparison.OrdinalIgnoreCase)
            ? _options.AuthenticationMaxRequestBodySizeBytes
            : _options.MaxRequestBodySizeBytes;

        if (context.Request.ContentLength is { } length && length > bodyLimit)
        {
            ApiSecurityMetrics.RequestsRejected.Add(
                1,
                new KeyValuePair<string, object?>("reason", "body_too_large"));
            await WriteProblemAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                "The request body exceeds the permitted size.");
            return;
        }

        if (context.Request.HasFormContentType)
        {
            ApiSecurityMetrics.RequestsRejected.Add(
                1,
                new KeyValuePair<string, object?>("reason", "form_not_supported"));
            await WriteProblemAsync(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                "Form request bodies are not supported by this API.");
            return;
        }

        await _next(context);
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
