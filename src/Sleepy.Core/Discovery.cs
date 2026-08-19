using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>One API module: a single OpenAPI document plus where it came from.</summary>
public sealed record ApiModule(string Name, JsonNode? Spec, string BaseUrl, string SourceName);

/// <summary>A module reference discovered in a Swagger UI configuration.</summary>
public sealed record ModuleRef(string Name, string Url);

/// <summary>
/// Expands one input (file, stdin, spec URL, or Swagger UI entry point) into the
/// list of OpenAPI documents it refers to.
///
/// Multi-document Swagger UIs (Swashbuckle's "Select a definition" dropdown,
/// springdoc's grouped APIs, springfox's resource list) publish their document
/// list as JSON; this walks the usual endpoints to find it.
/// </summary>
public static class Discovery
{
    /// <summary>Endpoints probed, relative to the input URL, when it is not itself a document list.</summary>
    private static readonly string[] ConfigCandidates =
    [
        "swagger/swagger-ui-init.js",
        "swagger-ui-init.js",
        "swagger/v1/swagger.json",
        "v3/api-docs/swagger-config",
        "swagger-config",
        "swagger-resources",
    ];

    public static async Task<List<ApiModule>> ExpandAsync(
        string input,
        SpecSource source,
        string? baseUrlOverride,
        CancellationToken cancellationToken)
    {
        if (input == "-" || !SpecSource.IsUrl(input))
        {
            byte[] bytes;
            var name = input;
            if (input == "-")
            {
                bytes = await SpecSource.ReadStdinAsync(cancellationToken).ConfigureAwait(false);
                name = "stdin";
            }
            else
            {
                if (!File.Exists(input))
                {
                    throw new SleepyException($"could not read file: {input}");
                }

                bytes = await File.ReadAllBytesAsync(input, cancellationToken).ConfigureAwait(false);
            }

            var local = Parse(bytes, name);

            // A local file may also be a manifest listing several documents.
            if (local is not null && !IsOpenApiDocument(local)
                && ReadModuleRefs(local, input == "-" ? Environment.CurrentDirectory + "/" : input)
                    is { Count: > 0 } localRefs)
            {
                return await LoadModulesAsync(localRefs, source, baseUrlOverride, cancellationToken)
                    .ConfigureAwait(false);
            }

            return [ToModule(local, name, baseUrlOverride ?? string.Empty)];
        }

        var fetched = await source.FetchAsync(input, cancellationToken).ConfigureAwait(false);
        var text = SpecParser.DecodeUtf8(fetched.Data);

        // The URL may already be a plain OpenAPI document.
        if (TryParseNode(text) is { } node)
        {
            if (IsOpenApiDocument(node))
            {
                return [ToModule(node, input, baseUrlOverride ?? SpecSource.Origin(input))];
            }

            if (ReadModuleRefs(node, input) is { Count: > 0 } refsFromJson)
            {
                return await LoadModulesAsync(refsFromJson, source, baseUrlOverride, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Swashbuckle serves its configuration inside swagger-ui-init.js.
        if (ExtractUrlsFromScript(text, input) is { Count: > 0 } refsFromScript)
        {
            return await LoadModulesAsync(refsFromScript, source, baseUrlOverride, cancellationToken)
                .ConfigureAwait(false);
        }

        var probed = await ProbeAsync(input, source, cancellationToken).ConfigureAwait(false);
        if (probed is { Count: > 0 })
        {
            return await LoadModulesAsync(probed, source, baseUrlOverride, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new SleepyException(
            $"{input} is neither an OpenAPI document nor a Swagger UI configuration listing one");
    }

    /// <summary>Walks the well-known configuration endpoints around an input URL.</summary>
    private static async Task<List<ModuleRef>?> ProbeAsync(
        string input,
        SpecSource source,
        CancellationToken cancellationToken)
    {
        var bases = new List<string>();
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            // Directory of the input (".../swagger/" for ".../swagger/index.html"),
            // then the site root.
            bases.Add(new Uri(uri, ".").ToString());
            bases.Add(uri.GetLeftPart(UriPartial.Authority) + "/");
        }

        foreach (var baseUrl in bases.Distinct(StringComparer.Ordinal))
        {
            foreach (var candidate in ConfigCandidates)
            {
                var url = SpecSource.ResolveUrl(baseUrl, candidate);
                if (string.Equals(url, input, StringComparison.Ordinal))
                {
                    continue;
                }

                var fetched = await source.TryFetchAsync(url, cancellationToken).ConfigureAwait(false);
                if (fetched is null)
                {
                    continue;
                }

                var text = SpecParser.DecodeUtf8(fetched.Data);

                if (TryParseNode(text) is { } node)
                {
                    if (IsOpenApiDocument(node))
                    {
                        return [new ModuleRef(TitleOf(node) ?? "default", url)];
                    }

                    if (ReadModuleRefs(node, url) is { Count: > 0 } refs)
                    {
                        return refs;
                    }
                }

                if (ExtractUrlsFromScript(text, url) is { Count: > 0 } scriptRefs)
                {
                    return scriptRefs;
                }
            }
        }

        return null;
    }

    private static async Task<List<ApiModule>> LoadModulesAsync(
        List<ModuleRef> refs,
        SpecSource source,
        string? baseUrlOverride,
        CancellationToken cancellationToken)
    {
        var modules = new List<ApiModule>(refs.Count);
        foreach (var moduleRef in refs)
        {
            byte[] data;
            string baseUrl;
            if (SpecSource.IsUrl(moduleRef.Url))
            {
                var fetched = await source.FetchAsync(moduleRef.Url, cancellationToken).ConfigureAwait(false);
                data = fetched.Data;
                baseUrl = baseUrlOverride ?? SpecSource.Origin(moduleRef.Url);
            }
            else
            {
                if (!File.Exists(moduleRef.Url))
                {
                    throw new SleepyException($"could not read file: {moduleRef.Url}");
                }

                data = await File.ReadAllBytesAsync(moduleRef.Url, cancellationToken).ConfigureAwait(false);
                baseUrl = baseUrlOverride ?? string.Empty;
            }

            var spec = Parse(data, moduleRef.Url);
            var name = moduleRef.Name.Length > 0 ? moduleRef.Name : TitleOf(spec) ?? moduleRef.Url;
            modules.Add(new ApiModule(name, spec, baseUrl, moduleRef.Url));
        }

        return modules;
    }

    /// <summary>
    /// Reads a document list from parsed JSON: either <c>{"urls":[{url,name}]}</c>
    /// (Swashbuckle, springdoc) or <c>[{name,url|location}]</c> (springfox).
    /// </summary>
    private static List<ModuleRef>? ReadModuleRefs(JsonNode node, string documentUrl)
    {
        if (Json.AsArray(Json.Get(node, "urls")) is { } urls)
        {
            return ReadRefArray(urls, documentUrl);
        }

        if (Json.AsArray(node) is { } array)
        {
            return ReadRefArray(array, documentUrl);
        }

        return null;
    }

    private static List<ModuleRef>? ReadRefArray(JsonArray array, string documentUrl)
    {
        var refs = new List<ModuleRef>();
        foreach (var entry in array)
        {
            var url = Json.GetString(entry, "url") ?? Json.GetString(entry, "location");
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            var name = Json.GetString(entry, "name") ?? string.Empty;
            refs.Add(new ModuleRef(name, ResolveReference(documentUrl, url)));
        }

        return refs.Count > 0 ? refs : null;
    }

    /// <summary>
    /// Pulls the <c>"urls": [...]</c> array out of a JavaScript file such as
    /// Swashbuckle's <c>swagger-ui-init.js</c>, where the configuration is embedded
    /// as a JSON string literal.
    /// </summary>
    private static List<ModuleRef>? ExtractUrlsFromScript(string script, string documentUrl)
    {
        var marker = script.IndexOf("\"urls\"", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var open = script.IndexOf('[', marker);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < script.Length; i++)
        {
            if (script[i] == '[')
            {
                depth++;
            }
            else if (script[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    var json = script[open..(i + 1)].Replace("\\\"", "\"", StringComparison.Ordinal);
                    if (TryParseNode(json) is JsonArray array)
                    {
                        return ReadRefArray(array, documentUrl);
                    }

                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a document reference found inside <paramref name="documentUrl"/>:
    /// absolute URLs are kept, relative ones are resolved against the document's
    /// URL, or against its directory when the document is a local file.
    /// </summary>
    private static string ResolveReference(string documentUrl, string reference)
    {
        if (SpecSource.IsUrl(reference))
        {
            return reference;
        }

        if (SpecSource.IsUrl(documentUrl))
        {
            return SpecSource.ResolveUrl(documentUrl, reference);
        }

        var directory = Path.GetDirectoryName(documentUrl);
        return Path.GetFullPath(Path.Combine(
            string.IsNullOrEmpty(directory) ? "." : directory,
            reference));
    }

    private static bool IsOpenApiDocument(JsonNode node)
        => Json.Get(node, "openapi") is not null
        || Json.Get(node, "swagger") is not null
        || Json.Get(node, "paths") is not null;

    private static string? TitleOf(JsonNode? spec)
    {
        var title = Json.GetString(Json.Get(spec, "info"), "title");
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static ApiModule ToModule(JsonNode? spec, string sourceName, string baseUrl)
        => new(TitleOf(spec) ?? sourceName, spec, baseUrl, sourceName);

    private static JsonNode? Parse(byte[] data, string sourceName)
        => SpecParser.Parse(data, sourceName);

    private static JsonNode? TryParseNode(string text) => SpecParser.TryParse(text);
}
