using System.Text.Json.Nodes;

namespace Sleepy.Serialization;

/// <summary>
/// Reads and writes <see cref="UserSettings"/> as JSON on disk.
///
/// Nothing here throws: a missing, unreadable or malformed file simply means
/// defaults. Losing a preference is a small annoyance, while failing to start
/// because a settings file has a stray comma is not.
/// </summary>
public static class UserSettingsFile
{
    /// <summary>
    /// Where settings live: the platform's per-user configuration directory,
    /// which is <c>~/.config/sleepy</c> on Linux and <c>%AppData%\sleepy</c> on
    /// Windows.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "sleepy",
        "settings.json");

    public static UserSettings Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            return FromJson(JsonNode.Parse(File.ReadAllText(path)));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            return new UserSettings();
        }
    }

    /// <summary>Writes the settings, reporting whether it worked rather than throwing.</summary>
    public static bool Write(string path, UserSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonWriter.Write(ToJson(settings)) + "\n");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Exposed for tests, and for anything that wants the shape without the file.</summary>
    public static UserSettings FromJson(JsonNode? node) => new()
    {
        Language = Json.GetString(node, "language") ?? string.Empty,
        Format = Json.GetString(node, "format") ?? string.Empty,
        Layout = ParseLayout(Json.GetString(node, "layout")),
        RequestHeader = Json.GetString(node, "requestHeader") ?? string.Empty,
        IgnoreCertificateErrors =
            Json.Get(node, "ignoreCertificateErrors") is JsonValue value
            && value.TryGetValue<bool>(out var ignore)
            && ignore,
    };

    public static JsonNode ToJson(UserSettings settings) => new JsonObject
    {
        ["language"] = settings.Language,
        ["format"] = settings.Format,
        ["layout"] = settings.Layout.ToString().ToLowerInvariant(),
        ["requestHeader"] = settings.RequestHeader,
        ["ignoreCertificateErrors"] = settings.IgnoreCertificateErrors,
    };

    private static FolderLayout ParseLayout(string? value)
        => Enum.TryParse<FolderLayout>(value, ignoreCase: true, out var layout) ? layout : FolderLayout.Nested;
}
