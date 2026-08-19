using Uno.UI.Hosting;

namespace Sleepy.Gui;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
