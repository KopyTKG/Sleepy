namespace Sleepy;

/// <summary>Shared identity of the tool, used by the CLI, the GUI, and the HTTP user agent.</summary>
public static class SleepyInfo
{
    public const string Version = "0.5.0";

    public const string UserAgent = $"sleepy/{Version}";
}
