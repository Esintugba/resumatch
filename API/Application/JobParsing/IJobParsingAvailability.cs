namespace Api.Application.JobParsing;

public interface IJobParsingAvailability
{
    /// <summary>
    /// True when a requested parse can be completed by this deployment. Pausing
    /// with ProviderExecutionEnabled=false does not disable the feature: queued
    /// drafts wait and run when execution resumes.
    /// </summary>
    bool IsEnabled { get; }
}
