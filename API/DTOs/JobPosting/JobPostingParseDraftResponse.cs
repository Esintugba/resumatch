using Api.Entities.Enums;

namespace Api.DTOs.JobPosting;

public sealed record JobPostingParseDraftResponse(
    Guid DraftId,
    Guid JobPostingId,
    JobPostingParseDraftStatus Status,
    string ParserVersion,
    uint DraftVersion,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    bool IsReused);
