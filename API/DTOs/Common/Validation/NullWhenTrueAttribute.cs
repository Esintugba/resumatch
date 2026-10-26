using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class NullWhenTrueAttribute : ValidationAttribute
{
    private readonly string _conditionPropertyName;
    private readonly string _targetPropertyName;

    public NullWhenTrueAttribute(string conditionPropertyName, string targetPropertyName)
    {
        _conditionPropertyName = conditionPropertyName;
        _targetPropertyName = targetPropertyName;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var conditionProperty = GetProperty(validationContext.ObjectType, _conditionPropertyName);
        var targetProperty = GetProperty(validationContext.ObjectType, _targetPropertyName);

        var condition = conditionProperty.GetValue(value);
        var target = targetProperty.GetValue(value);

        if (condition is true && target is not null)
        {
            return new ValidationResult(
                $"The {_targetPropertyName} field must be null when {_conditionPropertyName} is true.",
                new[] { _conditionPropertyName, _targetPropertyName });
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
