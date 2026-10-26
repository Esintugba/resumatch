using Api.Application.JobParsing.Policies;
using Api.Entities.Enums;

namespace API.Tests.JobParsing;

public sealed class JobPostingParseDraftTransitionPolicyTests
{
    public static TheoryData<JobPostingParseDraftStatus, JobPostingParseDraftStatus>
        ValidTransitions => new()
        {
            { JobPostingParseDraftStatus.Pending, JobPostingParseDraftStatus.Processing },
            { JobPostingParseDraftStatus.Pending, JobPostingParseDraftStatus.Cancelled },
            { JobPostingParseDraftStatus.Pending, JobPostingParseDraftStatus.Superseded },
            { JobPostingParseDraftStatus.Pending, JobPostingParseDraftStatus.Expired },
            { JobPostingParseDraftStatus.Processing, JobPostingParseDraftStatus.Ready },
            { JobPostingParseDraftStatus.Processing, JobPostingParseDraftStatus.Pending },
            { JobPostingParseDraftStatus.Processing, JobPostingParseDraftStatus.Failed },
            { JobPostingParseDraftStatus.Processing, JobPostingParseDraftStatus.Superseded },
            { JobPostingParseDraftStatus.Processing, JobPostingParseDraftStatus.Cancelled },
            { JobPostingParseDraftStatus.Ready, JobPostingParseDraftStatus.Confirmed },
            { JobPostingParseDraftStatus.Ready, JobPostingParseDraftStatus.Superseded },
            { JobPostingParseDraftStatus.Ready, JobPostingParseDraftStatus.Expired },
            { JobPostingParseDraftStatus.Ready, JobPostingParseDraftStatus.Cancelled }
        };

    public static TheoryData<JobPostingParseDraftStatus> TerminalStatuses => new()
    {
        JobPostingParseDraftStatus.Failed,
        JobPostingParseDraftStatus.Confirmed,
        JobPostingParseDraftStatus.Superseded,
        JobPostingParseDraftStatus.Expired,
        JobPostingParseDraftStatus.Cancelled
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransition_DefinedTransition_ReturnsTrue(
        JobPostingParseDraftStatus current,
        JobPostingParseDraftStatus target)
    {
        Assert.True(JobPostingParseDraftTransitionPolicy.CanTransition(
            current,
            target));
        JobPostingParseDraftTransitionPolicy.EnsureCanTransition(current, target);
    }

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void IsTerminal_TerminalStatus_CannotTransition(
        JobPostingParseDraftStatus status)
    {
        Assert.True(JobPostingParseDraftTransitionPolicy.IsTerminal(status));

        foreach (var target in Enum.GetValues<JobPostingParseDraftStatus>())
        {
            Assert.False(JobPostingParseDraftTransitionPolicy.CanTransition(
                status,
                target));
        }
    }

    [Fact]
    public void CanTransition_UndefinedTransition_ReturnsFalseAndEnsureThrows()
    {
        Assert.False(JobPostingParseDraftTransitionPolicy.CanTransition(
            JobPostingParseDraftStatus.Pending,
            JobPostingParseDraftStatus.Ready));

        Assert.Throws<InvalidOperationException>(() =>
            JobPostingParseDraftTransitionPolicy.EnsureCanTransition(
                JobPostingParseDraftStatus.Pending,
                JobPostingParseDraftStatus.Ready));
    }

    [Fact]
    public void CanTransition_ProcessingToProcessing_IsRejected()
    {
        Assert.False(JobPostingParseDraftTransitionPolicy.CanTransition(
            JobPostingParseDraftStatus.Processing,
            JobPostingParseDraftStatus.Processing));
    }

    [Fact]
    public void CanTransition_EveryUnlistedEnumPair_IsRejected()
    {
        var valid = ValidTransitions
            .Select(values => (
                (JobPostingParseDraftStatus)values[0],
                (JobPostingParseDraftStatus)values[1]))
            .ToHashSet();

        foreach (var current in Enum.GetValues<JobPostingParseDraftStatus>())
        {
            foreach (var target in Enum.GetValues<JobPostingParseDraftStatus>())
            {
                Assert.Equal(
                    valid.Contains((current, target)),
                    JobPostingParseDraftTransitionPolicy.CanTransition(
                        current,
                        target));
            }
        }
    }
}
