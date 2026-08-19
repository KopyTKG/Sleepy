using System.Reflection;

namespace Sleepy;

/// <summary>Shared identity of the tool, used by the CLI, the GUI, and the HTTP user agent.</summary>
public static class SleepyInfo
{
    private static string? _license;
    private static string? _thirdPartyNotices;

    public const string Version = "0.5.0";

    public const string UserAgent = $"sleepy/{Version}";

    public const string Copyright = "Copyright (c) 2026 Martin Kopecký";

    public const string LicenseName = "BSD 3-Clause License";

    /// <summary>The full licence text, embedded so it ships inside every binary.</summary>
    public static string License => _license ??= ReadEmbedded("LICENSE");

    /// <summary>Licences of the components distributed alongside sleepy.</summary>
    public static string ThirdPartyNotices
        => _thirdPartyNotices ??= ReadEmbedded("THIRD-PARTY-NOTICES");

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
