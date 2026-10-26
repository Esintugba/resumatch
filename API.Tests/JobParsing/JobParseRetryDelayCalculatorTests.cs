using Api.Application.JobParsing.Policies;

namespace API.Tests.JobParsing;

public sealed class JobParseRetryDelayCalculatorTests
{
    private static readonly Guid DraftId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Calculate_ValidProviderMinimumDelay_IsHonored()
    {
        var delay = JobParseRetryDelayCalculator.Calculate(
            DraftId,
            1,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromSeconds(45), delay);
    }

    [Fact]
    public void Calculate_ProviderDelayCannotExceedMaximumPolicy()
    {
        var delay = JobParseRetryDelayCalculator.Calculate(
            DraftId,
            1,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.Zero,
            TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromMinutes(2), delay);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    public void Calculate_InvalidProviderHint_IsSafelyIgnored(int seconds)
    {
        var delay = JobParseRetryDelayCalculator.Calculate(
            DraftId,
            1,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(seconds));

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public void Calculate_BackoffRemainsWhenItExceedsProviderMinimum()
    {
        var delay = JobParseRetryDelayCalculator.Calculate(
            DraftId,
            3,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(40), delay);
    }
}
