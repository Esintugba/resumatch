using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;
using Api.Entities.Enums;

namespace Api.DTOs.Skill;

public sealed class AddResumeSkillRequest
{
    [NotEmptyGuid]
    public Guid SkillId { get; init; }

    [EnumDataType(typeof(SkillLevel))]
    public SkillLevel? Level { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsHighlighted { get; init; }
}
