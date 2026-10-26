using System.Globalization;
using System.Net.Http.Headers;

namespace Api.Infrastructure.JobParsing.OpenAI;

public static class OpenAiRetryAfterParser
{
    public static readonly TimeSpan MaximumAcceptedDelay = TimeSpan.FromDays(1);

    public static TimeSpan? Parse(
        HttpResponseHeaders headers,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var rawValues = values.ToArray();
        if (rawValues.Length != 1)
        {
            return null;
        }

        var raw = rawValues[0].Trim();
        if (long.TryParse(
                raw,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            if (seconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(Math.Min(
                seconds,
                (long)MaximumAcceptedDelay.TotalSeconds));
        }

        if (!DateTimeOffset.TryParseExact(
                raw,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                out var retryAt))
        {
            return null;
        }

        var delay = retryAt - timeProvider.GetUtcNow();
        if (delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaximumAcceptedDelay
            ? MaximumAcceptedDelay
            : delay;
    }
}
