using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>
/// Walks a JSON schema and produces a placeholder example value for it.
/// </summary>
internal static class ExampleGenerator
{
    private const int MaxDepth = 6;

    public const string DateTimePlaceholder = "2020-01-01T00:00:00Z";
    public const string DatePlaceholder = "2020-01-01";
    public const string UuidPlaceholder = "00000000-0000-0000-0000-000000000000";

    public static JsonNode? Generate(JsonNode? schema, JsonNode? root)
        => Generate(schema, root, depth: 0, seen: new List<string>());

    private static JsonNode? Generate(JsonNode? schema, JsonNode? root, int depth, List<string> seen)
    {
        if (depth > MaxDepth || schema is not JsonObject)
        {
            return null;
        }

        if (Json.GetString(schema, "$ref") is { } reference)
        {
            if (seen.Contains(reference) || ResolveRef(root, reference) is not { } resolved)
            {
                return null;
            }

            seen.Add(reference);
            try
            {
                return Generate(resolved, root, depth + 1, seen);
            }
            finally
            {
                seen.RemoveAt(seen.Count - 1);
            }
        }

        if (Json.AsArray(Json.Get(schema, "allOf")) is { } allOf)
        {
            var merged = new JsonObject();
            foreach (var subSchema in allOf)
            {
                if (Generate(subSchema, root, depth + 1, seen) is JsonObject part)
                {
                    foreach (var pair in part.ToList())
                    {
                        merged[pair.Key] = pair.Value?.DeepClone();
                    }
                }
            }

            return merged;
        }

        foreach (var key in new[] { "oneOf", "anyOf" })
        {
            if (Json.AsArray(Json.Get(schema, key)) is { Count: > 0 } alternatives)
            {
                return Generate(alternatives[0], root, depth + 1, seen);
            }
        }

        if (Json.AsArray(Json.Get(schema, "enum")) is { Count: > 0 } enumValues)
        {
            return enumValues[0]?.DeepClone();
        }

        if (Json.Get(schema, "example") is { } example)
        {
            return example.DeepClone();
        }

        switch (Json.GetString(schema, "type"))
        {
            case "string":
                return Json.GetString(schema, "format") switch
                {
                    "date-time" => JsonValue.Create(DateTimePlaceholder),
                    "date" => JsonValue.Create(DatePlaceholder),
                    "uuid" => JsonValue.Create(UuidPlaceholder),
                    _ => JsonValue.Create(string.Empty),
                };
            case "integer":
            case "number":
                return JsonValue.Create(0L);
            case "boolean":
                return JsonValue.Create(false);
            case "array":
                {
                    var array = new JsonArray();
                    if (Json.Get(schema, "items") is { } items)
                    {
                        array.Add(Generate(items, root, depth + 1, seen));
                    }

                    return array;
                }

            default:
                {
                    // "object", or a schema with no declared type.
                    var obj = new JsonObject();
                    if (Json.AsObject(Json.Get(schema, "properties")) is { } properties)
                    {
                        foreach (var property in properties.ToList())
                        {
                            obj[property.Key] = Generate(property.Value, root, depth + 1, seen);
                        }
                    }

                    return obj;
                }
        }
    }

    /// <summary>
    /// Resolves a local JSON pointer such as <c>#/components/schemas/Foo</c>
    /// against the root document. Remote references are not supported.
    /// </summary>
    private static JsonNode? ResolveRef(JsonNode? root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        var current = root;
        foreach (var rawToken in reference[2..].Split('/'))
        {
            var token = rawToken.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = Json.Get(current, token);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}
