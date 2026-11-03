using Api.Application.Auth;

namespace API.Tests.Auth;

public sealed class AuthSessionUnitTests
{
    [Fact]
    public void RefreshToken_IsRandomUrlSafeAndHashesToTheStoredValue()
    {
        var (first, firstHash) = RefreshTokenCodec.Create();
        var (second, _) = RefreshTokenCodec.Create();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", first);
        Assert.Equal(32, firstHash.Length);
        Assert.True(RefreshTokenCodec.TryHash(first, out var hash));
        Assert.Equal(firstHash, hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("AAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void RefreshToken_RejectsMalformedValues(string? value)
    {
        Assert.False(RefreshTokenCodec.TryHash(value, out var hash));
        Assert.Empty(hash);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36 Edg/131.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Chrome/131.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("curl/8.9.1", "Unknown device")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void DeviceLabel_IsCoarse(string? userAgent, string? expected)
    {
        Assert.Equal(expected, DeviceLabel.FromUserAgent(userAgent));
    }

    [Fact]
    public void DefaultOptions_AreValid()
    {
        Assert.True(new AuthSessionOptionsValidator().Validate(null, new AuthSessionOptions()).Succeeded);
    }

    [Theory]
    [InlineData("RefreshTokenIdleLifetime")]
    [InlineData("RefreshTokenAbsoluteLifetime")]
    [InlineData("MaximumActiveSessionsPerUser")]
    [InlineData("RotationGracePeriod")]
    [InlineData("PurgeRetention")]
    [InlineData("PurgeInterval")]
    [InlineData("PurgeBatchSize")]
    public void InvalidOptions_AreRejected(string setting)
    {
        var options = setting switch
        {
            "RefreshTokenIdleLifetime" => new AuthSessionOptions { RefreshTokenIdleLifetime = TimeSpan.FromMinutes(5) },
            "RefreshTokenAbsoluteLifetime" => new AuthSessionOptions
            {
                RefreshTokenIdleLifetime = TimeSpan.FromDays(20),
                RefreshTokenAbsoluteLifetime = TimeSpan.FromDays(10)
            },
            "MaximumActiveSessionsPerUser" => new AuthSessionOptions { MaximumActiveSessionsPerUser = 0 },
            "RotationGracePeriod" => new AuthSessionOptions { RotationGracePeriod = TimeSpan.FromMinutes(2) },
            "PurgeRetention" => new AuthSessionOptions { PurgeRetention = TimeSpan.Zero },
            "PurgeInterval" => new AuthSessionOptions { PurgeInterval = TimeSpan.FromSeconds(1) },
            _ => new AuthSessionOptions { PurgeBatchSize = 0 }
        };

        var result = new AuthSessionOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.StartsWith(setting, StringComparison.Ordinal));
    }
}
