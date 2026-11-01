using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;
using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed class AddJobPostingSkillRequest
{
    [NotEmptyGuid]
    public Guid SkillId { get; init; }

    public JobSkillRequirementType RequirementType { get; init; } =
        JobSkillRequirementType.Required;

    [Range(0, 100)]
    public int ImportanceScore { get; init; } = 50;
}
