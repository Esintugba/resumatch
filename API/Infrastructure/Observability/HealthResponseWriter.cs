using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Infrastructure.Observability;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new
                {
                    name = item.Key,
                    status = item.Value.Status.ToString(),
                    durationMilliseconds = Math.Round(
                        item.Value.Duration.TotalMilliseconds,
                        2,
                        MidpointRounding.AwayFromZero)
                })
        };
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions),
            context.RequestAborted);
    }
}
