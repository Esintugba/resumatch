using System.ComponentModel.DataAnnotations;
using Api.Entities.Enums;

namespace Api.DTOs.Skill;

public sealed class UpdateResumeSkillRequest
{
    [EnumDataType(typeof(SkillLevel))]
    public SkillLevel? Level { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    public bool IsHighlighted { get; init; }
}
