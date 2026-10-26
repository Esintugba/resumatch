using System.ComponentModel.DataAnnotations;
using Api.DTOs.Common.Validation;

namespace Api.DTOs.Certification;

[DateOnlyOrder(nameof(IssueDate), nameof(ExpirationDate))]
public sealed class UpdateCertificationRequest
{
    [Required]
    [StringLength(250)]
    public string Name { get; init; } = string.Empty;

    [StringLength(250)]
    public string? IssuingOrganization { get; init; }

    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }

    [StringLength(200)]
    public string? CredentialId { get; init; }

    [Url]
    [StringLength(1000)]
    public string? CredentialUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; init; }
}
