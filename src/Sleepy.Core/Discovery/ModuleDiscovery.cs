using System.Text.Json.Nodes;
using Sleepy.Serialization;

namespace Sleepy.Discovery;

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
public static class ModuleDiscovery
{
    /// <summary>Endpoints probed, relative to the input URL, when it is not itself a document list.</summary>
    private static readonly string[] ConfigCandidates =
    [
        "swagger/swagger-ui-init.js",
        "swagger-ui-init.js",
        "swagger/index.js",
        "index.js",
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

        // The URL itself may not be servable — a bare host often 404s while its
        // Swagger UI sits a level down — so a failure here is not yet fatal.
        SleepyException? fetchFailure = null;
        Fetched? fetched = null;
        try
        {
            fetched = await source.FetchAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (SleepyException exception) when (!NamesADocument(input))
        {
            // A URL that names a document is expected to serve it; only entry
            // points worth probing get a second chance below.
            fetchFailure = exception;
        }

        var text = fetched is null ? string.Empty : SpecParser.DecodeUtf8(fetched.Data);

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

        // Otherwise follow the page's own <script src="…"> tags: which file holds
        // the configuration varies between Swagger UI packagings (index.js,
        // swagger-ui-init.js, a bundled app script).
        var fromScripts = await ReadScriptsAsync(text, input, source, cancellationToken)
            .ConfigureAwait(false);
        if (fromScripts is { Count: > 0 })
        {
            return await LoadModulesAsync(fromScripts, source, baseUrlOverride, cancellationToken)
                .ConfigureAwait(false);
        }

        var probed = await ProbeAsync(input, source, cancellationToken).ConfigureAwait(false);
        if (probed is { Count: > 0 })
        {
            return await LoadModulesAsync(probed, source, baseUrlOverride, cancellationToken)
                .ConfigureAwait(false);
        }

        throw fetchFailure ?? new SleepyException(
            $"{input} is neither an OpenAPI document nor a Swagger UI configuration listing one");
    }

    /// <summary>Whether a URL points at a specific document rather than at an entry point.</summary>
    private static bool NamesADocument(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches every script an HTML page references and returns the first module
    /// list found in one of them.
    /// </summary>
    private static async Task<List<ModuleRef>?> ReadScriptsAsync(
        string html,
        string documentUrl,
        SpecSource source,
        CancellationToken cancellationToken)
    {
        foreach (var scriptSource in ScriptSources(html))
        {
            var url = SpecSource.ResolveUrl(documentUrl, scriptSource);
            var fetched = await source.TryFetchAsync(url, cancellationToken).ConfigureAwait(false);
            if (fetched is null)
            {
                continue;
            }

            var refs = ExtractUrlsFromScript(SpecParser.DecodeUtf8(fetched.Data), url);
            if (refs is { Count: > 0 })
            {
                return refs;
            }
        }

        return null;
    }

    /// <summary>Reads the <c>src</c> of every script tag in a page, in document order.</summary>
    private static IEnumerable<string> ScriptSources(string html)
    {
        var index = 0;
        while (true)
        {
            var tag = html.IndexOf("<script", index, StringComparison.OrdinalIgnoreCase);
            if (tag < 0)
            {
                yield break;
            }

            var end = html.IndexOf('>', tag);
            if (end < 0)
            {
                yield break;
            }

            var attributes = html[tag..end];
            index = end + 1;

            var source = AttributeValue(attributes, "src");
            if (!string.IsNullOrEmpty(source))
            {
                yield return source;
            }
        }
    }

    private static string? AttributeValue(string tag, string name)
    {
        var at = tag.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var rest = tag[(at + name.Length + 1)..].TrimStart();
        if (rest.Length == 0)
        {
            return null;
        }

        var quote = rest[0];
        if (quote is not ('"' or '\''))
        {
            var space = rest.IndexOfAny([' ', '\t', '\r', '\n']);
            return space < 0 ? rest : rest[..space];
        }

        var close = rest.IndexOf(quote, 1);
        return close < 0 ? null : rest[1..close];
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
