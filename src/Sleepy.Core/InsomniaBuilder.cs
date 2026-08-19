using System.Text.Json.Nodes;

namespace Sleepy;

public enum FolderLayout
{
    /// <summary>A folder per module, with a tag subfolder inside it.</summary>
    Nested,

    /// <summary>A single folder level named "&lt;Module&gt; / &lt;Tag&gt;".</summary>
    Flat,

    /// <summary>A folder per module only; requests sit directly inside it.</summary>
    ModuleOnly,
}

/// <summary>Everything about the export that is not the specifications themselves.</summary>
public sealed record BuildOptions
{
    /// <summary>Recorded in <c>__export_source</c>.</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>Workspace name; defaults to the spec title, or the host for several modules.</summary>
    public string? WorkspaceName { get; init; }

    public FolderLayout Layout { get; init; } = FolderLayout.Nested;

    /// <summary>Prefills the environment's <c>bearer</c> variable.</summary>
    public string BearerToken { get; init; } = string.Empty;
}

/// <summary>
/// Turns one or more parsed OpenAPI 3 documents into a single Insomnia v4 export
/// document.
/// </summary>
public static class InsomniaBuilder
{
    private const string WorkspaceId = "wrk_sleepy";
    private const string EnvironmentId = "env_sleepy_base";
    private const string ExportDate = "2020-01-01T00:00:00Z";
    private const string DefaultTitle = "OpenAPI Import";
    private const string DefaultTag = "default";
    private const string BearerVariable = "bearer";
    private const string BaseUrlVariable = "baseUrl";

    private static readonly string[] HttpMethods = ["get", "post", "put", "delete", "patch"];

    private sealed record Operation(
        string Method,
        string Path,
        string Tag,
        string OperationId,
        string Summary,
        JsonNode? Node);

    public static JsonNode Build(IReadOnlyList<ApiModule> modules, BuildOptions options)
    {
        var layout = options.Layout;
        // A single module keeps the original flat shape: tag folders straight under
        // the workspace, and unprefixed resource ids.
        var multiModule = modules.Count > 1;
        var baseUrlVariables = ResolveBaseUrlVariables(modules);

        var resources = new JsonArray();
        resources.Add((JsonNode)BuildWorkspace(modules, options.WorkspaceName));
        resources.Add((JsonNode)BuildEnvironment(modules, baseUrlVariables, options.BearerToken));

        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        for (var moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            var module = modules[moduleIndex];
            var moduleSlug = multiModule ? Slug.Make(module.Name) : string.Empty;
            var operations = CollectOperations(module.Spec);

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

    /// <summary>Counts the operations a module contributes, for display purposes.</summary>
    public static int CountOperations(ApiModule module) => CollectOperations(module.Spec).Count;

    /// <summary>
    /// Maps each module to the environment variable holding its base URL. Modules
    /// that share an origin share one <c>baseUrl</c> variable; when the origins
    /// differ, each module gets its own <c>baseUrl_&lt;module&gt;</c>.
    /// </summary>
    private static string[] ResolveBaseUrlVariables(IReadOnlyList<ApiModule> modules)
    {
        var origins = modules
            .Select(module => module.BaseUrl)
            .Where(baseUrl => baseUrl.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var shared = origins.Count <= 1;
        return modules
            .Select(module => shared ? BaseUrlVariable : $"{BaseUrlVariable}_{Slug.Make(module.Name)}")
            .ToArray();
    }

    private static JsonObject BuildWorkspace(IReadOnlyList<ApiModule> modules, string? workspaceName)
    {
        var name = workspaceName;
        if (string.IsNullOrEmpty(name) && modules.Count == 1)
        {
            name = Json.GetString(Json.Get(modules[0].Spec, "info"), "title");
        }

        if (string.IsNullOrEmpty(name) && modules.Count > 1)
        {
            // Several modules have several titles; the host they were fetched from
            // is the most useful shared label.
            name = Uri.TryCreate(modules[0].SourceName, UriKind.Absolute, out var uri) ? uri.Host : null;
        }

        return new JsonObject
        {
            ["_id"] = WorkspaceId,
            ["_type"] = "workspace",
            ["parentId"] = null,
            ["name"] = string.IsNullOrEmpty(name) ? DefaultTitle : name,
            ["description"] = string.Empty,
            ["scope"] = "collection",
        };
    }

    private static JsonObject BuildEnvironment(
        IReadOnlyList<ApiModule> modules,
        string[] baseUrlVariables,
        string bearerToken)
    {
        var data = new JsonObject();
        for (var i = 0; i < modules.Count; i++)
        {
            data[baseUrlVariables[i]] = modules[i].BaseUrl;
        }

        if (data.Count == 0)
        {
            data[BaseUrlVariable] = string.Empty;
        }

        data[BearerVariable] = bearerToken;

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

    private static List<Operation> CollectOperations(JsonNode? spec)
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
                ["token"] = "{{ _." + BearerVariable + " }}",
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
