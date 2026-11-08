namespace Api.Application.Common.Exceptions;

/// <summary>
/// The If-Match precondition does not hold for a reason other than a version
/// mismatch caught by the database, for example the record no longer exists
/// or was created by another request first. Answers 412 precondition_failed.
/// </summary>
public sealed class PreconditionFailedException : Exception
{
    public PreconditionFailedException(string message)
        : base(message)
    {
    }
}
