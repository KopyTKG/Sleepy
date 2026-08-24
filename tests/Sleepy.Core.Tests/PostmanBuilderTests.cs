using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class PostmanBuilderTests
{
    private const string AdminSpec = """
        {"openapi":"3.0.1","info":{"title":"Admin"},"paths":{
          "/users/{id}":{"get":{"tags":["Users"],"operationId":"getUser","summary":"Get one",
            "parameters":[{"name":"expand","in":"query","description":"relations"},
                          {"name":"id","in":"path"}]}},
          "/users":{"post":{"tags":["Users"],"operationId":"createUser","requestBody":{"content":
            {"application/json":{"schema":{"$ref":"#/components/schemas/User"}}}}}},
          "/roles":{"get":{"tags":["Roles"],"operationId":"listRoles"}}},
          "components":{"schemas":{"User":{"type":"object","properties":{"n":{"type":"string"}}}}}}
        """;

    private const string SharedSpec = """
        {"openapi":"3.0.1","info":{"title":"Shared"},"paths":{
          "/ping":{"get":{"tags":["Health"],"operationId":"ping"}}}}
        """;

    private static ApiModule Module(string name, string spec, string baseUrl = "https://api.example.com")
        => new(name, JsonNode.Parse(spec), baseUrl, "https://api.example.com/swagger/index.html");

    private static JsonNode Build(BuildOptions? options = null, params ApiModule[] modules)
        => PostmanV21Builder.BuildDocument(
            modules.Length == 0 ? [Module("Admin", AdminSpec)] : modules,
            options ?? new BuildOptions { SourceName = "test" });

    private static IEnumerable<JsonNode> Requests(JsonNode node)
    {
        if (node["request"] is not null)
        {
            yield return node;
            yield break;
        }

        foreach (var child in (JsonArray)(node["item"] ?? new JsonArray()))
        {
            foreach (var request in Requests(child!))
            {
                yield return request;
            }
        }
    }

    private static List<JsonNode> AllRequests(JsonNode document)
        => ((JsonArray)document["item"]!).SelectMany(item => Requests(item!)).ToList();

    [Fact]
    public void DeclaresTheV21SchemaAndCollectionAuth()
    {
        var document = Build();

        Assert.Contains("v2.1.0", document["info"]?["schema"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Admin", document["info"]?["name"]?.GetValue<string>());
        Assert.Equal("bearer", document["auth"]?["type"]?.GetValue<string>());
        Assert.Equal("{{bearer}}", document["auth"]?["bearer"]?[0]?["value"]?.GetValue<string>());
    }

    [Fact]
    public void GivesTheSameCollectionTheSameId()
    {
        var first = Build()["info"]?["_postman_id"]?.GetValue<string>();
        var second = Build()["info"]?["_postman_id"]?.GetValue<string>();

        Assert.Equal(first, second);
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public void PublishesTheVariablesAsCollectionVariables()
    {
        var variables = (JsonArray)Build()["variable"]!;
        var byKey = variables.ToDictionary(
            entry => entry!["key"]!.GetValue<string>(),
            entry => entry!["value"]!.GetValue<string>(),
            StringComparer.Ordinal);

        Assert.Equal("https://api.example.com", byKey["baseUrl"]);
        Assert.Equal(string.Empty, byKey["bearer"]);
    }

    [Fact]
    public void WritesUrlsAsObjectsWithPostmanVariables()
    {
        var request = AllRequests(Build())
            .Single(item => item["name"]!.GetValue<string>() == "GET /users/{id}")["request"]!;

        var url = request["url"]!;
        Assert.Equal("{{baseUrl}}/users/:id", url["raw"]?.GetValue<string>());
        Assert.Equal("{{baseUrl}}", url["host"]?[0]?.GetValue<string>());
        Assert.Equal(["users", ":id"], ((JsonArray)url["path"]!).Select(part => part!.GetValue<string>()));
        Assert.Equal("id", url["variable"]?[0]?["key"]?.GetValue<string>());
        Assert.Equal("expand", url["query"]?[0]?["key"]?.GetValue<string>());
    }

    [Fact]
    public void WritesJsonBodiesAsRawWithAResolvedExample()
    {
        var request = AllRequests(Build())
            .Single(item => item["name"]!.GetValue<string>() == "POST /users")["request"]!;

        Assert.Equal("raw", request["body"]?["mode"]?.GetValue<string>());
        Assert.Equal("json", request["body"]?["options"]?["raw"]?["language"]?.GetValue<string>());
        Assert.Contains("\"n\"", request["body"]?["raw"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Content-Type", request["header"]?[0]?["key"]?.GetValue<string>());
    }

    [Fact]
    public void OmitsBodyAndHeaderForOperationsWithoutARequestBody()
    {
        var request = AllRequests(Build())
            .Single(item => item["name"]!.GetValue<string>() == "GET /roles")["request"]!;

        Assert.Null(request["body"]);
        Assert.Empty((JsonArray)request["header"]!);
    }

    [Fact]
    public void NestsTagFoldersInsideModuleFoldersForSeveralModules()
    {
        var document = Build(null, Module("Admin", AdminSpec), Module("Shared", SharedSpec));
        var top = (JsonArray)document["item"]!;

        Assert.Equal(
            ["Admin", "Shared"],
            top.Select(item => item!["name"]!.GetValue<string>()));
        Assert.Equal(
            ["Roles", "Users"],
            ((JsonArray)top[0]!["item"]!).Select(item => item!["name"]!.GetValue<string>()));
        Assert.Equal(4, AllRequests(document).Count);
    }

    [Fact]
    public void FlatLayoutNamesFoldersAfterModuleAndTag()
    {
        var document = Build(
            new BuildOptions { SourceName = "test", Layout = FolderLayout.Flat },
            Module("Admin", AdminSpec),
            Module("Shared", SharedSpec));

        var names = ((JsonArray)document["item"]!).Select(item => item!["name"]!.GetValue<string>()).ToList();

        Assert.Contains("Admin / Users", names);
        Assert.Contains("Shared / Health", names);
    }

    [Fact]
    public void GivesEachModuleItsOwnVariableWhenOriginsDiffer()
    {
        var document = Build(
            null,
            Module("Admin", AdminSpec, "https://admin.example.com"),
            Module("Shared", SharedSpec, "https://shared.example.com"));

        var keys = ((JsonArray)document["variable"]!)
            .Select(entry => entry!["key"]!.GetValue<string>())
            .ToList();

        Assert.Contains("baseUrl_admin", keys);
        Assert.Contains("baseUrl_shared", keys);
        Assert.Contains(
            AllRequests(document),
            item => item["request"]!["url"]!["raw"]!.GetValue<string>().StartsWith(
                "{{baseUrl_shared}}", StringComparison.Ordinal));
    }

    [Fact]
    public void ProducesIdenticalBytesForTheSameInput()
    {
        var builder = new PostmanV21Builder();
        var options = new BuildOptions { SourceName = "test" };

        Assert.Equal(
            builder.Build([Module("Admin", AdminSpec)], options).Content,
            builder.Build([Module("Admin", AdminSpec)], options).Content);
    }
}
