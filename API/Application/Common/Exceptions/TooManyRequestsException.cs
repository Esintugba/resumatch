namespace Api.Application.Common.Exceptions;

public sealed class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
            ? retryAfter
            : null;
    }

    public TimeSpan? RetryAfter { get; }
}
