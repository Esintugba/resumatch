using Api.Application.JobParsing.Engine.Preprocessing;

namespace API.Tests.JobParsing;

public sealed class JobDescriptionPreprocessorTests
{
    private readonly JobDescriptionPreprocessor _preprocessor = new();

    [Fact]
    public void Process_SameInput_ProducesSameResult()
    {
        const string description = "C# and ASP.NET Core are required.";

        var first = _preprocessor.Process(description);
        var second = _preprocessor.Process(description);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Process_AppliesUnicodeNfkcNormalization()
    {
        var result = _preprocessor.Process("ＡＳＰ．ＮＥＴ　Ｃｏｒｅ");

        Assert.Equal("asp.net core", result.NormalizedMatchingText);
    }

    [Fact]
    public void Process_CollapsesWhitespaceForMatching()
    {
        var result = _preprocessor.Process("  C#\t\r\n  ASP.NET   Core  ");

        Assert.Equal("c# asp.net core", result.NormalizedMatchingText);
    }

    [Fact]
    public void Process_PreservesOriginalDescriptionExactly()
    {
        const string description = "  C#\t\r\n  ASP.NET   Core  ";

        var result = _preprocessor.Process(description);

        Assert.Equal(description, result.OriginalDescription);
    }

    [Fact]
    public void DescriptionHash_UsesDeterministicLowercaseSha256()
    {
        const string description = "C# is required.";

        var first = DescriptionHashCalculator.Compute(description);
        var second = DescriptionHashCalculator.Compute(description);
        var changed = DescriptionHashCalculator.Compute(description + " ");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Process_UnsupportedControlCharacter_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            _preprocessor.Process("C#\u0001 is required."));
    }

    [Fact]
    public void Process_DescriptionOverMaximumLength_IsRejected()
    {
        var description = new string(
            'a',
            JobDescriptionPreprocessor.MaximumDescriptionLength + 1);

        Assert.Throws<ArgumentException>(() =>
            _preprocessor.Process(description));
    }
}
