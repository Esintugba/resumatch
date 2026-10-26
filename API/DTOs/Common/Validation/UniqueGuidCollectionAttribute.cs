using System.ComponentModel.DataAnnotations;

namespace Api.DTOs.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class UniqueGuidCollectionAttribute : ValidationAttribute
{
    public UniqueGuidCollectionAttribute()
        : base("The {0} field must not contain duplicate GUID values.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not IEnumerable<Guid> identifiers)
        {
            return false;
        }

        var uniqueIdentifiers = new HashSet<Guid>();
        return identifiers.All(uniqueIdentifiers.Add);
    }
}
