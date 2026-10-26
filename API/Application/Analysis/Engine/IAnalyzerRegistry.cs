namespace Api.Application.Analysis.Engine;

public interface IAnalyzerRegistry
{
    IAnalysisEngine Current { get; }
}
