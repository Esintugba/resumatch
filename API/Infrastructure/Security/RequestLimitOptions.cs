using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Security;

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    public long MaxRequestBodySizeBytes { get; init; } = 1_048_576;
    public long AuthenticationMaxRequestBodySizeBytes { get; init; } = 16_384;
    public int MaxRequestHeadersTotalSize { get; init; } = 32_768;
    public int MaxRequestLineSize { get; init; } = 8_192;
    public int JsonMaxDepth { get; init; } = 32;
}

public sealed class RequestLimitOptionsValidator :
    IValidateOptions<RequestLimitOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        RequestLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.MaxRequestBodySizeBytes is < 65_536 or > 10_485_760)
        {
            failures.Add(
                "MaxRequestBodySizeBytes must be between 64 KiB and 10 MiB.");
        }

        if (options.AuthenticationMaxRequestBodySizeBytes is < 1_024 ||
            options.AuthenticationMaxRequestBodySizeBytes >
            options.MaxRequestBodySizeBytes)
        {
            failures.Add(
                "AuthenticationMaxRequestBodySizeBytes must be between 1 KiB and the global body limit.");
        }

        if (options.MaxRequestHeadersTotalSize is < 8_192 or > 131_072)
        {
            failures.Add(
                "MaxRequestHeadersTotalSize must be between 8 KiB and 128 KiB.");
        }

        if (options.MaxRequestLineSize is < 1_024 or > 32_768)
        {
            failures.Add("MaxRequestLineSize must be between 1 KiB and 32 KiB.");
        }

        if (options.JsonMaxDepth is < 8 or > 64)
        {
            failures.Add("JsonMaxDepth must be between 8 and 64.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
