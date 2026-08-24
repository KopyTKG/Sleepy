using System.Text;

namespace Sleepy.Cli;

/// <summary>
/// sleepy: OpenAPI 3 JSON -&gt; Insomnia v4 export JSON.
/// </summary>
internal static class Program
{
    public static string Version => SleepyInfo.BuildIdentity;

    public static async Task<int> Main(string[] args)
    {
        switch (Cli.Parse(args, out var options))
        {
            case ParseResult.Usage:
                Cli.WriteUsage();
                return 0;
            case ParseResult.Version:
                Console.WriteLine(Version);
                return 0;
            case ParseResult.License:
                WriteLicense();
                return 0;
            case ParseResult.Error:
                Cli.WriteUsage();
                return 2;
        }

        try
        {
            return await RunAsync(options).ConfigureAwait(false);
        }
        catch (SleepyException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("error: the request timed out");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static void WriteLicense()
    {
        Console.WriteLine($"sleepy {SleepyInfo.BuildIdentity} — {SleepyInfo.LicenseName}");
        Console.WriteLine();
        Console.WriteLine(SleepyInfo.License);
        Console.WriteLine();
        Console.WriteLine(SleepyInfo.ThirdPartyNotices);
    }

    private static async Task<int> RunAsync(Options options)
    {
        using var source = new SpecSource(options.Headers, options.Insecure);

        var modules = new List<ApiModule>();
        foreach (var input in options.Inputs)
        {
            var expanded = await ModuleDiscovery
                .ExpandAsync(input, source, options.BaseUrl, CancellationToken.None)
                .ConfigureAwait(false);
            modules.AddRange(expanded);
        }

        modules = ApplyModuleFilters(modules, options.ModuleFilters);
        if (modules.Count == 0)
        {
            throw new SleepyException("no modules matched the given input");
        }

        if (options.ListOnly)
        {
            foreach (var module in modules)
            {
                Console.WriteLine($"{module.Name}\t{module.SourceName}");
            }

            return 0;
        }

        if (modules.Count > 1)
        {
            Console.Error.WriteLine(
                $"merging {modules.Count} modules: {string.Join(", ", modules.Select(module => module.Name))}");
        }

        var sourceName = string.Join(",", options.Inputs.Select(input => input == "-" ? "stdin" : input));
        var builder = options.Format.Length == 0
            ? CollectionBuilders.Default
            : CollectionBuilders.Resolve(options.Format);

        var collection = builder.Build(modules, new BuildOptions
        {
            SourceName = sourceName,
            WorkspaceName = options.WorkspaceName,
            Layout = options.Layout,
            BearerToken = options.BearerToken,
        });

        if (!collection.IsSingleFile)
        {
            return await WriteFolderCollectionAsync(collection, options).ConfigureAwait(false);
        }

        var output = collection.Content;

        if (options.OutputPath.Length == 0)
        {
            Console.Out.WriteLine(output);
        }
        else
        {
            await File.WriteAllTextAsync(options.OutputPath, output, new UTF8Encoding(false))
                .ConfigureAwait(false);
            Console.Error.WriteLine($"wrote {options.OutputPath}");
        }

        return 0;
    }

    /// <summary>
    /// Writes a format that is a directory of files rather than one document.
    /// </summary>
    private static async Task<int> WriteFolderCollectionAsync(BuiltCollection collection, Options options)
    {
        var root = options.OutputPath.Length == 0
            ? throw new SleepyException(
                $"the {collection.FormatId} format writes a directory; give one with -o")
            : options.OutputPath;

        foreach (var file in collection.Files)
        {
            var path = Path.Combine(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false))
                .ConfigureAwait(false);
        }

        Console.Error.WriteLine($"wrote {collection.Files.Count} files to {root}");
        return 0;
    }

    private static List<ApiModule> ApplyModuleFilters(
        List<ApiModule> modules,
        IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
        {
            return modules;
        }

        var wanted = new HashSet<string>(filters, StringComparer.OrdinalIgnoreCase);
        var kept = modules.Where(module => wanted.Contains(module.Name)).ToList();

        foreach (var filter in filters)
        {
            if (!modules.Any(module => string.Equals(module.Name, filter, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"warning: no module named {filter}");
            }
        }

        return kept;
    }
}
