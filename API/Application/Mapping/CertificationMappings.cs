using Api.DTOs.Certification;
using Api.Entities;

namespace Api.Application.Mapping;

public static class CertificationMappings
{
    public static ResumeCertification ToEntity(this CreateCertificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeCertification
        {
            Name = request.Name,
            IssuingOrganization = request.IssuingOrganization,
            IssueDate = request.IssueDate,
            ExpirationDate = request.ExpirationDate,
            CredentialId = request.CredentialId,
            CredentialUrl = request.CredentialUrl,
            SortOrder = request.SortOrder
        };
    }

    public static void ApplyUpdate(
        this UpdateCertificationRequest request,
        ResumeCertification certification)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(certification);

        certification.Name = request.Name;
        certification.IssuingOrganization = request.IssuingOrganization;
        certification.IssueDate = request.IssueDate;
        certification.ExpirationDate = request.ExpirationDate;
        certification.CredentialId = request.CredentialId;
        certification.CredentialUrl = request.CredentialUrl;
        certification.SortOrder = request.SortOrder;
    }

    public static CertificationResponse ToResponse(this ResumeCertification certification)
    {
        ArgumentNullException.ThrowIfNull(certification);

        return new CertificationResponse
        {
            Id = certification.Id,
            Name = certification.Name,
            IssuingOrganization = certification.IssuingOrganization,
            IssueDate = certification.IssueDate,
            ExpirationDate = certification.ExpirationDate,
            CredentialId = certification.CredentialId,
            CredentialUrl = certification.CredentialUrl,
            SortOrder = certification.SortOrder
        };
    }
}
