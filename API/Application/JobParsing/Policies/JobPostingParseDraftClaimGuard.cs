using System.ComponentModel.DataAnnotations;
using Api.Application.JobParsing.Models;
using Api.Data;
using Api.Entities;
using Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.JobParsing.Policies;

public static class JobPostingParseDraftClaimGuard
{
    private const int MaximumLeaseOwnerLength = 200;

    public static void ValidateAndConfigureConcurrency(
        AppDbContext dbContext,
        JobPostingParseDraft draft,
        JobPostingParseDraftClaim claim,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft, claim, now);

        dbContext.Entry(draft)
            .Property(item => item.Version)
            .OriginalValue = claim.DraftVersion;
    }

    public static void Validate(
        JobPostingParseDraft draft,
        JobPostingParseDraftClaim claim,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateToken(claim);

        if (draft.Id != claim.DraftId ||
            draft.JobPostingId != claim.JobPostingId ||
            draft.Version != claim.DraftVersion ||
            draft.Status != JobPostingParseDraftStatus.Processing ||
            !string.Equals(
                draft.LeaseOwner,
                claim.LeaseOwner,
                StringComparison.Ordinal) ||
            draft.AttemptCount != claim.AttemptCount ||
            draft.LastAttemptAt != claim.ClaimedAt ||
            draft.LeaseExpiresAt != claim.LeaseExpiresAt ||
            draft.LeaseExpiresAt is null ||
            draft.LeaseExpiresAt <= now)
        {
            throw InvalidClaim();
        }
    }

    public static void ValidateToken(JobPostingParseDraftClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.DraftId == Guid.Empty ||
            claim.JobPostingId == Guid.Empty ||
            claim.AttemptCount < 1 ||
            claim.DraftVersion == 0 ||
            claim.ClaimedAt == default ||
            claim.LeaseExpiresAt <= claim.ClaimedAt ||
            string.IsNullOrWhiteSpace(claim.LeaseOwner) ||
            claim.LeaseOwner.Length > MaximumLeaseOwnerLength)
        {
            throw new ValidationException("The draft claim token is invalid.");
        }
    }

    public static DbUpdateConcurrencyException InvalidClaim() =>
        new("The job posting parse draft claim is no longer valid.");
}
