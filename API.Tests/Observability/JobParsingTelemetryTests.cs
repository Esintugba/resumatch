using System.Diagnostics;
using Api.Application.JobParsing.Diagnostics;

namespace API.Tests.Observability;

public sealed class JobParsingTelemetryTests
{
    [Fact]
    public void ActivitySource_ProducesBoundedSpanWithoutSensitiveIdentifiers()
    {
        // The source is process-wide and test classes run in parallel, so only
        // activities stopped in this test's flow are captured.
        var currentTest = new AsyncLocal<bool> { Value = true };
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == JobParsingTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (currentTest.Value)
                {
                    captured = activity;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = JobParsingTelemetry.StartActivity(
            "job_parser.worker.execute"))
        {
            activity?.SetTag("parser.version", "job-parser-v1");
            activity?.SetTag("draft.status", "processing");
        }

        Assert.NotNull(captured);
        Assert.Equal("job_parser.worker.execute", captured.OperationName);
        Assert.Equal("job-parser-v1", captured.GetTagItem("parser.version"));
        var serialized = string.Join(
            ' ',
            captured.TagObjects.Select(item => $"{item.Key}={item.Value}"));
        Assert.DoesNotContain("UserId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DraftId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
