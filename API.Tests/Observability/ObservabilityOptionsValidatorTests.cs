using Api.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace API.Tests.Observability;

public sealed class ObservabilityOptionsValidatorTests
{
    private readonly ObservabilityOptionsValidator _validator = new();

    [Fact]
    public void SecureTransport_RejectsHttpOtlpEndpoint()
    {
        var result = _validator.Validate(
            null,
            new ObservabilityOptions
            {
                Enabled = true,
                OtlpEnabled = true,
                OtlpEndpoint = "http://collector:4317",
                RequireSecureOtlpTransport = true
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("HTTPS", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("00:00:00.100")]
    [InlineData("00:00:31")]
    public void OtlpTimeout_MustBeBounded(string value)
    {
        var result = _validator.Validate(
            null,
            new ObservabilityOptions
            {
                OtlpExportTimeout = TimeSpan.Parse(value)
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void DisabledTelemetry_WithNoOtlp_IsValid()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("ftp://collector.example")]
    public void EnabledOtlp_RequiresAbsoluteHttpEndpoint(string? endpoint)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                Enabled = true,
                OtlpEnabled = true,
                OtlpEndpoint = endpoint
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("http://collector:4317")]
    [InlineData("https://collector.example/v1/telemetry")]
    public void EnabledOtlp_AcceptsAbsoluteHttpEndpoint(string endpoint)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                Enabled = true,
                OtlpEnabled = true,
                OtlpEndpoint = endpoint
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OtlpCannotStartWhileTelemetryIsDisabled()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                Enabled = false,
                OtlpEnabled = true,
                OtlpEndpoint = "http://collector:4317"
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void EnabledTelemetry_RequiresAtLeastOneSignal()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                Enabled = true,
                TracingEnabled = false,
                MetricsEnabled = false
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(3601)]
    public void SamplerInterval_IsBounded(int seconds)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                OperationalSamplerInterval = TimeSpan.FromSeconds(seconds)
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0.249)]
    [InlineData(31)]
    public void DatabaseCheckTimeout_IsBounded(double seconds)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new ObservabilityOptions
            {
                DatabaseCheckTimeout = TimeSpan.FromSeconds(seconds)
            });

        Assert.True(result.Failed);
    }
}
