using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; init; }
    public bool TracingEnabled { get; init; } = true;
    public bool MetricsEnabled { get; init; } = true;
    public bool OtlpEnabled { get; init; }
    public string? OtlpEndpoint { get; init; }
    public bool RequireSecureOtlpTransport { get; init; }
    public TimeSpan OtlpExportTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool OperationalSamplerEnabled { get; init; } = true;
    public TimeSpan OperationalSamplerInterval { get; init; } =
        TimeSpan.FromSeconds(30);
    public TimeSpan DatabaseCheckTimeout { get; init; } =
        TimeSpan.FromSeconds(3);
}

public sealed class ObservabilityOptionsValidator :
    IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.OtlpEnabled && !options.Enabled)
        {
            failures.Add("OTLP export requires observability to be enabled.");
        }

        if (options.OtlpEnabled &&
            !TryGetValidOtlpEndpoint(options.OtlpEndpoint, out _))
        {
            failures.Add(
                "OtlpEndpoint must be an absolute HTTP or HTTPS URI when OTLP is enabled.");
        }

        if (options.OtlpEnabled &&
            options.RequireSecureOtlpTransport &&
            TryGetValidOtlpEndpoint(options.OtlpEndpoint, out var endpoint) &&
            endpoint!.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                "OtlpEndpoint must use HTTPS when secure OTLP transport is required.");
        }

        if (options.OtlpExportTimeout < TimeSpan.FromMilliseconds(250) ||
            options.OtlpExportTimeout > TimeSpan.FromSeconds(30))
        {
            failures.Add(
                "OtlpExportTimeout must be between 250 milliseconds and 30 seconds.");
        }

        if (options.Enabled && !options.TracingEnabled && !options.MetricsEnabled)
        {
            failures.Add(
                "At least tracing or metrics must be enabled when observability is enabled.");
        }

        if (options.OperationalSamplerInterval < TimeSpan.FromSeconds(10) ||
            options.OperationalSamplerInterval > TimeSpan.FromHours(1))
        {
            failures.Add(
                "OperationalSamplerInterval must be between ten seconds and one hour.");
        }

        if (options.DatabaseCheckTimeout < TimeSpan.FromMilliseconds(250) ||
            options.DatabaseCheckTimeout > TimeSpan.FromSeconds(30))
        {
            failures.Add(
                "DatabaseCheckTimeout must be between 250 milliseconds and 30 seconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    public static bool TryGetValidOtlpEndpoint(
        string? value,
        out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }
}
