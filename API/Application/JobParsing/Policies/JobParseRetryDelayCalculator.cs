using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Api.Application.JobParsing.Policies;

public static class JobParseRetryDelayCalculator
{
    public static TimeSpan Calculate(
        Guid draftId,
        int attemptCount,
        TimeSpan baseDelay,
        TimeSpan maximumDelay,
        TimeSpan jitterMaximum,
        TimeSpan? minimumRetryDelay = null)
    {
        if (draftId == Guid.Empty || attemptCount < 1 ||
            baseDelay <= TimeSpan.Zero || maximumDelay < baseDelay ||
            jitterMaximum < TimeSpan.Zero)
        {
            throw new ArgumentException("Retry delay inputs are invalid.");
        }

        var exponent = Math.Min(attemptCount - 1, 30);
        var multiplier = 1L << exponent;
        var exponentialTicks = baseDelay.Ticks > long.MaxValue / multiplier
            ? maximumDelay.Ticks
            : baseDelay.Ticks * multiplier;
        var boundedTicks = Math.Min(exponentialTicks, maximumDelay.Ticks);
        var jitterTicks = CalculateJitterTicks(
            draftId,
            attemptCount,
            jitterMaximum.Ticks);

        var calculatedTicks = Math.Min(
            maximumDelay.Ticks,
            boundedTicks + jitterTicks);
        var hintedTicks = minimumRetryDelay.HasValue &&
            minimumRetryDelay.Value > TimeSpan.Zero
            ? Math.Min(minimumRetryDelay.Value.Ticks, maximumDelay.Ticks)
            : 0;

        return TimeSpan.FromTicks(Math.Max(calculatedTicks, hintedTicks));
    }

    private static long CalculateJitterTicks(
        Guid draftId,
        int attemptCount,
        long maximumJitterTicks)
    {
        if (maximumJitterTicks == 0)
        {
            return 0;
        }

        Span<byte> input = stackalloc byte[20];
        draftId.TryWriteBytes(input[..16], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(input[16..], attemptCount);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        var value = BinaryPrimitives.ReadUInt64BigEndian(digest);
        return (long)(value % ((ulong)maximumJitterTicks + 1UL));
    }
}
