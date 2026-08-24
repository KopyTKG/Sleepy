namespace Sleepy.Gui.Services;

/// <summary>
/// The app's remembered choices, loaded once at startup and written back when
/// something changes.
///
/// Writes are best-effort: a read-only home directory costs the user their
/// preferences, not their session.
/// </summary>
public static class Settings
{
    private static UserSettings _current = new();
    private static bool _loaded;

    /// <summary>Where the file lives, so the About dialog and docs can name it.</summary>
    public static string Path => UserSettingsFile.DefaultPath;

    public static UserSettings Current
    {
        get
        {
            Load();
            return _current;
        }
    }

    /// <summary>Reads the file, once per run.</summary>
    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _current = UserSettingsFile.Read(Path);
        _loaded = true;
    }

    /// <summary>Applies a change and writes it back.</summary>
    public static void Update(Func<UserSettings, UserSettings> change)
    {
        Load();
        var updated = change(_current);
        if (updated == _current)
        {
            return;
        }

        _current = updated;
        UserSettingsFile.Write(Path, _current);
    }
}
