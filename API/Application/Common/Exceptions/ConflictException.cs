namespace Api.Application.Common.Exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(
        string message,
        string code = ApiErrorCodes.Conflict)
        : base(message)
    {
        Code = code;
    }

    public ConflictException(
        string message,
        Exception innerException,
        string code = ApiErrorCodes.Conflict)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
