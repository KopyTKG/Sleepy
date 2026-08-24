using Sleepy.Serialization;
namespace Sleepy.OpenApi;

/// <summary>
/// The decisions every output format shares: what the collection is called,
/// which modules contribute anything, and which variable each module's base URL
/// hides behind. Keeping them here is what makes an Insomnia export, a Postman
/// collection and a Bruno folder describe the same API rather than three
/// slightly different ones.
/// </summary>
public static class CollectionPlan
{
    public const string BaseUrlVariable = "baseUrl";

    public const string BearerVariable = "bearer";

    private const string DefaultName = "OpenAPI Import";

    /// <summary>
    /// The collection name: what the user asked for, else the title of the only
    /// module, else the host the modules were fetched from.
    /// </summary>
    public static string ResolveName(IReadOnlyList<ApiModule> modules, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        if (modules.Count == 1
            && Json.GetString(Json.Get(modules[0].Spec, "info"), "title") is { Length: > 0 } title)
        {
            return title;
        }

        if (modules.Count > 1
            && Uri.TryCreate(modules[0].SourceName, UriKind.Absolute, out var uri))
        {
            // Several modules have several titles; the host they came from is the
            // most useful shared label.
            return uri.Host;
        }

        return DefaultName;
    }

    /// <summary>
    /// The variable holding each module's base URL, by module index. Modules that
    /// share an origin share one <c>baseUrl</c>; when the origins differ, each
    /// module gets its own <c>baseUrl_&lt;module&gt;</c>.
    /// </summary>
    public static string[] ResolveBaseUrlVariables(IReadOnlyList<ApiModule> modules)
    {
        var shared = modules
            .Select(module => module.BaseUrl)
            .Where(baseUrl => baseUrl.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count() <= 1;

        return modules
            .Select(module => shared ? BaseUrlVariable : $"{BaseUrlVariable}_{Slug.Make(module.Name)}")
            .ToArray();
    }

    /// <summary>
    /// Whether a module should be written at all. A module that declares no paths
    /// would only contribute an empty folder — several real APIs ship placeholder
    /// modules like that — but a lone module is written even when empty, so that
    /// converting one specification always produces a collection.
    /// </summary>
    public static bool Contributes(ApiModule module, int moduleCount)
        => moduleCount == 1 || Operations.Collect(module.Spec).Count > 0;

    /// <summary>The environment variables a collection needs, in ordinal order.</summary>
    public static SortedDictionary<string, string> ResolveVariables(
        IReadOnlyList<ApiModule> modules,
        string[] baseUrlVariables,
        string bearerToken)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < modules.Count; i++)
        {
            variables[baseUrlVariables[i]] = modules[i].BaseUrl;
        }

        if (variables.Count == 0)
        {
            variables[BaseUrlVariable] = string.Empty;
        }

        variables[BearerVariable] = bearerToken;
        return variables;
    }
}
