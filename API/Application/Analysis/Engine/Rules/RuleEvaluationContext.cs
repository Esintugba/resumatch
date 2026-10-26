using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.Rules;

public sealed record RuleEvaluationContext
{
    public RuleEvaluationContext(AnalysisInput input)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public AnalysisInput Input { get; }
    public ResumeSnapshot Resume => Input.Resume;
    public JobPostingSnapshot? JobPosting => Input.JobPosting;
    public DateTimeOffset AsOf => Input.AsOf;
}
