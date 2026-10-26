using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.Resume;

[NullWhenTrue(nameof(IsMasterResume), nameof(SourceResumeId))]
public sealed class CreateResumeRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [StringLength(200)]
    public string? ProfessionalTitle { get; init; }

    [StringLength(3000)]
    public string? Summary { get; init; }

    public bool IsMasterResume { get; init; }

    [NotEmptyGuid]
    public Guid? SourceResumeId { get; init; }

    [StringLength(100)]
    public string? TemplateKey { get; init; }

    [StringLength(200)]
    public string? TargetRole { get; init; }

    public ResumeContactRequest? Contact { get; init; }

    [MaxLength(DtoCollectionLimits.ResumeLinks)]
    public List<ResumeLinkRequest> Links { get; init; } = new();
}
