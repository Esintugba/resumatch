using Api.DTOs.Language;
using Api.Entities;

namespace Api.Application.Mapping;

public static class LanguageMappings
{
    public static ResumeLanguage ToEntity(this CreateLanguageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResumeLanguage
        {
            Name = request.Name,
            Proficiency = request.Proficiency!.Value,
            SortOrder = request.SortOrder
        };
    }

    public static void ApplyUpdate(
        this UpdateLanguageRequest request,
        ResumeLanguage language)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(language);

        language.Name = request.Name;
        language.Proficiency = request.Proficiency!.Value;
        language.SortOrder = request.SortOrder;
    }

    public static LanguageResponse ToResponse(this ResumeLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);

        return new LanguageResponse
        {
            Id = language.Id,
            Name = language.Name,
            Proficiency = language.Proficiency,
            SortOrder = language.SortOrder
        };
    }
}
