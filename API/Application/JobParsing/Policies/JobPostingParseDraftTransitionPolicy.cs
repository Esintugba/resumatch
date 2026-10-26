using Api.Entities.Enums;

namespace Api.Application.JobParsing.Policies;

public static class JobPostingParseDraftTransitionPolicy
{
    public static bool CanTransition(
        JobPostingParseDraftStatus current,
        JobPostingParseDraftStatus target)
    {
        return current switch
        {
            JobPostingParseDraftStatus.Pending => target is
                JobPostingParseDraftStatus.Processing or
                JobPostingParseDraftStatus.Cancelled or
                JobPostingParseDraftStatus.Superseded or
                JobPostingParseDraftStatus.Expired,

            JobPostingParseDraftStatus.Processing => target is
                JobPostingParseDraftStatus.Ready or
                JobPostingParseDraftStatus.Pending or
                JobPostingParseDraftStatus.Failed or
                JobPostingParseDraftStatus.Superseded or
                JobPostingParseDraftStatus.Cancelled,

            JobPostingParseDraftStatus.Ready => target is
                JobPostingParseDraftStatus.Confirmed or
                JobPostingParseDraftStatus.Superseded or
                JobPostingParseDraftStatus.Expired or
                JobPostingParseDraftStatus.Cancelled,

            _ => false
        };
    }

    public static bool IsTerminal(JobPostingParseDraftStatus status)
    {
        return status is
            JobPostingParseDraftStatus.Failed or
            JobPostingParseDraftStatus.Confirmed or
            JobPostingParseDraftStatus.Superseded or
            JobPostingParseDraftStatus.Expired or
            JobPostingParseDraftStatus.Cancelled;
    }

    public static void EnsureCanTransition(
        JobPostingParseDraftStatus current,
        JobPostingParseDraftStatus target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidOperationException(
                $"Job parse draft cannot transition from {current} to {target}.");
        }
    }
}
