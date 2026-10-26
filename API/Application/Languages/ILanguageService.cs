using Api.DTOs.Language;

namespace Api.Application.Languages;

public interface ILanguageService
{
    Task<IReadOnlyList<LanguageResponse>> GetLanguagesAsync(
        Guid resumeId,
        CancellationToken cancellationToken);

    Task<LanguageResponse> CreateAsync(
        Guid resumeId,
        CreateLanguageRequest request,
        CancellationToken cancellationToken);

    Task<LanguageResponse> GetByIdAsync(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken);

    Task<LanguageResponse> UpdateAsync(
        Guid resumeId,
        Guid languageId,
        UpdateLanguageRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid resumeId,
        Guid languageId,
        CancellationToken cancellationToken);
}
