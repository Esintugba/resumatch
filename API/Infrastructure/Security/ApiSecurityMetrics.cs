using System.Diagnostics.Metrics;

namespace Api.Infrastructure.Security;

public static class ApiSecurityMetrics
{
    public const string MeterName = "ResuMatch.Security";
    public const string InstrumentationVersion = "1.0.0";

    private static readonly Meter Meter = new(MeterName, InstrumentationVersion);

    public static readonly Counter<long> RequestsRejected =
        Meter.CreateCounter<long>("resumatch.security.requests.rejected");

    public static readonly Counter<long> RateLimited =
        Meter.CreateCounter<long>("resumatch.security.rate_limited");
}
