using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed record JobPostingParseDraftDetailResponse(
    Guid DraftId,
    Guid JobPostingId,
    JobPostingParseDraftStatus Status,
    string ParserVersion,
    uint DraftVersion,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ExpiresAt,
    int AttemptCount,
    string? FailureCode,
    string? ProviderName,
    string? ProviderModel,
    IReadOnlyList<JobPostingParseDraftSkillResponse> Skills);
