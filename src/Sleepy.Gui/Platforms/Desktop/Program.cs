using System.Diagnostics;
using System.Globalization;
using Sleepy.Gui.Services;
using Uno.UI.Hosting;

namespace Sleepy.Gui;

internal class Program
{
    /// <summary>The variable Uno's X11 host reads its display scale from.</summary>
    private const string ScaleVariable = "UNO_DISPLAY_SCALE_OVERRIDE";

    private static string[] _args = [];

    [STAThread]
    public static void Main(string[] args)
    {
        _args = args;
        ApplyUiScale(args);
        App.InitializeLogging();

        var builder = UnoPlatformHostBuilder.Create()
            .App(() => new App());

        // Linux has two hosts: X11 (also used on Wayland sessions, through
        // XWayland) and the framebuffer/DRM host for a bare console. Which one
        // runs can be forced with --display or SLEEPY_GUI_DISPLAY, so the
        // desktop entries can pick per session instead of guessing.
        switch (ResolveDisplayMode(args))
        {
            case "x11":
                builder = builder.UseX11();
                break;

            case "framebuffer":
                builder = builder.UseLinuxFrameBuffer();
                break;

            default:
                builder = builder
                    .UseX11()
                    .UseLinuxFrameBuffer()
                    .UseMacOS()
                    .UseWin32();
                break;
        }

        try
        {
            builder.Build().Run();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("platform host"))
        {
            Console.Error.WriteLine(
                "sleepy-GUI: no display available. Run it from an X11 or Wayland session, "
                + "or from a TTY with --display framebuffer.");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Sets the interface scale before the host starts, since it is read once,
    /// with the first window. In order of precedence: <c>--scale</c>, a
    /// <c>UNO_DISPLAY_SCALE_OVERRIDE</c> already in the environment, then the
    /// saved setting. Only the X11 host reads it; Windows scales by its own
    /// display settings.
    /// </summary>
    private static void ApplyUiScale(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var scale = UserSettings.ParseUiScale(ArgumentValue(args, "--scale"));
        if (scale == 0 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ScaleVariable)))
        {
            return;
        }

        if (scale == 0)
        {
            scale = Settings.Current.UiScale;
        }

        if (scale > 0)
        {
            Environment.SetEnvironmentVariable(ScaleVariable, scale.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Starts a fresh copy of the app with the given scale and closes this one —
    /// the only way to change a scale the host read at startup.
    /// </summary>
    public static bool Restart(double scale)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return false;
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in WithoutScale(_args))
        {
            start.ArgumentList.Add(argument);
        }

        if (scale > 0)
        {
            start.Environment[ScaleVariable] = scale.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            start.Environment.Remove(ScaleVariable);
        }

        try
        {
            Process.Start(start);
        }
        catch (Exception)
        {
            return false;
        }

        Application.Current.Exit();
        return true;
    }

    private static IEnumerable<string> WithoutScale(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--scale=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (args[i].Equals("--scale", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            yield return args[i];
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }

            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string ResolveDisplayMode(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--display=", StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(args[i]["--display=".Length..]);
            }

            if (args[i].Equals("--display", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return Normalize(args[i + 1]);
            }
        }

        return Normalize(Environment.GetEnvironmentVariable("SLEEPY_GUI_DISPLAY") ?? string.Empty);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant() switch
    {
        "x11" or "xwayland" or "wayland" => "x11",
        "framebuffer" or "fb" or "drm" or "console" => "framebuffer",
        _ => "auto",
    };
}
