using System.Text.Json.Nodes;
using Sleepy.Serialization;

namespace Sleepy.OpenApi;

/// <summary>
/// Reads the operations out of an OpenAPI document. Every builder starts here, so
/// the rules for tags, operation ids and ordering are decided once rather than
/// per output format.
/// </summary>
public static class Operations
{
    /// <summary>The tag used when an operation declares none.</summary>
    public const string DefaultTag = "default";

    private static readonly string[] HttpMethods = ["get", "post", "put", "delete", "patch"];

    /// <summary>
    /// Collects every operation, paths in ordinal order and methods in a fixed
    /// order, so the output of any builder is deterministic.
    /// </summary>
    public static List<Operation> Collect(JsonNode? spec)
    {
        var operations = new List<Operation>();
        if (Json.AsObject(Json.Get(spec, "paths")) is not { } paths)
        {
            return operations;
        }

        var pathKeys = paths.Select(pair => pair.Key).ToList();
        pathKeys.Sort(StringComparer.Ordinal);

        foreach (var path in pathKeys)
        {
            var pathItem = paths[path];
            foreach (var method in HttpMethods)
            {
                if (Json.Get(pathItem, method) is not { } operation)
                {
                    continue;
                }

                var tag = DefaultTag;
                if (Json.AsArray(Json.Get(operation, "tags")) is { Count: > 0 } tags
                    && Json.AsString(tags[0]) is { Length: > 0 } firstTag)
                {
                    tag = firstTag;
                }

                var operationId = Json.GetString(operation, "operationId");
                if (string.IsNullOrEmpty(operationId))
                {
                    operationId = $"{method}_{path}";
                }

                var summary = Json.GetString(operation, "summary");
                if (string.IsNullOrEmpty(summary))
                {
                    summary = Json.GetString(operation, "description") ?? string.Empty;
                }

                operations.Add(new Operation(method, path, tag, operationId, summary, operation));
            }
        }

        return operations;
    }

    /// <summary>How many requests a module contributes, for display purposes.</summary>
    public static int Count(ApiModule module) => Collect(module.Spec).Count;
}
