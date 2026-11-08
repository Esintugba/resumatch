using System.Reflection;
using System.Text.Json.Nodes;
using Api.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

namespace Api.Infrastructure.ExceptionHandling;

/// <summary>
/// Describes error responses in the OpenAPI document as they are sent, so the
/// generated client types are right. Documentation only: no response changes.
/// See docs/api/error-codes.md.
/// </summary>
public static class ErrorResponseSchemas
{
    public const string ProblemDetails = "ProblemDetails";
    public const string ValidationProblemDetails = "ValidationProblemDetails";
    public const string UnsupportedMediaTypeProblemDetails = "UnsupportedMediaTypeProblemDetails";
    public const string ProblemJson = "application/problem+json";

    /// <summary>Every value of <see cref="ApiErrorCodes"/>, in declaration order.</summary>
    public static IReadOnlyList<string> Codes { get; } = typeof(ApiErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    /// <summary>The schema that describes an error response with this status.</summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ValidationProblemDetails,
        // MVC writes this one itself when a JSON endpoint gets another media
        // type or no body, and that body has no code.
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaTypeProblemDetails,
        _ => ProblemDetails
    };

    internal static OpenApiSchema Create(bool codeRequired, bool withErrors)
    {
        var properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = NullableString(),
            ["title"] = NullableString(),
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["detail"] = NullableString(),
            ["instance"] = NullableString(),
            ["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Stable error code; see docs/api/error-codes.md.",
                Enum = Codes.Select(code => (JsonNode)JsonValue.Create(code)!).ToList()
            },
            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
        };
        if (withErrors)
        {
            properties["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Field messages, when available. Keys are field paths.",
                AdditionalProperties = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            };
        }

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = properties,
            Required = codeRequired ? new HashSet<string> { "status", "code" } : new HashSet<string> { "status" }
        };
    }

    private static OpenApiSchema NullableString() =>
        new() { Type = JsonSchemaType.String | JsonSchemaType.Null };
}

/// <summary>Registers the error schemas, replacing the generated ProblemDetails.</summary>
public sealed class ErrorResponseDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        var schemas = document.Components.Schemas;
        schemas[ErrorResponseSchemas.ProblemDetails] =
            ErrorResponseSchemas.Create(codeRequired: true, withErrors: false);
        schemas[ErrorResponseSchemas.ValidationProblemDetails] =
            ErrorResponseSchemas.Create(codeRequired: true, withErrors: true);
        schemas[ErrorResponseSchemas.UnsupportedMediaTypeProblemDetails] =
            ErrorResponseSchemas.Create(codeRequired: false, withErrors: false);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds the errors that every request to an endpoint can meet, on top of the
/// ones its action declares, and gives each error response its schema.
/// </summary>
public sealed class ErrorResponseOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var description = context.Description;
        var metadata = description.ActionDescriptor.EndpointMetadata;
        var parameters = description.ParameterDescriptions;
        var hasBody = parameters.Any(parameter => parameter.Source == BindingSource.Body);
        // Bodies, queries and headers are validated, and the services reject an
        // empty GUID in the route, which the :guid constraint lets through.
        var hasValidatedInput = parameters.Any(parameter =>
            parameter.Source == BindingSource.Body ||
            parameter.Source == BindingSource.Query ||
            parameter.Source == BindingSource.Header ||
            parameter.Source == BindingSource.Path);

        operation.Responses ??= new OpenApiResponses();
        var statuses = operation.Responses.Keys
            .Select(key => int.TryParse(key, out var status) ? status : 0)
            .Where(status => status >= 400)
            .ToHashSet();

        if (hasValidatedInput)
        {
            statuses.Add(StatusCodes.Status400BadRequest);
        }

        // The fallback policy requires a user everywhere else.
        if (!metadata.OfType<IAllowAnonymous>().Any())
        {
            statuses.Add(StatusCodes.Status401Unauthorized);
        }

        if (hasBody)
        {
            statuses.Add(StatusCodes.Status413PayloadTooLarge);
            statuses.Add(StatusCodes.Status415UnsupportedMediaType);
        }

        if (metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            statuses.Add(StatusCodes.Status429TooManyRequests);
        }

        statuses.Add(StatusCodes.Status500InternalServerError);

        foreach (var status in statuses.Order())
        {
            var key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var existing = operation.Responses.TryGetValue(key, out var response) ? response : null;
            operation.Responses[key] = new OpenApiResponse
            {
                Description = string.IsNullOrEmpty(existing?.Description)
                    ? ReasonPhrases.GetReasonPhrase(status)
                    : existing.Description,
                Headers = existing?.Headers,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [ErrorResponseSchemas.ProblemJson] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchemaReference(
                            ErrorResponseSchemas.ForStatus(status),
                            context.Document)
                    }
                }
            };
        }

        return Task.CompletedTask;
    }
}
