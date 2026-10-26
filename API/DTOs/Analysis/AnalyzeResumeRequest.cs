using Api.DTOs.Common.Validation;

namespace Api.DTOs.Analysis;

public sealed class AnalyzeResumeRequest
{
    [NotEmptyGuid]
    public Guid ResumeId { get; init; }

    [NotEmptyGuid]
    public Guid? JobPostingId { get; init; }
}
