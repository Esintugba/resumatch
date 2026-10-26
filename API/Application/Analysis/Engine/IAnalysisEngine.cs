using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine;

public interface IAnalysisEngine
{
    string AnalyzerVersion { get; }

    AnalysisResult Analyze(AnalysisInput input);
}
