namespace Api.Application.Common.Exceptions;

public sealed class TooManyRequestsException : Exception
{
    public TooManyRequestsException(
        string message,
        TimeSpan? retryAfter = null,
        string code = ApiErrorCodes.RateLimited)
        : base(message)
    {
        RetryAfter = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
            ? retryAfter
            : null;
        Code = code;
    }

    public TimeSpan? RetryAfter { get; }

    public string Code { get; }
}
