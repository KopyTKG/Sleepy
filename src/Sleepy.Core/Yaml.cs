using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Sleepy;

/// <summary>
/// Converts a YAML document into the same <see cref="JsonNode"/> tree the JSON
/// path produces, so the rest of the pipeline never has to care which it was.
///
/// This walks YamlDotNet's representation model rather than deserializing into
/// CLR types, which keeps it reflection-free and Native AOT safe.
/// </summary>
public static class Yaml
{
    public static JsonNode? Parse(string text)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);

        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                {
                    var obj = new JsonObject();
                    foreach (var pair in mapping.Children)
                    {
                        var key = pair.Key is YamlScalarNode scalar
                            ? scalar.Value ?? string.Empty
                            : pair.Key.ToString();
                        obj[key] = Convert(pair.Value);
                    }

                    return obj;
                }

            case YamlSequenceNode sequence:
                {
                    var array = new JsonArray();
                    foreach (var child in sequence.Children)
                    {
                        array.Add(Convert(child));
                    }

                    return array;
                }

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                return null;
        }
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        // Quoted scalars are always strings; only plain ones carry an implicit type.
        if (scalar.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted
            or YamlDotNet.Core.ScalarStyle.DoubleQuoted
            or YamlDotNet.Core.ScalarStyle.Literal
            or YamlDotNet.Core.ScalarStyle.Folded)
        {
            return JsonValue.Create(value);
        }

        if (value.Length == 0 || value is "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }
}
