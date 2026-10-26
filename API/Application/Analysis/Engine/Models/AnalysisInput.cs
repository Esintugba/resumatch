namespace Api.Application.Analysis.Engine.Models;

public sealed record AnalysisInput
{
    public AnalysisInput(
        ResumeSnapshot resume,
        JobPostingSnapshot? jobPosting,
        DateTimeOffset asOf)
    {
        Resume = resume ?? throw new ArgumentNullException(nameof(resume));
        JobPosting = jobPosting;
        AsOf = asOf;
    }

    public ResumeSnapshot Resume { get; }
    public JobPostingSnapshot? JobPosting { get; }
    public DateTimeOffset AsOf { get; }
}
