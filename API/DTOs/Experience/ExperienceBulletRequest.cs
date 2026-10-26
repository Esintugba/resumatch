using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Experience;

public sealed class ExperienceBulletRequest
{
    [Required]
    [StringLength(2000)]
    public string Content { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}
