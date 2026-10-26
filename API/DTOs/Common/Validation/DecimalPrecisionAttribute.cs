using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class DecimalPrecisionAttribute : ValidationAttribute
{
    private readonly decimal _maximumValue;

    public DecimalPrecisionAttribute(int precision, int scale)
    {
        if (precision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precision));
        }

        if (scale < 0 || scale > precision)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        Precision = precision;
        Scale = scale;
        _maximumValue = CalculateMaximumValue(precision, scale);
    }

    public int Precision { get; }
    public int Scale { get; }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not decimal decimalValue)
        {
            return false;
        }

        if (decimalValue < -_maximumValue || decimalValue > _maximumValue)
        {
            return false;
        }

        return decimal.Round(decimalValue, Scale) == decimalValue;
    }

    public override string FormatErrorMessage(string name)
    {
        var integerDigits = Precision - Scale;
        return $"The {name} field must have at most {integerDigits} integer digits and {Scale} decimal digits.";
    }

    private static decimal CalculateMaximumValue(int precision, int scale)
    {
        var integerLimit = 1m;
        for (var index = 0; index < precision - scale; index++)
        {
            integerLimit *= 10m;
        }

        var smallestUnit = 1m;
        for (var index = 0; index < scale; index++)
        {
            smallestUnit /= 10m;
        }

        return integerLimit - smallestUnit;
    }
}
