using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Resume;

public sealed class UpdateResumeRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [StringLength(200)]
    public string? ProfessionalTitle { get; init; }

    [StringLength(3000)]
    public string? Summary { get; init; }

    [StringLength(500)]
    public string? UnlistedSkillsNote { get; init; }

    [StringLength(100)]
    public string? TemplateKey { get; init; }

    [StringLength(200)]
    public string? TargetRole { get; init; }
}
