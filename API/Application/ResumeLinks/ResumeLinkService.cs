using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Resume;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.ResumeLinks;

public sealed class ResumeLinkService : IResumeLinkService
{
    private static readonly UrlAttribute UrlValidator = new();

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ResumeLinkService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<ResumeLinkResponse>> GetLinksAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var links = await _dbContext.ResumeLinks
            .AsNoTracking()
            .Where(link => link.ResumeId == resumeId)
            .OrderBy(link => link.Type)
            .ThenBy(link => link.Label)
            .ThenBy(link => link.Id)
            .ToListAsync(cancellationToken);

        return links
            .Select(link => link.ToResponse())
            .ToList();
    }

    public async Task<ResumeLinkResponse> CreateAsync(
        Guid resumeId,
        ResumeLinkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateLink(request.Type, request.Url);
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeLinks.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Links,
            "links",
            cancellationToken);

        var link = request.ToEntity();
        link.ResumeId = resumeId;

        _dbContext.ResumeLinks.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return link.ToResponse();
    }

    public async Task<ResumeLinkResponse> GetByIdAsync(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(linkId, nameof(linkId));
        var link = await _dbContext.ResumeLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == linkId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (link is null)
        {
            throw new NotFoundException("Resume link was not found.");
        }

        return link.ToResponse();
    }

    public async Task<ResumeLinkResponse> UpdateAsync(
        Guid resumeId,
        Guid linkId,
        ResumeLinkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(linkId, nameof(linkId));
        ValidateLink(request.Type, request.Url);
        var link = await _dbContext.ResumeLinks.SingleOrDefaultAsync(
            item =>
                item.Id == linkId &&
                item.ResumeId == resumeId &&
                item.Resume.UserId == userId,
            cancellationToken);

        if (link is null)
        {
            throw new NotFoundException("Resume link was not found.");
        }

        request.ApplyUpdate(link);
        link.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return link.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid linkId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(linkId, nameof(linkId));
        var link = await _dbContext.ResumeLinks.SingleOrDefaultAsync(
            item =>
                item.Id == linkId &&
                item.ResumeId == resumeId &&
                item.Resume.UserId == userId,
            cancellationToken);

        if (link is null)
        {
            throw new NotFoundException("Resume link was not found.");
        }

        _dbContext.ResumeLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateLink(ResumeLinkType? type, string url)
    {
        if (!type.HasValue || !Enum.IsDefined(type.Value))
        {
            throw new ValidationException("Resume link type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(url) || !UrlValidator.IsValid(url))
        {
            throw new ValidationException("Resume link URL is invalid.");
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
