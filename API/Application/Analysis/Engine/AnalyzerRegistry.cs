namespace Api.Application.Analysis.Engine;

public sealed class AnalyzerRegistry : IAnalyzerRegistry
{
    private const string CurrentAnalyzerVersion = AnalysisEngineV1.Version;

    public AnalyzerRegistry(IEnumerable<IAnalysisEngine> analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);

        var analyzerArray = analyzers.ToArray();
        if (analyzerArray.Any(analyzer => analyzer is null))
        {
            throw new ArgumentException(
                "Analyzer collection cannot contain null values.",
                nameof(analyzers));
        }

        if (analyzerArray.Any(analyzer =>
                string.IsNullOrWhiteSpace(analyzer.AnalyzerVersion)))
        {
            throw new InvalidOperationException(
                "Every registered analyzer must declare a version.");
        }

        var duplicateVersion = analyzerArray
            .GroupBy(
                analyzer => analyzer.AnalyzerVersion,
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException(
                $"Analyzer version '{duplicateVersion}' is registered more than once.");
        }

        Current = analyzerArray.SingleOrDefault(analyzer =>
            string.Equals(
                analyzer.AnalyzerVersion,
                CurrentAnalyzerVersion,
                StringComparison.Ordinal)) ??
            throw new InvalidOperationException(
                $"Current analyzer '{CurrentAnalyzerVersion}' is not registered.");
    }

    public IAnalysisEngine Current { get; }
}
