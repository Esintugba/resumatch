using System.ComponentModel.DataAnnotations;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Exceptions;

namespace Api.Controllers;

internal static class EntityTagPreconditions
{
    public static uint GetRequiredVersion(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw new PreconditionRequiredException(
                "The If-Match header is required for this operation.");
        }

        if (!EntityTagCodec.TryParse(ifMatch, out var version))
        {
            throw new ValidationException(
                "The If-Match header must contain a single valid strong ETag.");
        }

        return version;
    }
}
