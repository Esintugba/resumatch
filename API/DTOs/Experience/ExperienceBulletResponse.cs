namespace Api.DTOs.Experience;

public sealed class ExperienceBulletResponse
{
    public Guid Id { get; init; }
    public string Content { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsAiGenerated { get; init; }
}
