using System.Text.Json.Nodes;
using Sleepy.OpenApi;
using Sleepy.Serialization;

namespace Sleepy.Builders;

/// <summary>
/// Turns one or more parsed OpenAPI 3 documents into a single Insomnia v4 export
/// document. Reached through <see cref="InsomniaV4Builder"/>; the type stays
/// public because it is the reference other builders are written against.
/// </summary>
public static class InsomniaBuilder
{
    private const string WorkspaceId = "wrk_sleepy";
    private const string EnvironmentId = "env_sleepy_base";
    private const string ExportDate = "2020-01-01T00:00:00Z";

    public static JsonNode Build(IReadOnlyList<ApiModule> modules, BuildOptions options)
    {
        var layout = options.Layout;
        // A single module keeps the original flat shape: tag folders straight under
        // the workspace, and unprefixed resource ids.
        var multiModule = modules.Count > 1;
        var baseUrlVariables = CollectionPlan.ResolveBaseUrlVariables(modules);

        var resources = new JsonArray();
        resources.Add((JsonNode)BuildWorkspace(modules, options.WorkspaceName));
        resources.Add((JsonNode)BuildEnvironment(modules, baseUrlVariables, options.BearerToken));

        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        for (var moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            var module = modules[moduleIndex];
            var moduleSlug = multiModule ? Slug.Make(module.Name) : string.Empty;
            var operations = Operations.Collect(module.Spec);

            if (!CollectionPlan.Contributes(module, modules.Count))
            {
                continue;
            }

            var tags = operations.Select(operation => operation.Tag).Distinct().ToList();
            tags.Sort(StringComparer.Ordinal);

            var moduleFolderId = WorkspaceId;
            if (multiModule && layout is FolderLayout.Nested or FolderLayout.ModuleOnly)
            {
                moduleFolderId = UniqueId($"fld_{moduleSlug}", usedIds);
                JsonNode moduleFolder = new JsonObject
                {
                    ["_id"] = moduleFolderId,
                    ["_type"] = "request_group",
                    ["parentId"] = WorkspaceId,
                    ["name"] = module.Name,
                };
                resources.Add(moduleFolder);
            }

            var folderIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in layout == FolderLayout.ModuleOnly ? [] : tags)
            {
                var folderId = UniqueId(
                    multiModule ? $"fld_{moduleSlug}_{Slug.Make(tag)}" : $"fld_{Slug.Make(tag)}",
                    usedIds);
                folderIds[tag] = folderId;

                var folderName = multiModule && layout == FolderLayout.Flat
                    ? $"{module.Name} / {tag}"
                    : tag;

                JsonNode folder = new JsonObject
                {
                    ["_id"] = folderId,
                    ["_type"] = "request_group",
                    ["parentId"] = moduleFolderId,
                    ["name"] = folderName,
                };
                resources.Add(folder);
            }

            var baseUrlVariable = baseUrlVariables[moduleIndex];
            foreach (var operation in operations)
            {
                var requestId = UniqueId(
                    multiModule
                        ? $"req_{moduleSlug}_{Slug.Make(operation.OperationId)}"
                        : $"req_{Slug.Make(operation.OperationId)}",
                    usedIds);

                resources.Add((JsonNode)BuildRequest(
                    operation,
                    requestId,
                    folderIds.TryGetValue(operation.Tag, out var tagFolderId) ? tagFolderId : moduleFolderId,
                    baseUrlVariable,
                    module.Spec));
            }
        }

        return new JsonObject
        {
            ["_type"] = "export",
            ["__export_format"] = 4,
            ["__export_source"] = $"sleepy:{options.SourceName}",
            ["__export_date"] = ExportDate,
            ["resources"] = resources,
        };
    }

    private static JsonObject BuildWorkspace(IReadOnlyList<ApiModule> modules, string? workspaceName)
        => new()
        {
            ["_id"] = WorkspaceId,
            ["_type"] = "workspace",
            ["parentId"] = null,
            ["name"] = CollectionPlan.ResolveName(modules, workspaceName),
            ["description"] = string.Empty,
            ["scope"] = "collection",
        };

    private static JsonObject BuildEnvironment(
        IReadOnlyList<ApiModule> modules,
        string[] baseUrlVariables,
        string bearerToken)
    {
        var data = new JsonObject();
        foreach (var variable in CollectionPlan.ResolveVariables(modules, baseUrlVariables, bearerToken))
        {
            data[variable.Key] = variable.Value;
        }

        return new JsonObject
        {
            ["_id"] = EnvironmentId,
            ["_type"] = "environment",
            ["parentId"] = WorkspaceId,
            ["name"] = "Base Environment",
            ["data"] = data,
        };
    }

    private static string UniqueId(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var next = $"{candidate}_{suffix}";
            if (used.Add(next))
            {
                return next;
            }
        }
    }

    private static JsonObject BuildRequest(
        Operation operation,
        string requestId,
        string parentId,
        string baseUrlVariable,
        JsonNode? spec)
    {
        var bodySchema = FindJsonBodySchema(operation.Node, out var hasRequestBody);

        var headers = new JsonArray();
        if (hasRequestBody)
        {
            JsonNode contentType = new JsonObject
            {
                ["name"] = "Content-Type",
                ["value"] = "application/json",
            };
            headers.Add(contentType);
        }

        var body = new JsonObject();
        if (bodySchema is not null)
        {
            body["mimeType"] = "application/json";
            body["text"] = JsonWriter.Write(ExampleGenerator.Generate(bodySchema, spec));
        }

        return new JsonObject
        {
            ["_id"] = requestId,
            ["_type"] = "request",
            ["parentId"] = parentId,
            ["name"] = $"{operation.Method.ToUpperInvariant()} {operation.Path}",
            ["method"] = operation.Method.ToUpperInvariant(),
            ["url"] = "{{ _." + baseUrlVariable + " }}" + operation.Path,
            ["description"] = operation.Summary,
            ["headers"] = headers,
            ["parameters"] = BuildQueryParameters(operation.Node),
            ["authentication"] = new JsonObject
            {
                ["type"] = "bearer",
                ["token"] = "{{ _." + CollectionPlan.BearerVariable + " }}",
            },
            ["body"] = body,
        };
    }

    private static JsonNode? FindJsonBodySchema(JsonNode? operation, out bool hasRequestBody)
    {
        var requestBody = Json.Get(operation, "requestBody");
        hasRequestBody = requestBody is not null;
        if (!hasRequestBody)
        {
            return null;
        }

        var content = Json.Get(requestBody, "content");
        return Json.Get(Json.Get(content, "application/json"), "schema");
    }

    private static JsonArray BuildQueryParameters(JsonNode? operation)
    {
        var parameters = new JsonArray();
        if (Json.AsArray(Json.Get(operation, "parameters")) is not { } declared)
        {
            return parameters;
        }

        foreach (var parameter in declared)
        {
            if (Json.GetString(parameter, "in") != "query")
            {
                continue;
            }

            JsonNode entry = new JsonObject
            {
                ["name"] = Json.GetString(parameter, "name") ?? string.Empty,
                ["value"] = string.Empty,
                ["disabled"] = false,
                ["description"] = Json.GetString(parameter, "description") ?? string.Empty,
            };
            parameters.Add(entry);
        }

        return parameters;
    }
}
