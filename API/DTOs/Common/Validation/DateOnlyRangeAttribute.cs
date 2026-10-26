using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class DateOnlyRangeAttribute : ValidationAttribute
{
    private readonly DateOnly _minimumDate;

    public DateOnlyRangeAttribute(int minimumYear)
    {
        if (minimumYear is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumYear));
        }

        _minimumDate = new DateOnly(minimumYear, 1, 1);
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not DateOnly date)
        {
            return false;
        }

        var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        return date >= _minimumDate && date <= currentDate;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"The {name} field must be between {_minimumDate:yyyy-MM-dd} and the current UTC date.";
    }
}
