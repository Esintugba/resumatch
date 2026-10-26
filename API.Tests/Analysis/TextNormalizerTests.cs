using Api.Application.Analysis.Engine.Keywords;

namespace API.Tests.Analysis;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Developed services with C++", "C++")]
    [InlineData("Developed services with C#", "C#")]
    [InlineData("Built APIs on .NET", ".NET")]
    [InlineData("Built APIs with ASP.NET Core", "ASP.NET Core")]
    public void ContainsExactPhrase_MatchesTechnologyPunctuation(
        string text,
        string keyword)
    {
        Assert.True(TextNormalizer.ContainsExactPhrase(text, keyword));
    }

    [Fact]
    public void ContainsExactPhrase_DoesNotMatchGoInsideGoogle()
    {
        Assert.False(TextNormalizer.ContainsExactPhrase(
            "Worked with Google Cloud",
            "Go"));
    }

    [Fact]
    public void Normalize_UsesUnicodeNfkc()
    {
        var normalized = TextNormalizer.Normalize("ＡＳＰ．ＮＥＴ　Ｃｏｒｅ");

        Assert.Equal("asp.net core", normalized);
    }

    [Fact]
    public void Normalize_CollapsesAllConsecutiveWhitespace()
    {
        var normalized = TextNormalizer.Normalize(
            "  ASP.NET\t\r\n   Core  ");

        Assert.Equal("asp.net core", normalized);
    }
}
