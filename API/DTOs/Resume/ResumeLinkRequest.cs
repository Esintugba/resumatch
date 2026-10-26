using System.ComponentModel.DataAnnotations;
using Api.Entities.Enums;

namespace Api.DTOs.Resume;

public sealed class ResumeLinkRequest
{
    [Required]
    [EnumDataType(typeof(ResumeLinkType))]
    public ResumeLinkType? Type { get; init; }

    [StringLength(100)]
    public string? Label { get; init; }

    [Required]
    [Url]
    [StringLength(1000)]
    public string Url { get; init; } = string.Empty;
}
