using Sleepy;

namespace Sleepy.Cli;

internal sealed record Options
{
    public required IReadOnlyList<string> Inputs { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }

    public string? WorkspaceName { get; init; }

    public IReadOnlyList<string> ModuleFilters { get; init; } = [];

    public IReadOnlyList<string> Headers { get; init; } = [];

    public FolderLayout Layout { get; init; } = FolderLayout.Nested;

    public string BearerToken { get; init; } = string.Empty;

    public bool Insecure { get; init; }

    public bool ListOnly { get; init; }
}

internal enum ParseResult
{
    Ok,
    Usage,
    Version,
    License,
    Error,
}

internal static class Cli
{
    public static ParseResult Parse(string[] args, out Options options)
    {
        options = new Options { Inputs = [] };

        if (args.Length == 0)
        {
            return ParseResult.Error;
        }

        var inputs = new List<string>();
        var moduleFilters = new List<string>();
        var headers = new List<string>();
        var outputPath = string.Empty;
        string? baseUrl = null;
        string? workspaceName = null;
        var layout = FolderLayout.Nested;
        var bearerToken = string.Empty;
        var insecure = false;
        var listOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (!TryTakeValue(args, ref i, "-o", out outputPath))
                    {
                        return ParseResult.Error;
                    }

                    break;

                case "--base-url":
                    if (!TryTakeValue(args, ref i, "--base-url", out var parsedBaseUrl))
                    {
                        return ParseResult.Error;
                    }

                    baseUrl = parsedBaseUrl;
                    break;

                case "--name":
                    if (!TryTakeValue(args, ref i, "--name", out var parsedName))
                    {
                        return ParseResult.Error;
                    }

                    workspaceName = parsedName;
                    break;

                case "--module":
                    if (!TryTakeValue(args, ref i, "--module", out var parsedModule))
                    {
                        return ParseResult.Error;
                    }

                    moduleFilters.Add(parsedModule);
                    break;

                case "--header":
                case "-H":
                    if (!TryTakeValue(args, ref i, "--header", out var parsedHeader))
                    {
                        return ParseResult.Error;
                    }

                    headers.Add(parsedHeader);
                    break;

                case "--flat":
                    layout = FolderLayout.Flat;
                    break;

                case "--no-tags":
                    layout = FolderLayout.ModuleOnly;
                    break;

                case "--bearer":
                    if (!TryTakeValue(args, ref i, "--bearer", out var parsedBearer))
                    {
                        return ParseResult.Error;
                    }

                    bearerToken = parsedBearer;
                    break;

                case "--insecure":
                case "-k":
                    insecure = true;
                    break;

                case "--list":
                    listOnly = true;
                    break;

                case "-h":
                case "--help":
                    return ParseResult.Usage;

                case "-v":
                case "--version":
                    return ParseResult.Version;

                case "--license":
                    return ParseResult.License;

                default:
                    inputs.Add(args[i]);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            return ParseResult.Error;
        }

        options = new Options
        {
            Inputs = inputs,
            OutputPath = outputPath,
            BaseUrl = baseUrl,
            WorkspaceName = workspaceName,
            ModuleFilters = moduleFilters,
            Headers = headers,
            Layout = layout,
            BearerToken = bearerToken,
            Insecure = insecure,
            ListOnly = listOnly,
        };

        return ParseResult.Ok;
    }

    private static bool TryTakeValue(string[] args, ref int index, string flag, out string value)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"error: {flag} requires an argument");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    public static void WriteUsage()
    {
        Console.Error.WriteLine("sleepy - convert OpenAPI 3 JSON specs into an Insomnia v4 export");
        Console.Error.WriteLine();
        Console.Error.WriteLine("usage: sleepy <spec|url|-> [<spec|url> ...] [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Each input may be a file path, an http(s) URL, or - for stdin. A URL may");
        Console.Error.WriteLine("point at a single OpenAPI document or at a Swagger UI that serves several");
        Console.Error.WriteLine("(the \"Select a definition\" dropdown); in the latter case every module is");
        Console.Error.WriteLine("fetched and merged into one collection.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("options:");
        Console.Error.WriteLine("  -o, --output <file>   write the export to a file instead of stdout");
        Console.Error.WriteLine("      --base-url <url>  override the environment's baseUrl for every module");
        Console.Error.WriteLine("      --name <name>     workspace name (default: spec title / \"OpenAPI Import\")");
        Console.Error.WriteLine("      --module <name>   only include this module (repeatable, case-insensitive)");
        Console.Error.WriteLine("      --flat            one folder level named \"<Module> / <Tag>\"");
        Console.Error.WriteLine("      --no-tags         a folder per module only, without tag subfolders");
        Console.Error.WriteLine("      --bearer <token>  prefill the environment's bearer variable");
        Console.Error.WriteLine("  -H, --header <h>      extra request header when fetching, e.g. \"Authorization: Bearer x\"");
        Console.Error.WriteLine("  -k, --insecure        do not verify TLS certificates when fetching");
        Console.Error.WriteLine("      --list            list the discovered modules and exit");
        Console.Error.WriteLine("  -h, --help            show this help");
        Console.Error.WriteLine("  -v, --version         show the version");
        Console.Error.WriteLine("      --license         show the licence and third-party notices");
    }
}
