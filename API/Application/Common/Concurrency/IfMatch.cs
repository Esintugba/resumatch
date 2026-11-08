using System.ComponentModel.DataAnnotations;
using Api.Application.Common.Exceptions;

namespace Api.Application.Common.Concurrency;

/// <summary>Reads the If-Match header for optimistic concurrency.</summary>
public static class IfMatch
{
    /// <summary>
    /// The version the client based its change on. A missing header is 428
    /// precondition_required; a malformed one is 400 validation_failed.
    /// </summary>
    public static uint RequireVersion(string? ifMatch)
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
