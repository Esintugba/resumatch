namespace Api.Entities;

public class ResumeCertification : BaseEntity
{
    public Guid ResumeId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? IssuingOrganization { get; set; }

    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }

    public string? CredentialId { get; set; }
    public string? CredentialUrl { get; set; }

    public int SortOrder { get; set; }

    public Resume Resume { get; set; } = null!;
}
