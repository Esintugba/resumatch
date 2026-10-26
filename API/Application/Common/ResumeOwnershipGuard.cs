using Api.Application.Common.Exceptions;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Common;

public static class ResumeOwnershipGuard
{
    public static async Task EnsureOwnedResumeExistsAsync(
        AppDbContext dbContext,
        Guid resumeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var resumeExists = await dbContext.Resumes
            .AsNoTracking()
            .AnyAsync(
                resume => resume.Id == resumeId && resume.UserId == userId,
                cancellationToken);

        if (!resumeExists)
        {
            throw new NotFoundException("Resume was not found.");
        }
    }
}
