using Api.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Api.Application.Common;

public static class ResumeSectionLimitGuard
{
    public static async Task EnsureCanAddAsync<T>(
        IQueryable<T> sectionQuery,
        int limit,
        string sectionName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sectionQuery);

        var limitReached = await sectionQuery
            .Take(limit)
            .CountAsync(cancellationToken) >= limit;

        if (limitReached)
        {
            throw new ConflictException(
                $"A resume cannot contain more than {limit} {sectionName}.");
        }
    }
}
