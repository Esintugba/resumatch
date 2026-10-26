using System.Text.Json;

namespace Api.Infrastructure.JobParsing.OpenAI;

public static class OpenAiStructuredOutputSchema
{
    private const string SchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "skills": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "requirement": {
                    "type": "string",
                    "enum": ["required", "preferred", "mentioned"]
                  },
                  "confidence": {
                    "anyOf": [
                      { "type": "number" },
                      { "type": "null" }
                    ]
                  },
                  "evidence": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "text": { "type": "string" }
                      },
                      "required": ["text"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["name", "requirement", "confidence", "evidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["skills"],
          "additionalProperties": false
        }
        """;

    private static readonly JsonElement Schema = ParseSchema();

    public static JsonElement GetSchema() => Schema.Clone();

    private static JsonElement ParseSchema()
    {
        using var document = JsonDocument.Parse(SchemaJson);
        return document.RootElement.Clone();
    }
}
