using Api.Application.JobParsing.Engine.Preprocessing;

namespace API.Tests.JobParsing;

public sealed class SourceInputHashCalculatorTests
{
    [Fact]
    public void Compute_SameOriginalInput_ProducesSameHash()
    {
        var first = SourceInputHashCalculator.Compute(
            "Backend Engineer",
            "C# and ASP.NET Core are required.");
        var second = SourceInputHashCalculator.Compute(
            "Backend Engineer",
            "C# and ASP.NET Core are required.");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_TitleChanges_ProducesDifferentHash()
    {
        var first = SourceInputHashCalculator.Compute("Engineer", "C# required.");
        var second = SourceInputHashCalculator.Compute("Senior Engineer", "C# required.");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_DescriptionChanges_ProducesDifferentHash()
    {
        var first = SourceInputHashCalculator.Compute("Engineer", "C# required.");
        var second = SourceInputHashCalculator.Compute("Engineer", "Go required.");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_FieldBoundaries_AreUnambiguous()
    {
        var first = SourceInputHashCalculator.Compute("ab", "c");
        var second = SourceInputHashCalculator.Compute("a", "bc");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_UnicodeInput_IsDeterministic()
    {
        const string title = "Kıdemli Yazılım Mühendisi 🚀";
        const string description = "C# ve .NET deneyimi gereklidir.";

        Assert.Equal(
            SourceInputHashCalculator.Compute(title, description),
            SourceInputHashCalculator.Compute(title, description));
    }

    [Fact]
    public void Compute_DoesNotNormalizeOriginalSource()
    {
        var composed = SourceInputHashCalculator.Compute("Café", "Engineer");
        var decomposed = SourceInputHashCalculator.Compute("Cafe\u0301", "Engineer");
        var differentCase = SourceInputHashCalculator.Compute("CAFÉ", "Engineer");

        Assert.NotEqual(composed, decomposed);
        Assert.NotEqual(composed, differentCase);
    }

    [Fact]
    public void Compute_ReturnsLowercaseSha256Hex()
    {
        var result = SourceInputHashCalculator.Compute("Engineer", "C# required.");

        Assert.Equal(64, result.Length);
        Assert.Matches("^[0-9a-f]{64}$", result);
    }
}
