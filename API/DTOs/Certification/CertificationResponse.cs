namespace Api.DTOs.Certification;

public sealed class CertificationResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IssuingOrganization { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public string? CredentialId { get; init; }
    public string? CredentialUrl { get; init; }
    public int SortOrder { get; init; }
}
