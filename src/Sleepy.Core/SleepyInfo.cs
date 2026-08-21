using System.Reflection;

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
    private static string? _thirdPartyNotices;

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

    /// <summary>Licences of the components distributed alongside sleepy.</summary>
    public static string ThirdPartyNotices =>
        _thirdPartyNotices ??= ReadEmbedded("THIRD-PARTY-NOTICES");

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
