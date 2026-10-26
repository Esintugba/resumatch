using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Language;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Application.Languages;

public sealed class LanguageService : ILanguageService
{
    private const string NormalizedLanguageIndexName =
        "IX_ResumeLanguages_ResumeId_NormalizedName";
    private const string DuplicateLanguageMessage =
        "The language is already added to this resume.";

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public LanguageService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<LanguageResponse>> GetLanguagesAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);

        var languages = await _dbContext.ResumeLanguages
            .AsNoTracking()
            .Where(language => language.ResumeId == resumeId)
            .OrderBy(language => language.SortOrder)
            .ThenBy(language => language.Name)
            .ThenBy(language => language.Id)
            .ToListAsync(cancellationToken);

        return languages
            .Select(language => language.ToResponse())
            .ToList();
    }

    public async Task<LanguageResponse> CreateAsync(
        Guid resumeId,
        CreateLanguageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateLanguageValues(request.Name, request.Proficiency, request.SortOrder);
        await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
            _dbContext, resumeId, userId, cancellationToken);
        await ResumeSectionLimitGuard.EnsureCanAddAsync(
            _dbContext.ResumeLanguages.Where(item => item.ResumeId == resumeId),
            ResumeSectionLimits.Languages,
            "languages",
            cancellationToken);

        await EnsureLanguageNameAvailableAsync(
            resumeId,
            request.Name,
            excludedLanguageId: null,
            cancellationToken);

        var language = request.ToEntity();
        language.ResumeId = resumeId;
        language.Name = request.Name.Trim();

        _dbContext.ResumeLanguages.Add(language);
        await SaveChangesAsync(cancellationToken);

        return language.ToResponse();
    }

    public async Task<LanguageResponse> GetByIdAsync(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(languageId, nameof(languageId));
        var language = await _dbContext.ResumeLanguages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == languageId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (language is null)
        {
            throw new NotFoundException("Language was not found.");
        }

        return language.ToResponse();
    }

    public async Task<LanguageResponse> UpdateAsync(
        Guid resumeId,
        Guid languageId,
        UpdateLanguageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(languageId, nameof(languageId));
        ValidateLanguageValues(request.Name, request.Proficiency, request.SortOrder);
        var language = await _dbContext.ResumeLanguages
            .SingleOrDefaultAsync(
                item =>
                    item.Id == languageId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (language is null)
        {
            throw new NotFoundException("Language was not found.");
        }

        await EnsureLanguageNameAvailableAsync(
            resumeId,
            request.Name,
            languageId,
            cancellationToken);

        request.ApplyUpdate(language);
        language.Name = request.Name.Trim();
        language.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveChangesAsync(cancellationToken);
        return language.ToResponse();
    }

    public async Task DeleteAsync(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId, nameof(resumeId));
        ValidateRequiredId(languageId, nameof(languageId));
        var language = await _dbContext.ResumeLanguages
            .SingleOrDefaultAsync(
                item =>
                    item.Id == languageId &&
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (language is null)
        {
            throw new NotFoundException("Language was not found.");
        }

        _dbContext.ResumeLanguages.Remove(language);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLanguageNameAvailableAsync(
        Guid resumeId,
        string languageName,
        Guid? excludedLanguageId,
        CancellationToken cancellationToken)
    {
        IQueryable<Entities.ResumeLanguage> duplicateQuery =
            _dbContext.ResumeLanguages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "ResumeLanguages"
                    WHERE "ResumeId" = {resumeId}
                      AND "NormalizedName" = lower(btrim({languageName}))
                    """)
                .AsNoTracking();

        if (excludedLanguageId.HasValue)
        {
            duplicateQuery = duplicateQuery.Where(
                language => language.Id != excludedLanguageId.Value);
        }

        var duplicateExists = await duplicateQuery.AnyAsync(cancellationToken);

        if (duplicateExists)
        {
            throw new ConflictException(DuplicateLanguageMessage);
        }
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsNormalizedNameConflict(exception))
        {
            throw new ConflictException(DuplicateLanguageMessage, exception);
        }
    }

    private static bool IsNormalizedNameConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NormalizedLanguageIndexName
        };
    }

    private static void ValidateLanguageValues(
        string name,
        LanguageProficiency? proficiency,
        int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Language name is required.");
        }

        if (!proficiency.HasValue || !Enum.IsDefined(proficiency.Value))
        {
            throw new ValidationException("Language proficiency is invalid.");
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
