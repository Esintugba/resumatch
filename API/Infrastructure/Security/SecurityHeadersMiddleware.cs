namespace Api.Infrastructure.Security;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = (HttpResponse)state;
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.ReferrerPolicy = "no-referrer";
            response.Headers.XFrameOptions = "DENY";
            response.Headers.PermissionsPolicy =
                "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        }, context.Response);

        await _next(context);
    }
}
