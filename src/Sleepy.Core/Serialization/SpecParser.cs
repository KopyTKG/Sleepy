using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sleepy.Serialization;

/// <summary>
/// Parses a specification document that may be JSON or YAML.
/// </summary>
public static class SpecParser
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 512,
    };

    public static bool LooksLikeYamlName(string name)
        => name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <paramref name="data"/>, trying JSON first (or YAML first when the
    /// name says so) and falling back to the other syntax.
    /// </summary>
    public static JsonNode? Parse(byte[] data, string sourceName)
        => Parse(DecodeUtf8(data), sourceName);

    public static JsonNode? Parse(string text, string sourceName)
    {
        var yamlFirst = LooksLikeYamlName(sourceName);
        JsonNode? json = null;
        string? jsonError = null;

        if (!yamlFirst && TryParseJson(text, out json, out jsonError))
        {
            return json;
        }

        if (TryParseYaml(text, out var yaml, out var yamlError))
        {
            return yaml;
        }

        if (yamlFirst && TryParseJson(text, out json, out jsonError))
        {
            return json;
        }

        var reason = yamlFirst ? yamlError ?? jsonError : jsonError ?? yamlError;
        throw new SleepyException($"failed to parse {sourceName} as JSON or YAML: {reason}");
    }

    /// <summary>Parses without throwing; returns null when the text is neither JSON nor YAML.</summary>
    public static JsonNode? TryParse(string text)
    {
        if (TryParseJson(text, out var json, out _))
        {
            return json;
        }

        return TryParseYaml(text, out var yaml, out _) ? yaml : null;
    }

    public static string DecodeUtf8(byte[] data)
    {
        // Strip a UTF-8 BOM, which neither parser accepts.
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        return Encoding.UTF8.GetString(data);
    }

    private static bool TryParseJson(string text, out JsonNode? node, out string? error)
    {
        try
        {
            node = JsonNode.Parse(text, nodeOptions: null, JsonOptions);
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            node = null;
            error = exception.Message;
            return false;
        }
    }

    private static bool TryParseYaml(string text, out JsonNode? node, out string? error)
    {
        try
        {
            node = Yaml.Parse(text);
            error = null;

            // A bare scalar parses as YAML but is never a document we can use.
            return node is JsonObject or JsonArray;
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            node = null;
            error = exception.Message;
            return false;
        }
    }
}
