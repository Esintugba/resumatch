using Api.Application.Analysis.Engine.Models;

namespace Api.Application.Analysis.Engine.JobMatching;

public interface IJobMatchCalculator
{
    JobMatchCalculationResult Calculate(
        ResumeSnapshot resume,
        JobPostingSnapshot jobPosting);
}
