using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>
/// Pretty-prints a <see cref="JsonNode"/> tree with two-space indentation and
/// object keys in ordinal order, so the same spec always yields byte-identical
/// output.
/// </summary>
public static class JsonWriter
{
    public static string Write(JsonNode? node)
    {
        var builder = new StringBuilder();
        WriteNode(builder, node, 0);
        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, JsonNode? node, int indent)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                WriteObject(builder, obj, indent);
                break;
            case JsonArray array:
                WriteArray(builder, array, indent);
                break;
            case JsonValue value:
                WriteValue(builder, value);
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonObject obj, int indent)
    {
        if (obj.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        var keys = obj.Select(pair => pair.Key).ToList();
        keys.Sort(StringComparer.Ordinal);

        builder.Append("{\n");
        for (var i = 0; i < keys.Count; i++)
        {
            WriteIndent(builder, indent + 1);
            WriteEscapedString(builder, keys[i]);
            builder.Append(": ");
            WriteNode(builder, obj[keys[i]], indent + 1);
            if (i < keys.Count - 1)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        WriteIndent(builder, indent);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonArray array, int indent)
    {
        if (array.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append("[\n");
        for (var i = 0; i < array.Count; i++)
        {
            WriteIndent(builder, indent + 1);
            WriteNode(builder, array[i], indent + 1);
            if (i < array.Count - 1)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        WriteIndent(builder, indent);
        builder.Append(']');
    }

    private static void WriteValue(StringBuilder builder, JsonValue value)
    {
        // Nodes produced by parsing carry a JsonElement; nodes we build ourselves
        // carry a CLR primitive.
        if (value.TryGetValue<JsonElement>(out var element))
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    WriteEscapedString(builder, element.GetString() ?? string.Empty);
                    return;
                case JsonValueKind.Number:
                    builder.Append(element.GetRawText());
                    return;
                case JsonValueKind.True:
                    builder.Append("true");
                    return;
                case JsonValueKind.False:
                    builder.Append("false");
                    return;
                default:
                    builder.Append("null");
                    return;
            }
        }

        if (value.TryGetValue<string>(out var text))
        {
            WriteEscapedString(builder, text);
            return;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            builder.Append(flag ? "true" : "false");
            return;
        }

        if (value.TryGetValue<int>(out var small))
        {
            builder.Append(small.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            builder.Append(integer.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (value.TryGetValue<decimal>(out var fixedPoint))
        {
            builder.Append(fixedPoint.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (value.TryGetValue<double>(out var number))
        {
            builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        builder.Append("null");
    }

    private static void WriteIndent(StringBuilder builder, int depth)
        => builder.Append(' ', depth * 2);

    private static void WriteEscapedString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
