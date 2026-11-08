using Api.Application.Common.Concurrency;

namespace Api.Controllers;

internal static class EntityTagPreconditions
{
    public static uint GetRequiredVersion(string? ifMatch) => IfMatch.RequireVersion(ifMatch);
}
