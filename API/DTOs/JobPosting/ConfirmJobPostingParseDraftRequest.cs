using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.JobPosting;

public sealed record ConfirmJobPostingParseDraftRequest
{
    [Required]
    public IReadOnlyList<SelectedJobPostingParseDraftSkillRequest> SelectedSkills
        { get; init; } = [];
}

public sealed record SelectedJobPostingParseDraftSkillRequest
{
    [NotEmptyGuid]
    public Guid DraftSkillId { get; init; }

    [Range(0, 100)]
    public int? ImportanceScore { get; init; }
}
