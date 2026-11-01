using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.Infrastructure.ExceptionHandling;

/// <summary>
/// Makes the OpenAPI document match the wire contract, because client types are
/// generated from it. Nullability of an enum is expressed by the property that
/// uses it, so the shared enum schema must not list null as a value.
/// </summary>
public sealed class ApiContractSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if ((Nullable.GetUnderlyingType(type) ?? type).IsEnum)
        {
            schema.Type = JsonSchemaType.String;
            schema.Enum = schema.Enum?
                .Where(value => value is not null)
                .ToList();
        }
        else if (type == typeof(ProblemDetails) && schema.Properties is { } properties)
        {
            properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
            properties["traceId"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null
            };
            properties["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object | JsonSchemaType.Null,
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            };
        }

        return Task.CompletedTask;
    }
}
