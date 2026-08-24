using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class BrunoBuilderTests
{
    private const string AdminSpec = """
        {"openapi":"3.0.1","info":{"title":"Admin"},"paths":{
          "/users/{id}":{"get":{"tags":["Users"],"operationId":"getUser","summary":"Get one user",
            "parameters":[{"name":"expand","in":"query"},{"name":"id","in":"path"}]}},
          "/users":{"post":{"tags":["Users"],"operationId":"createUser","requestBody":{"content":
            {"application/json":{"schema":{"type":"object","properties":{"n":{"type":"string"}}}}}}}},
          "/roles":{"get":{"tags":["Roles"],"operationId":"listRoles"}}}}
        """;

    private const string SharedSpec = """
        {"openapi":"3.0.1","info":{"title":"Shared"},"paths":{
          "/ping":{"get":{"tags":["Health"],"operationId":"ping"}}}}
        """;

    private static ApiModule Module(string name, string spec, string baseUrl = "https://api.example.com")
        => new(name, JsonNode.Parse(spec), baseUrl, "https://api.example.com/swagger/index.html");

    private static BuiltCollection Build(BuildOptions? options = null, params ApiModule[] modules)
        => new BrunoBuilder().Build(
            modules.Length == 0 ? [Module("Admin", AdminSpec)] : modules,
            options ?? new BuildOptions { SourceName = "test" });

    private static string File(BuiltCollection collection, string path)
        => collection.Files.Single(file => file.RelativePath == path).Content;

    [Fact]
    public void IsAFolderFormat()
    {
        var builder = new BrunoBuilder();

        Assert.True(builder.IsFolderFormat);
        Assert.False(Build().IsSingleFile);
    }

    [Fact]
    public void WritesTheCollectionMarkerAndEnvironment()
    {
        var collection = Build();

        var marker = JsonNode.Parse(File(collection, "bruno.json"))!;
        Assert.Equal("collection", marker["type"]?.GetValue<string>());
        Assert.Equal("Admin", marker["name"]?.GetValue<string>());

        var environment = File(collection, "environments/Base Environment.bru");
        Assert.Contains("baseUrl: https://api.example.com", environment, StringComparison.Ordinal);
        Assert.Contains("bearer: ", environment, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsEachRequestInItsTagFolder()
    {
        var paths = Build().Files.Select(file => file.RelativePath).ToList();

        Assert.Contains("Users/getuser.bru", paths);
        Assert.Contains("Users/createuser.bru", paths);
        Assert.Contains("Roles/listroles.bru", paths);
        Assert.Contains("Users/folder.bru", paths);
    }

    [Fact]
    public void WritesTheRequestBlocksBrunoExpects()
    {
        var request = File(Build(), "Users/getuser.bru");

        Assert.Contains("meta {", request, StringComparison.Ordinal);
        Assert.Contains("name: GET /users/{id}", request, StringComparison.Ordinal);
        Assert.Contains("type: http", request, StringComparison.Ordinal);
        Assert.Contains("get {", request, StringComparison.Ordinal);
        Assert.Contains("url: {{baseUrl}}/users/{id}", request, StringComparison.Ordinal);
        Assert.Contains("auth: bearer", request, StringComparison.Ordinal);
        Assert.Contains("params:query {", request, StringComparison.Ordinal);
        Assert.Contains("expand:", request, StringComparison.Ordinal);
        Assert.Contains("token: {{bearer}}", request, StringComparison.Ordinal);
        Assert.Contains("docs {", request, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesJsonBodiesWithAContentTypeHeader()
    {
        var request = File(Build(), "Users/createuser.bru");

        Assert.Contains("body: json", request, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/json", request, StringComparison.Ordinal);
        Assert.Contains("body:json {", request, StringComparison.Ordinal);
        Assert.Contains("\"n\": \"\"", request, StringComparison.Ordinal);
    }

    [Fact]
    public void MarksRequestsWithoutABodyAsNone()
    {
        var request = File(Build(), "Roles/listroles.bru");

        Assert.Contains("body: none", request, StringComparison.Ordinal);
        Assert.DoesNotContain("body:json", request, StringComparison.Ordinal);
    }

    [Fact]
    public void NestsModuleDirectoriesForSeveralModules()
    {
        var paths = Build(null, Module("Admin", AdminSpec), Module("Shared", SharedSpec))
            .Files.Select(file => file.RelativePath).ToList();

        Assert.Contains("Admin/Users/getuser.bru", paths);
        Assert.Contains("Shared/Health/ping.bru", paths);
        Assert.Contains("Admin/Users/folder.bru", paths);
    }

    [Fact]
    public void KeepsFlatLayoutNamesUsableAsDirectories()
    {
        var paths = Build(
                new BuildOptions { SourceName = "test", Layout = FolderLayout.Flat },
                Module("Admin", AdminSpec),
                Module("Shared", SharedSpec))
            .Files.Select(file => file.RelativePath).ToList();

        // "Admin / Users" would be two directories; the separator has to go.
        Assert.Contains("Admin - Users/getuser.bru", paths);
        Assert.DoesNotContain(paths, path => path.Contains(" / ", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleOnlyLayoutSkipsTagDirectories()
    {
        var paths = Build(
                new BuildOptions { SourceName = "test", Layout = FolderLayout.ModuleOnly },
                Module("Admin", AdminSpec),
                Module("Shared", SharedSpec))
            .Files.Select(file => file.RelativePath).ToList();

        Assert.Contains("Admin/getuser.bru", paths);
        Assert.Contains("Shared/ping.bru", paths);
    }

    [Fact]
    public void NumbersRequestsSoBrunoOrdersThem()
    {
        var collection = Build();

        Assert.Contains("seq: 1", File(collection, "Users/createuser.bru"), StringComparison.Ordinal);
        Assert.Contains("seq: 2", File(collection, "Users/getuser.bru"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPathIsDistinctAndRelative()
    {
        var files = Build(null, Module("Admin", AdminSpec), Module("Shared", SharedSpec)).Files;

        Assert.Equal(files.Count, files.Select(file => file.RelativePath).Distinct(StringComparer.Ordinal).Count());
        Assert.All(files, file =>
        {
            Assert.False(Path.IsPathRooted(file.RelativePath));
            Assert.DoesNotContain("..", file.RelativePath, StringComparison.Ordinal);
        });
    }
}
