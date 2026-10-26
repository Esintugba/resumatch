using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.Project;

[DateOnlyOrder(nameof(StartDate), nameof(EndDate))]
public sealed class UpdateProjectRequest
{
    [Required]
    [StringLength(250)]
    public string Name { get; init; } = string.Empty;

    [StringLength(5000)]
    public string? Description { get; init; }

    [Url]
    [StringLength(1000)]
    public string? Url { get; init; }

    [Url]
    [StringLength(1000)]
    public string? RepositoryUrl { get; init; }

    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }

    [NotEmptyGuidCollection]
    [UniqueGuidCollection]
    [MaxLength(DtoCollectionLimits.ProjectSkills)]
    public List<Guid> SkillIds { get; init; } = new();
}
