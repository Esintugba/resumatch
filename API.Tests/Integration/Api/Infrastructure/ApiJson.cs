using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.Tests.Integration.Api.Infrastructure;

/// <summary>Reads API responses with the same enum contract the API writes.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}
