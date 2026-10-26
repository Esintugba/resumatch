namespace Api.Application.JobParsing.Engine.Validation;

public sealed class ParserValidationException : Exception
{
    public ParserValidationException(string code, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Parser validation error code is required.",
                nameof(code));
        }

        Code = code;
    }

    public string Code { get; }
}
