using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Certification;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Certifications;

public sealed class CertificationService : ICertificationService
{
    private static readonly UrlAttribute UrlValidator = new();

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public CertificationService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<CertificationResponse>> GetCertificationsAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var certifications = await _dbContext.ResumeCertifications
            .AsNoTracking()
            .Where(certification => certification.ResumeId == resumeId)
            .OrderBy(certification => certification.SortOrder)
            .ThenByDescending(certification => certification.IssueDate)
            .ThenBy(certification => certification.Id)
            .ToListAsync(cancellationToken);

        return certifications
            .Select(certification => certification.ToResponse())
            .ToList();
    }

    public async Task<CertificationResponse> CreateAsync(
        Guid resumeId,
        CreateCertificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateCertificationValues(
            request.IssueDate,
            request.ExpirationDate,
            request.CredentialUrl,
            request.SortOrder);

        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeCertifications.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Certifications,
            "certifications",
            cancellationToken);

        var certification = request.ToEntity();
        certification.ResumeId = resumeId;

        _dbContext.ResumeCertifications.Add(certification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return certification.ToResponse();
    }

    public async Task<CertificationResponse> GetByIdAsync(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(certificationId, nameof(certificationId));
        var certification = await _dbContext.ResumeCertifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == certificationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (certification is null)
        {
            throw new NotFoundException("Certification was not found.");
        }

        return certification.ToResponse();
    }

    public async Task<CertificationResponse> UpdateAsync(
        Guid resumeId,
        Guid certificationId,
        UpdateCertificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(certificationId, nameof(certificationId));
        ValidateCertificationValues(
            request.IssueDate,
            request.ExpirationDate,
            request.CredentialUrl,
            request.SortOrder);

        var certification = await _dbContext.ResumeCertifications
            .SingleOrDefaultAsync(
                item =>
                    item.Id == certificationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (certification is null)
        {
            throw new NotFoundException("Certification was not found.");
        }

        request.ApplyUpdate(certification);
        certification.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return certification.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid certificationId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(certificationId, nameof(certificationId));
        var certification = await _dbContext.ResumeCertifications
            .SingleOrDefaultAsync(
                item =>
                    item.Id == certificationId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (certification is null)
        {
            throw new NotFoundException("Certification was not found.");
        }

        _dbContext.ResumeCertifications.Remove(certification);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateCertificationValues(
        DateOnly? issueDate,
        DateOnly? expirationDate,
        string? credentialUrl,
        int sortOrder)
    {
        if (issueDate.HasValue &&
            expirationDate.HasValue &&
            expirationDate.Value < issueDate.Value)
        {
            throw new ValidationException(
                "Expiration date cannot be earlier than issue date.");
        }

        if (!string.IsNullOrWhiteSpace(credentialUrl) &&
            !UrlValidator.IsValid(credentialUrl))
        {
            throw new ValidationException("Credential URL is invalid.");
        }

        if (sortOrder < 0)
        {
            throw new ValidationException("Sort order cannot be negative.");
        }
    }

    private static void ValidateRequiredId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException($"{parameterName} cannot be an empty GUID.");
        }
    }
}
