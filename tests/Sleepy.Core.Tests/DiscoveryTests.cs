namespace Sleepy.Tests;

public class DiscoveryTests : IDisposable
{
    private const string AdminSpec =
        """{"openapi":"3.0.1","info":{"title":"Admin Module"},"paths":{"/a":{"get":{"operationId":"a"}}}}""";

    private const string WorkflowSpec =
        """{"openapi":"3.0.1","info":{"title":"Workflow Module"},"paths":{"/w":{"get":{"operationId":"w"}}}}""";

    private static readonly string InitScript = """
        window.onload = function () {
          var configObject = JSON.parse('{"urls":[{"url":"/swagger/Admin/swagger.json","name":"Admin"},{"url":"/swagger/Workflow/swagger.json","name":"Workflow"}],"deepLinking":false}');
          var ui = SwaggerUIBundle(configObject);
        };
        """;

    private readonly List<string> _temporaryFiles = [];

    private static Dictionary<string, (string Body, string ContentType)> SwashbuckleRoutes() => new()
    {
        ["/swagger/index.html"] = ("<html><body>swagger</body></html>", "text/html"),
        ["/"] = ("<html><body>root</body></html>", "text/html"),
        ["/swagger/swagger-ui-init.js"] = (InitScript, "text/javascript"),
        ["/swagger/Admin/swagger.json"] = (AdminSpec, "application/json"),
        ["/swagger/Workflow/swagger.json"] = (WorkflowSpec, "application/json"),
    };

    private static async Task<List<ApiModule>> ExpandAsync(string input, string? baseUrlOverride = null)
    {
        using var source = new SpecSource([], allowInvalidCertificates: false);
        return await ModuleDiscovery.ExpandAsync(input, source, baseUrlOverride, CancellationToken.None);
    }

    private string WriteTemporaryFile(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sleepy-tests-{Guid.NewGuid():N}", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        _temporaryFiles.Add(Path.GetDirectoryName(path)!);
        return path;
    }

    [Fact]
    public async Task ReadsASingleLocalSpec()
    {
        var path = WriteTemporaryFile("openapi.json", AdminSpec);

        var module = Assert.Single(await ExpandAsync(path));

        Assert.Equal("Admin Module", module.Name);
        Assert.Equal(string.Empty, module.BaseUrl);
    }

    [Fact]
    public async Task ReadsALocalYamlSpec()
    {
        var path = WriteTemporaryFile("openapi.yaml", """
            openapi: "3.0.1"
            info:
              title: Yaml Module
            paths:
              /a:
                get:
                  operationId: a
            """);

        Assert.Equal("Yaml Module", Assert.Single(await ExpandAsync(path)).Name);
    }

    [Fact]
    public async Task ReportsMissingFiles()
    {
        var error = await Assert.ThrowsAsync<SleepyException>(() => ExpandAsync("/no/such/spec.json"));

        Assert.Contains("could not read file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpandsAManifestOfLocalPaths()
    {
        var admin = WriteTemporaryFile("admin.json", AdminSpec);
        var directory = Path.GetDirectoryName(admin)!;
        File.WriteAllText(Path.Combine(directory, "workflow.json"), WorkflowSpec);
        var manifest = Path.Combine(directory, "manifest.json");
        File.WriteAllText(
            manifest,
            """{"urls":[{"name":"Admin","url":"admin.json"},{"name":"Workflow","url":"workflow.json"}]}""");

        var modules = await ExpandAsync(manifest);

        Assert.Equal(["Admin", "Workflow"], modules.Select(module => module.Name));
    }

    [Fact]
    public async Task FetchesASpecUrlAndPrefillsTheOrigin()
    {
        using var server = new SwaggerServer(SwashbuckleRoutes());

        var module = Assert.Single(await ExpandAsync($"{server.Prefix}/swagger/Admin/swagger.json"));

        Assert.Equal("Admin Module", module.Name);
        Assert.Equal(server.Prefix, module.BaseUrl);
    }

    [Fact]
    public async Task HonoursTheBaseUrlOverride()
    {
        using var server = new SwaggerServer(SwashbuckleRoutes());

        var module = Assert.Single(
            await ExpandAsync($"{server.Prefix}/swagger/Admin/swagger.json", "https://api.example.com"));

        Assert.Equal("https://api.example.com", module.BaseUrl);
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/index.html?urls.primaryName=Admin")]
    [InlineData("/swagger/swagger-ui-init.js")]
    [InlineData("/")]
    public async Task DiscoversEveryModuleFromASwaggerUi(string path)
    {
        using var server = new SwaggerServer(SwashbuckleRoutes());

        var modules = await ExpandAsync(server.Prefix + path);

        Assert.Equal(["Admin", "Workflow"], modules.Select(module => module.Name));
        Assert.All(modules, module => Assert.Equal(server.Prefix, module.BaseUrl));
    }

    [Fact]
    public async Task ReadsASpringdocSwaggerConfig()
    {
        using var server = new SwaggerServer(new Dictionary<string, (string, string)>
        {
            ["/v3/api-docs/swagger-config"] =
                ("""{"urls":[{"url":"/v3/api-docs/Admin","name":"Admin"}]}""", "application/json"),
            ["/v3/api-docs/Admin"] = (AdminSpec, "application/json"),
        });

        var modules = await ExpandAsync($"{server.Prefix}/v3/api-docs/swagger-config");

        Assert.Equal("Admin", Assert.Single(modules).Name);
    }

    [Fact]
    public async Task ReadsASpringfoxResourceList()
    {
        using var server = new SwaggerServer(new Dictionary<string, (string, string)>
        {
            ["/swagger-resources"] =
                ("""[{"name":"Admin","location":"/v2/api-docs?group=Admin"}]""", "application/json"),
            ["/v2/api-docs"] = (AdminSpec, "application/json"),
        });

        var modules = await ExpandAsync($"{server.Prefix}/swagger-resources");

        Assert.Equal("Admin", Assert.Single(modules).Name);
    }

    [Fact]
    public async Task FollowsTheScriptTagsOfAPageToFindTheModuleList()
    {
        // Swagger UI's static bundle keeps the configuration in index.js rather
        // than in swagger-ui-init.js.
        using var server = new SwaggerServer(new Dictionary<string, (string, string)>
        {
            ["/swagger/index.html"] = (
                """<html><body><script src="./swagger-ui-bundle.js"></script><script src="index.js"></script></body></html>""",
                "text/html"),
            ["/swagger/swagger-ui-bundle.js"] = ("/* no configuration here */", "text/javascript"),
            ["/swagger/index.js"] = (InitScript, "text/javascript"),
            ["/swagger/Admin/swagger.json"] = (AdminSpec, "application/json"),
            ["/swagger/Workflow/swagger.json"] = (WorkflowSpec, "application/json"),
        });

        var modules = await ExpandAsync($"{server.Prefix}/swagger/index.html");

        Assert.Equal(["Admin", "Workflow"], modules.Select(module => module.Name));
    }

    [Fact]
    public async Task ProbesOnWhenTheEntryPointItselfDoesNotServeAnything()
    {
        // A bare host commonly 404s while its Swagger UI sits a level down.
        using var server = new SwaggerServer(new Dictionary<string, (string, string)>
        {
            ["/swagger/swagger-ui-init.js"] = (InitScript, "text/javascript"),
            ["/swagger/Admin/swagger.json"] = (AdminSpec, "application/json"),
            ["/swagger/Workflow/swagger.json"] = (WorkflowSpec, "application/json"),
        });

        var modules = await ExpandAsync(server.Prefix);

        Assert.Equal(["Admin", "Workflow"], modules.Select(module => module.Name));
    }

    [Fact]
    public async Task KeepsAMissingDocumentFatalInsteadOfProbing()
    {
        using var server = new SwaggerServer(SwashbuckleRoutes());

        var error = await Assert.ThrowsAsync<SleepyException>(
            () => ExpandAsync($"{server.Prefix}/openapi.json"));

        Assert.Contains("404", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAPageThatListsNothing()
    {
        using var server = new SwaggerServer(new Dictionary<string, (string, string)>
        {
            ["/swagger/index.html"] = ("<html>nothing here</html>", "text/html"),
        });

        var error = await Assert.ThrowsAsync<SleepyException>(
            () => ExpandAsync($"{server.Prefix}/swagger/index.html"));

        Assert.Contains("neither an OpenAPI document", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFailedFetch()
    {
        using var server = new SwaggerServer(SwashbuckleRoutes());

        await Assert.ThrowsAsync<SleepyException>(
            () => ExpandAsync($"{server.Prefix}/swagger/Missing/swagger.json"));
    }

    public void Dispose()
    {
        foreach (var directory in _temporaryFiles.Distinct(StringComparer.Ordinal))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
