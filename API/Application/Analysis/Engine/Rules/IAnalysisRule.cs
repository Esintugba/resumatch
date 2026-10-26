namespace Api.Application.Analysis.Engine.Rules;

public interface IAnalysisRule
{
    string RuleCode { get; }
    RuleCategory Category { get; }
    decimal Weight { get; }
    RuleIssueMetadata FailureIssue { get; }

    decimal? Evaluate(RuleEvaluationContext context);
}
