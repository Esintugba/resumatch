using Api.Entities.Enums;

namespace Api.DTOs.Language;

public sealed class LanguageResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public LanguageProficiency Proficiency { get; init; }
    public int SortOrder { get; init; }
}
