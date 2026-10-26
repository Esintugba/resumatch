using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidCollectionAttribute : ValidationAttribute
{
    public NotEmptyGuidCollectionAttribute()
        : base("The {0} field must not contain an empty GUID.")
    {
    }

    public override bool IsValid(object? value)
    {
        return value is null ||
               value is IEnumerable<Guid> identifiers && identifiers.All(identifier => identifier != Guid.Empty);
    }
}
