namespace Api.Application.JobParsing.Models;

public sealed record JobPostingParseDraftClaim(
    Guid DraftId,
    Guid JobPostingId,
    string LeaseOwner,
    int AttemptCount,
    uint DraftVersion,
    DateTimeOffset ClaimedAt,
    DateTimeOffset LeaseExpiresAt,
    string ParserVersion = "",
    bool IsLeaseRecovery = false);
