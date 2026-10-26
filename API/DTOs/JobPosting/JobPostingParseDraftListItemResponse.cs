using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed record JobPostingParseDraftListItemResponse(
    Guid DraftId,
    JobPostingParseDraftStatus Status,
    string ParserVersion,
    uint DraftVersion,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    int AttemptCount,
    string? FailureCode);
