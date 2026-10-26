using System.ComponentModel.DataAnnotations;
using Api.Entities.Enums;

namespace Api.DTOs.Language;

public sealed class UpdateLanguageRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [EnumDataType(typeof(LanguageProficiency))]
    public LanguageProficiency? Proficiency { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}
