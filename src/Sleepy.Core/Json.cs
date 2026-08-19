using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>
/// Small read helpers over <see cref="JsonNode"/> that never throw: a missing
/// key, a wrong node kind, or a null node all degrade to "not found".
/// </summary>
internal static class Json
{
    public static JsonObject? AsObject(JsonNode? node) => node as JsonObject;

    public static JsonArray? AsArray(JsonNode? node) => node as JsonArray;

    public static JsonNode? Get(JsonNode? node, string key)
        => node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;

    public static string? AsString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    public static string? GetString(JsonNode? node, string key) => AsString(Get(node, key));
}
