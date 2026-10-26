using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.Experience;

public sealed class UpdateExperienceBulletRequest
{
    [NotEmptyGuid]
    public Guid? Id { get; init; }

    [Required]
    [StringLength(2000)]
    public string Content { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}
