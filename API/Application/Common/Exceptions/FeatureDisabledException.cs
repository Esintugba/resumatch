namespace Api.Application.Common.Exceptions;

public sealed class FeatureDisabledException : Exception
{
    public FeatureDisabledException(string message)
        : base(message)
    {
    }
}
