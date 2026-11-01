using System.Text.RegularExpressions;
using Api.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Infrastructure.ExceptionHandling;

/// <summary>
/// Builds the only client-visible validation text that is not the generic
/// message. It lists the public enum names and never echoes the rejected value.
/// </summary>
public static partial class EnumValidationMessages
{
    private const string Prefix = "Must be one of: ";

    public static string Create(Type enumType) =>
        Prefix + string.Join(", ", Enum.GetNames(enumType)) + ".";

    public static string? Find(ModelErrorCollection errors)
    {
        foreach (var error in errors)
        {
            if (error.ErrorMessage.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return error.ErrorMessage;
            }

            // The text comes from System.Text.Json and is matched only to find
            // the target type. If it ever changes, the generic message is used.
            var match = JsonEnumTarget().Match(error.ErrorMessage);
            var enumType = match.Success
                ? typeof(User).Assembly.GetType(match.Value)
                : null;
            if (enumType is { IsEnum: true })
            {
                return Create(enumType);
            }
        }

        return null;
    }

    [GeneratedRegex(@"Api\.Entities\.Enums\.[A-Za-z]+")]
    private static partial Regex JsonEnumTarget();
}
