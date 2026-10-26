using System.Diagnostics;

namespace Api.Application.JobParsing.Diagnostics;

public static class JobParsingTelemetry
{
    public const string ActivitySourceName = "ResuMatch.JobParsing";
    public const string InstrumentationVersion = "1.0.0";

    public static readonly ActivitySource ActivitySource =
        new(ActivitySourceName, InstrumentationVersion);

    public static Activity? StartActivity(
        string operationName,
        ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(operationName, kind);
}
