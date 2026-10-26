using Api.Entities.Enums;

namespace Api.DTOs.Resume;

public sealed class ResumeLinkResponse
{
    public Guid Id { get; init; }
    public ResumeLinkType Type { get; init; }
    public string? Label { get; init; }
    public string Url { get; init; } = string.Empty;
}
