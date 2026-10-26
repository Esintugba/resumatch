namespace Api.Application.Common.Exceptions;

public sealed class PreconditionRequiredException : Exception
{
    public PreconditionRequiredException(string message)
        : base(message)
    {
    }
}
