using System.Text;
using System.Text.Json.Nodes;
using Sleepy.OpenApi;
using Sleepy.Serialization;

namespace Sleepy.Builders;

/// <summary>
/// A Bruno collection: a directory tree rather than one document. Every request
/// is a <c>.bru</c> file in its own block syntax, folders on disk mirror the
/// collection tree, <c>bruno.json</c> marks the root, and variables live in
/// <c>environments/</c>.
///
/// This is the format <see cref="BuiltCollection"/>'s file list exists for: the
/// builder returns paths and contents, and the caller decides where to put them.
/// </summary>
public sealed class BrunoBuilder : ICollectionBuilder
{
    public string FormatId => "bruno";

    public string DisplayName => "Bruno";

    public string DefaultOutputName => "bruno-collection";

    public bool IsFolderFormat => true;

    public BuiltCollection Build(IReadOnlyList<ApiModule> modules, BuildOptions options)
    {
        var name = CollectionPlan.ResolveName(modules, options.WorkspaceName);
        var baseUrlVariables = CollectionPlan.ResolveBaseUrlVariables(modules);
        var multiModule = modules.Count > 1;

        var files = new List<BuiltFile>
        {
            new("bruno.json", CollectionFile(name)),
            new(
                "environments/Base Environment.bru",
                EnvironmentFile(CollectionPlan.ResolveVariables(modules, baseUrlVariables, options.BearerToken))),
        };

        // Bruno orders items by the seq in each file, so requests are numbered as
        // they are written and folders carry a seq of their own.
        var folderSequence = 1;
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < modules.Count; index++)
        {
            var module = modules[index];
            if (!CollectionPlan.Contributes(module, modules.Count))
            {
                continue;
            }

            var operations = Operations.Collect(module.Spec);
            var variable = baseUrlVariables[index];

            foreach (var group in GroupOperations(module, operations, options.Layout, multiModule))
            {
                var directory = string.Join('/', group.Key.Select(SanitizeSegment));
                if (directory.Length > 0)
                {
                    files.Add(new BuiltFile(
                        $"{directory}/folder.bru",
                        FolderFile(group.Key[^1], folderSequence++)));
                }

                var sequence = 1;
                foreach (var operation in group.Value)
                {
                    var fileName = UniqueName(directory, operation, usedPaths);
                    files.Add(new BuiltFile(fileName, RequestFile(operation, variable, module.Spec, sequence++)));
                }
            }
        }

        return new BuiltCollection(FormatId, files);
    }

    /// <summary>
    /// The directory each operation belongs in, keyed by path segments, in the
    /// order the layout puts them.
    /// </summary>
    private static List<KeyValuePair<string[], List<Operation>>> GroupOperations(
        ApiModule module,
        List<Operation> operations,
        FolderLayout layout,
        bool multiModule)
    {
        var groups = new List<KeyValuePair<string[], List<Operation>>>();

        if (layout == FolderLayout.ModuleOnly)
        {
            groups.Add(new(multiModule ? [module.Name] : [], operations));
            return groups;
        }

        var tags = operations.Select(operation => operation.Tag).Distinct().ToList();
        tags.Sort(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            var inTag = operations.Where(operation => operation.Tag == tag).ToList();
            string[] segments = (multiModule, layout) switch
            {
                (true, FolderLayout.Flat) => [$"{module.Name} - {tag}"],
                (true, _) => [module.Name, tag],
                _ => [tag],
            };

            groups.Add(new(segments, inTag));
        }

        return groups;
    }

    private static string CollectionFile(string name) =>
        JsonWriter.Write(new JsonObject
        {
            ["version"] = "1",
            ["name"] = name,
            ["type"] = "collection",
            ["ignore"] = IgnoreList(),
        }) + "\n";

    private static JsonArray IgnoreList()
    {
        var ignore = new JsonArray();
        ignore.Add((JsonNode)JsonValue.Create("node_modules"));
        ignore.Add((JsonNode)JsonValue.Create(".git"));
        return ignore;
    }

    private static string EnvironmentFile(SortedDictionary<string, string> variables)
    {
        var text = new StringBuilder();
        text.Append("vars {\n");
        foreach (var variable in variables)
        {
            text.Append($"  {variable.Key}: {variable.Value}\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    private static string FolderFile(string name, int sequence) =>
        $"meta {{\n  name: {Escape(name)}\n  seq: {sequence}\n}}\n";

    private static string RequestFile(Operation operation, string baseUrlVariable, JsonNode? spec, int sequence)
    {
        var method = operation.Method.ToLowerInvariant();
        var url = "{{" + baseUrlVariable + "}}" + operation.Path;
        var body = RequestBody(operation, spec);

        var text = new StringBuilder();
        text.Append("meta {\n");
        text.Append($"  name: {Escape($"{operation.Method.ToUpperInvariant()} {operation.Path}")}\n");
        text.Append("  type: http\n");
        text.Append($"  seq: {sequence}\n");
        text.Append("}\n\n");

        text.Append($"{method} {{\n");
        text.Append($"  url: {url}\n");
        text.Append($"  body: {(body is null ? "none" : "json")}\n");
        text.Append("  auth: bearer\n");
        text.Append("}\n");

        if (QueryParameters(operation) is { Count: > 0 } query)
        {
            text.Append("\nparams:query {\n");
            foreach (var parameter in query)
            {
                text.Append($"  {parameter}: \n");
            }

            text.Append("}\n");
        }

        if (body is not null)
        {
            text.Append("\nheaders {\n  Content-Type: application/json\n}\n");
        }

        text.Append("\nauth:bearer {\n");
        text.Append("  token: {{" + CollectionPlan.BearerVariable + "}}\n");
        text.Append("}\n");

        if (body is not null)
        {
            text.Append("\nbody:json {\n");
            text.Append(Indent(body));
            text.Append("}\n");
        }

        if (operation.Summary.Length > 0)
        {
            text.Append($"\ndocs {{\n{Indent(operation.Summary)}}}\n");
        }

        return text.ToString();
    }

    private static List<string> QueryParameters(Operation operation)
    {
        var names = new List<string>();
        if (Json.AsArray(Json.Get(operation.Node, "parameters")) is not { } parameters)
        {
            return names;
        }

        foreach (var parameter in parameters)
        {
            if (Json.GetString(parameter, "in") == "query"
                && Json.GetString(parameter, "name") is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string? RequestBody(Operation operation, JsonNode? spec)
    {
        var schema = Json.Get(
            Json.Get(Json.Get(Json.Get(operation.Node, "requestBody"), "content"), "application/json"),
            "schema");

        return schema is null ? null : JsonWriter.Write(ExampleGenerator.Generate(schema, spec));
    }

    /// <summary>Bruno's blocks are indented by two spaces; keep the nesting readable.</summary>
    private static string Indent(string text)
    {
        var builder = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            builder.Append(line.Length == 0 ? "\n" : $"  {line}\n");
        }

        return builder.ToString();
    }

    /// <summary>A file name for one request, unique within its directory.</summary>
    private static string UniqueName(string directory, Operation operation, HashSet<string> used)
    {
        var stem = Slug.Make(operation.OperationId);
        if (stem.Length == 0)
        {
            stem = Slug.Make($"{operation.Method}_{operation.Path}");
        }

        var candidate = directory.Length == 0 ? $"{stem}.bru" : $"{directory}/{stem}.bru";
        if (used.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var next = directory.Length == 0 ? $"{stem}_{suffix}.bru" : $"{directory}/{stem}_{suffix}.bru";
            if (used.Add(next))
            {
                return next;
            }
        }
    }

    /// <summary>Keeps a folder name usable as a directory on every platform.</summary>
    private static string SanitizeSegment(string segment)
    {
        var cleaned = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            cleaned.Append(char.IsControl(character) || "/\\:*?\"<>|".Contains(character, StringComparison.Ordinal)
                ? '-'
                : character);
        }

        return cleaned.ToString().Trim().Trim('.');
    }

    /// <summary>Block values are single lines, so newlines cannot travel in them.</summary>
    private static string Escape(string value)
        => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
