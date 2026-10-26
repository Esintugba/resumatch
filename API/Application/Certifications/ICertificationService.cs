using Api.DTOs.Certification;

namespace Api.Application.Certifications;

public interface ICertificationService
{
    Task<IReadOnlyList<CertificationResponse>> GetCertificationsAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<CertificationResponse> CreateAsync(
        Guid resumeId,
        CreateCertificationRequest request,
        CancellationToken cancellationToken);

    Task<CertificationResponse> GetByIdAsync(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken);

    Task<CertificationResponse> UpdateAsync(
        Guid resumeId,
        Guid certificationId,
        UpdateCertificationRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken);
}
