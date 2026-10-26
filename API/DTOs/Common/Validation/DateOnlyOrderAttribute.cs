using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class DateOnlyOrderAttribute : ValidationAttribute
{
    private readonly string _startPropertyName;
    private readonly string _endPropertyName;

    public DateOnlyOrderAttribute(string startPropertyName, string endPropertyName)
    {
        _startPropertyName = startPropertyName;
        _endPropertyName = endPropertyName;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var startProperty = GetProperty(validationContext.ObjectType, _startPropertyName);
        var endProperty = GetProperty(validationContext.ObjectType, _endPropertyName);

        var startDate = startProperty.GetValue(value) as DateOnly?;
        var endDate = endProperty.GetValue(value) as DateOnly?;

        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            return new ValidationResult(
                $"The {_endPropertyName} field must be on or after {_startPropertyName}.",
                new[] { _startPropertyName, _endPropertyName });
        }

        return ValidationResult.Success;
    }

    private static PropertyInfo GetProperty(Type objectType, string propertyName)
    {
        return objectType.GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' was not found on type '{objectType.Name}'.");
    }
}
