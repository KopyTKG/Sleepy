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

    /// <summary>
    /// Interface scale, 1.0 being 100 %; 0 follows the display. Applied when the
    /// app starts, because the display host reads it before the first window.
    /// </summary>
    public double UiScale { get; init; }

    /// <summary>The narrowest and widest scales worth offering.</summary>
    public const double MinUiScale = 0.5;

    public const double MaxUiScale = 3.0;

    /// <summary>A scale clamped to the supported range, or 0 for "follow the display".</summary>
    public static double NormalizeUiScale(double scale)
        => double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, MinUiScale, MaxUiScale) : 0;

    /// <summary>
    /// Reads <c>1.5</c>, <c>150</c> and <c>150%</c> as the same scale, as typed
    /// after <c>--scale</c>; 0 when the text is not a scale.
    /// </summary>
    public static double ParseUiScale(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var trimmed = text.Trim();
        var percent = trimmed.EndsWith('%');
        if (!double.TryParse(
                trimmed.TrimEnd('%').Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            return 0;
        }

        // Nobody means a fifteen-fold interface: a large number is a percentage.
        if (percent || value > 10)
        {
            value /= 100;
        }

        return NormalizeUiScale(value);
    }
}
