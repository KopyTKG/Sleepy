namespace Sleepy;

/// <summary>
/// What the desktop app remembers between runs: the choices a user would be
/// annoyed to make twice.
///
/// Deliberately absent: the bearer token and the base URL. Both are per-API and
/// one of them is a credential, and a settings file is the wrong place to keep a
/// credential.
/// </summary>
public sealed record UserSettings
{
    /// <summary>Language tag the app is pinned to, or empty to follow the system.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Output format id, as registered by the builders.</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Folder layout last chosen.</summary>
    public FolderLayout Layout { get; init; } = FolderLayout.Nested;

    /// <summary>Extra header sent when fetching specifications, e.g. an API key.</summary>
    public string RequestHeader { get; init; } = string.Empty;

    /// <summary>Whether TLS certificate errors are ignored while fetching.</summary>
    public bool IgnoreCertificateErrors { get; init; }
}
