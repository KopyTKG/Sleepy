using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>
/// Shared identity of the tool: version, build provenance, licence, and the HTTP
/// user agent.
///
/// The version comes from the assembly rather than a constant, so
/// <c>&lt;Version&gt;</c> in Directory.Build.props is the only place it is
/// written. The build appends the commit it was built from, which is what turns
/// "0.5.0" into an answer to "which 0.5.0 is this?".
/// </summary>
public static class SleepyInfo
{
    private static string? _license;
    private static IReadOnlyList<ThirdPartyComponent>? _components;
    private static string? _thirdPartyNotes;

    /// <summary>
    /// The full informational version, e.g. <c>0.5.0+c5fac9f</c>, or
    /// <c>0.5.0+c5fac9f.dirty</c> when built from a modified tree.
    /// </summary>
    public static string FullVersion { get; } = ReadInformationalVersion();

    /// <summary>The version without build metadata, e.g. <c>0.5.0</c>.</summary>
    public static string Version { get; } = FullVersion.Split('+')[0];

    /// <summary>
    /// The commit this build came from, or null when the build had no git
    /// information (a source archive, for instance).
    /// </summary>
    public static string? Commit { get; } =
        FullVersion.Contains('+', StringComparison.Ordinal) ? FullVersion.Split('+')[1] : null;

    /// <summary>Whether this is a debug build.</summary>
    public static bool IsDebugBuild { get; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// The one-line build identity shown by `sleepy --version` and the About
    /// dialog: version, commit, and the build kind when it is not a release.
    /// </summary>
    public static string BuildIdentity => IsDebugBuild ? $"{FullVersion} (debug)" : FullVersion;

    public static string UserAgent { get; } = $"sleepy/{Version}";

    public const string Copyright = "Copyright (c) 2026 Martin Kopecký";

    public const string LicenseName = "BSD 3-Clause License";

    /// <summary>The full licence text, embedded so it ships inside every binary.</summary>
    public static string License => _license ??= ReadEmbedded("LICENSE");

    /// <summary>
    /// The components distributed alongside sleepy, as data rather than prose, so
    /// each surface can render them the way it renders anything else.
    /// </summary>
    public static IReadOnlyList<ThirdPartyComponent> ThirdPartyComponents
    {
        get
        {
            EnsureNoticesRead();
            return _components ?? [];
        }
    }

    /// <summary>The obligations worth knowing about, in one paragraph.</summary>
    public static string ThirdPartyNotes
    {
        get
        {
            EnsureNoticesRead();
            return _thirdPartyNotes ?? string.Empty;
        }
    }

    /// <summary>The notices as aligned plain text, for the command line.</summary>
    public static string ThirdPartyNotices
    {
        get
        {
            var components = ThirdPartyComponents;
            if (components.Count == 0)
            {
                return string.Empty;
            }

            var nameWidth = components.Max(component => component.Name.Length);
            var licenceWidth = components.Max(component => component.License.Length);

            var text = new StringBuilder();
            text.AppendLine("Third-party components");
            text.AppendLine();
            foreach (var component in components)
            {
                text.Append("  ")
                    .Append(component.Name.PadRight(nameWidth))
                    .Append("  ")
                    .Append(component.License.PadRight(licenceWidth))
                    .Append("  ")
                    .AppendLine(component.UsedBy);
            }

            if (ThirdPartyNotes.Length > 0)
            {
                text.AppendLine();
                text.AppendLine(ThirdPartyNotes);
            }

            return text.ToString().TrimEnd();
        }
    }

    private static void EnsureNoticesRead()
    {
        if (_components is not null)
        {
            return;
        }

        var notices = JsonNode.Parse(ReadEmbedded("THIRD-PARTY-NOTICES"));
        _thirdPartyNotes = notices?["notes"]?.GetValue<string>() ?? string.Empty;
        _components = (notices?["components"] as JsonArray ?? [])
            .Select(entry => new ThirdPartyComponent(
                entry?["name"]?.GetValue<string>() ?? string.Empty,
                entry?["license"]?.GetValue<string>() ?? string.Empty,
                entry?["usedBy"]?.GetValue<string>() ?? string.Empty,
                entry?["url"]?.GetValue<string>() ?? string.Empty))
            .ToList();
    }

    private static string ReadInformationalVersion()
    {
        var assembly = typeof(SleepyInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(SleepyInfo).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd();
    }
}
