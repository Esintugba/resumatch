using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace Api.Infrastructure.ExceptionHandling;

/// <summary>
/// Accepts only enum names in the query string, matching the JSON contract.
/// The default binder also accepts numbers.
/// </summary>
public sealed class StrictEnumQueryModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var enumType = Nullable.GetUnderlyingType(context.Metadata.ModelType) ??
            context.Metadata.ModelType;
        return enumType.IsEnum && context.BindingInfo.BindingSource == BindingSource.Query
            ? new StrictEnumQueryModelBinder(enumType)
            : null;
    }

    private sealed class StrictEnumQueryModelBinder(Type enumType) : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (value == ValueProviderResult.None)
            {
                return Task.CompletedTask;
            }

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
            var name = Enum.GetNames(enumType).FirstOrDefault(item =>
                string.Equals(item, value.FirstValue, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    EnumValidationMessages.Create(enumType));
                return Task.CompletedTask;
            }

            bindingContext.Result = ModelBindingResult.Success(Enum.Parse(enumType, name));
            return Task.CompletedTask;
        }
    }
}
