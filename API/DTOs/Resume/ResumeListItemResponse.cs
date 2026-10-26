namespace Api.DTOs.Resume;

public sealed class ResumeListItemResponse
{
    public Guid Id { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? ProfessionalTitle { get; init; }
    public string? TargetRole { get; init; }
    public bool IsMasterResume { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
