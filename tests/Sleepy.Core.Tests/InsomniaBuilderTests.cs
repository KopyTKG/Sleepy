using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class InsomniaBuilderTests
{
    private static ApiModule Module(string name, string spec, string baseUrl = "", string source = "spec.json")
        => new(name, JsonNode.Parse(spec), baseUrl, source);

    private static JsonNode Build(params ApiModule[] modules)
        => InsomniaBuilder.Build(modules, new BuildOptions { SourceName = "test" });

    private static JsonArray Resources(JsonNode document) => (JsonArray)document["resources"]!;

    private static List<JsonNode> OfType(JsonNode document, string type) => Resources(document)
        .Where(resource => resource?["_type"]?.GetValue<string>() == type)
        .Select(resource => resource!)
        .ToList();

    private const string AdminSpec = """
        {"openapi":"3.0.1","info":{"title":"Admin Module"},"paths":{
          "/users":{
            "get":{"tags":["Users"],"operationId":"listUsers","summary":"List them",
                   "parameters":[{"name":"q","in":"query","description":"search"},
                                 {"name":"id","in":"path"}]},
            "post":{"tags":["Users"],"operationId":"createUser","requestBody":{"content":
                    {"application/json":{"schema":{"type":"object","properties":{"n":{"type":"string"}}}}}}}},
          "/roles":{"get":{"tags":["Roles"],"operationId":"listRoles"}},
          "/loose":{"delete":{"operationId":"drop"}}}}
        """;

    private const string SharedSpec = """
        {"openapi":"3.0.1","info":{"title":"Shared Module"},"paths":{
          "/shared/users":{"get":{"tags":["Users"],"operationId":"listUsers"}}}}
        """;

    [Fact]
    public void StartsWithAWorkspaceAndAnEnvironment()
    {
        var document = Build(Module("Admin", AdminSpec, "https://api.example.com"));
        var resources = Resources(document);

        Assert.Equal("export", document["_type"]?.GetValue<string>());
        Assert.Equal(4, document["__export_format"]?.GetValue<int>());
        Assert.Equal("sleepy:test", document["__export_source"]?.GetValue<string>());
        Assert.Equal("workspace", resources[0]?["_type"]?.GetValue<string>());
        Assert.Equal("Admin Module", resources[0]?["name"]?.GetValue<string>());
        Assert.Equal("environment", resources[1]?["_type"]?.GetValue<string>());
        Assert.Equal("https://api.example.com", resources[1]?["data"]?["baseUrl"]?.GetValue<string>());
        Assert.Equal(string.Empty, resources[1]?["data"]?["bearer"]?.GetValue<string>());
    }

    [Fact]
    public void KeepsASingleModuleFlatAndUnprefixed()
    {
        var document = Build(Module("Admin", AdminSpec));

        Assert.Equal(
            ["Roles", "Users", "default"],
            OfType(document, "request_group").Select(folder => folder["name"]!.GetValue<string>()));
        Assert.Contains(
            OfType(document, "request"),
            request => request["_id"]!.GetValue<string>() == "req_listusers");
    }

    [Fact]
    public void BuildsOneRequestPerOperation()
    {
        var document = Build(Module("Admin", AdminSpec, "https://api.example.com"));
        var request = OfType(document, "request")
            .Single(candidate => candidate["_id"]!.GetValue<string>() == "req_listusers");

        Assert.Equal("GET /users", request["name"]?.GetValue<string>());
        Assert.Equal("GET", request["method"]?.GetValue<string>());
        Assert.Equal("{{ _.baseUrl }}/users", request["url"]?.GetValue<string>());
        Assert.Equal("List them", request["description"]?.GetValue<string>());
        Assert.Equal("bearer", request["authentication"]?["type"]?.GetValue<string>());
        Assert.Equal("{{ _.bearer }}", request["authentication"]?["token"]?.GetValue<string>());

        // Only query parameters travel; the path parameter is dropped.
        var parameters = (JsonArray)request["parameters"]!;
        Assert.Equal("q", Assert.Single(parameters)?["name"]?.GetValue<string>());
    }

    [Fact]
    public void AddsAJsonHeaderAndBodyOnlyForRequestBodies()
    {
        var document = Build(Module("Admin", AdminSpec));
        var requests = OfType(document, "request");

        var post = requests.Single(request => request["_id"]!.GetValue<string>() == "req_createuser");
        Assert.Equal("Content-Type", ((JsonArray)post["headers"]!)[0]?["name"]?.GetValue<string>());
        Assert.Equal("application/json", post["body"]?["mimeType"]?.GetValue<string>());
        Assert.Contains("\"n\"", post["body"]?["text"]?.GetValue<string>(), StringComparison.Ordinal);

        var get = requests.Single(request => request["_id"]!.GetValue<string>() == "req_listusers");
        Assert.Empty((JsonArray)get["headers"]!);
        Assert.Empty((JsonObject)get["body"]!);
    }

    [Fact]
    public void FallsBackToDefaultTagAndDerivedOperationId()
    {
        var document = Build(Module("Admin", AdminSpec));
        var request = OfType(document, "request")
            .Single(candidate => candidate["name"]!.GetValue<string>() == "DELETE /loose");

        var parent = request["parentId"]!.GetValue<string>();
        var folder = OfType(document, "request_group")
            .Single(candidate => candidate["_id"]!.GetValue<string>() == parent);

        Assert.Equal("default", folder["name"]?.GetValue<string>());
    }

    [Fact]
    public void NestsTagFoldersInsideModuleFolders()
    {
        var document = Build(Module("Admin", AdminSpec), Module("Shared", SharedSpec));
        var folders = OfType(document, "request_group");

        var admin = folders.Single(folder => folder["_id"]!.GetValue<string>() == "fld_admin");
        var adminUsers = folders.Single(folder => folder["_id"]!.GetValue<string>() == "fld_admin_users");

        Assert.Equal("wrk_sleepy", admin["parentId"]?.GetValue<string>());
        Assert.Equal("fld_admin", adminUsers["parentId"]?.GetValue<string>());
        Assert.Equal("Users", adminUsers["name"]?.GetValue<string>());
    }

    [Fact]
    public void PrefixesIdsSoModulesNeverCollide()
    {
        var document = Build(Module("Admin", AdminSpec), Module("Shared", SharedSpec));
        var ids = Resources(document).Select(resource => resource!["_id"]!.GetValue<string>()).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("req_admin_listusers", ids);
        Assert.Contains("req_shared_listusers", ids);
    }

    [Fact]
    public void FlatLayoutNamesFoldersAfterModuleAndTag()
    {
        var document = InsomniaBuilder.Build(
            [Module("Admin", AdminSpec), Module("Shared", SharedSpec)],
            new BuildOptions { SourceName = "test", Layout = FolderLayout.Flat });

        var names = OfType(document, "request_group")
            .Select(folder => folder["name"]!.GetValue<string>())
            .ToList();

        Assert.Contains("Admin / Users", names);
        Assert.Contains("Shared / Users", names);
        Assert.All(OfType(document, "request_group"),
            folder => Assert.Equal("wrk_sleepy", folder["parentId"]?.GetValue<string>()));
    }

    [Fact]
    public void ModuleOnlyLayoutSkipsTagFolders()
    {
        var document = InsomniaBuilder.Build(
            [Module("Admin", AdminSpec), Module("Shared", SharedSpec)],
            new BuildOptions { SourceName = "test", Layout = FolderLayout.ModuleOnly });

        Assert.Equal(
            ["Admin", "Shared"],
            OfType(document, "request_group").Select(folder => folder["name"]!.GetValue<string>()));
        Assert.All(OfType(document, "request"), request =>
            Assert.StartsWith("fld_", request["parentId"]!.GetValue<string>(), StringComparison.Ordinal));
    }

    [Fact]
    public void SharesOneBaseUrlWhenTheOriginsMatch()
    {
        var document = Build(
            Module("Admin", AdminSpec, "https://api.example.com"),
            Module("Shared", SharedSpec, "https://api.example.com"));

        var data = (JsonObject)OfType(document, "environment")[0]["data"]!;

        Assert.Equal(["baseUrl", "bearer"], data.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.All(OfType(document, "request"), request =>
            Assert.StartsWith("{{ _.baseUrl }}", request["url"]!.GetValue<string>(), StringComparison.Ordinal));
    }

    [Fact]
    public void GivesEachModuleItsOwnBaseUrlWhenOriginsDiffer()
    {
        var document = Build(
            Module("Admin", AdminSpec, "https://admin.example.com"),
            Module("Shared", SharedSpec, "https://shared.example.com"));

        var data = (JsonObject)OfType(document, "environment")[0]["data"]!;

        Assert.Equal("https://admin.example.com", data["baseUrl_admin"]?.GetValue<string>());
        Assert.Equal("https://shared.example.com", data["baseUrl_shared"]?.GetValue<string>());
        Assert.Contains(OfType(document, "request"), request =>
            request["url"]!.GetValue<string>().StartsWith("{{ _.baseUrl_shared }}", StringComparison.Ordinal));
    }

    [Fact]
    public void PrefillsTheBearerToken()
    {
        var document = InsomniaBuilder.Build(
            [Module("Admin", AdminSpec)],
            new BuildOptions { SourceName = "test", BearerToken = "secret" });

        Assert.Equal("secret", OfType(document, "environment")[0]["data"]?["bearer"]?.GetValue<string>());
    }

    [Fact]
    public void NamesTheWorkspaceAfterTheOptionThenTheTitleThenTheHost()
    {
        var named = InsomniaBuilder.Build(
            [Module("Admin", AdminSpec)],
            new BuildOptions { SourceName = "test", WorkspaceName = "Chosen" });
        Assert.Equal("Chosen", OfType(named, "workspace")[0]["name"]?.GetValue<string>());

        var single = Build(Module("Admin", AdminSpec));
        Assert.Equal("Admin Module", OfType(single, "workspace")[0]["name"]?.GetValue<string>());

        var several = Build(
            Module("Admin", AdminSpec, source: "https://api.example.com/swagger/Admin/swagger.json"),
            Module("Shared", SharedSpec, source: "https://api.example.com/swagger/Shared/swagger.json"));
        Assert.Equal("api.example.com", OfType(several, "workspace")[0]["name"]?.GetValue<string>());
    }

    [Fact]
    public void CountsOperationsForDisplay()
        => Assert.Equal(4, Operations.Count(Module("Admin", AdminSpec)));

    [Fact]
    public void SkipsModulesThatDeclareNoPaths()
    {
        var document = Build(
            Module("Admin", AdminSpec),
            Module("Placeholder", """{"openapi":"3.0.1","info":{"title":"Placeholder"}}"""));

        Assert.DoesNotContain(
            OfType(document, "request_group"),
            folder => folder["name"]!.GetValue<string>() == "Placeholder");
    }

    [Fact]
    public void HandlesADocumentWithoutPaths()
    {
        var document = Build(Module("Empty", """{"openapi":"3.0.1","info":{"title":"Empty"}}"""));

        Assert.Empty(OfType(document, "request"));
        Assert.Empty(OfType(document, "request_group"));
    }
}
