using System.Text;
using Sleepy;

namespace Sleepy.Cli;

/// <summary>
/// sleepy: OpenAPI 3 JSON -&gt; Insomnia v4 export JSON.
/// </summary>
internal static class Program
{
    public const string Version = SleepyInfo.Version;

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

    private static async Task<int> RunAsync(Options options)
    {
        using var source = new SpecSource(options.Headers, options.Insecure);

        var modules = new List<ApiModule>();
        foreach (var input in options.Inputs)
        {
            var expanded = await Discovery
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
        var document = InsomniaBuilder.Build(modules, new BuildOptions
        {
            SourceName = sourceName,
            WorkspaceName = options.WorkspaceName,
            Layout = options.Layout,
            BearerToken = options.BearerToken,
        });
        var output = JsonWriter.Write(document);

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
