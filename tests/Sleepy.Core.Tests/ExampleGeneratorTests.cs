using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class ExampleGeneratorTests
{
    private static JsonNode? Generate(string schemaJson, string? rootJson = null)
    {
        var root = JsonNode.Parse(rootJson ?? "{}");
        return ExampleGenerator.Generate(JsonNode.Parse(schemaJson), root);
    }

    [Theory]
    [InlineData("""{"type":"string"}""", "\"\"")]
    [InlineData("""{"type":"string","format":"date-time"}""", "\"2020-01-01T00:00:00Z\"")]
    [InlineData("""{"type":"string","format":"date"}""", "\"2020-01-01\"")]
    [InlineData("""{"type":"string","format":"uuid"}""", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("""{"type":"integer"}""", "0")]
    [InlineData("""{"type":"number"}""", "0")]
    [InlineData("""{"type":"boolean"}""", "false")]
    public void MapsScalarTypesToPlaceholders(string schema, string expected)
        => Assert.Equal(expected, Generate(schema)?.ToJsonString() ?? "null");

    [Fact]
    public void UsesTheFirstEnumValue()
        => Assert.Equal("\"alpha\"", Generate("""{"type":"string","enum":["alpha","beta"]}""")?.ToJsonString());

    [Fact]
    public void PrefersAnExplicitExample()
        => Assert.Equal("""{"a":1}""", Generate("""{"type":"object","example":{"a":1}}""")?.ToJsonString());

    [Fact]
    public void WrapsArrayItems()
        => Assert.Equal("""[""]""", Generate("""{"type":"array","items":{"type":"string"}}""")?.ToJsonString());

    [Fact]
    public void MergesAllOfObjects()
    {
        const string schema = """
            {"allOf":[
              {"type":"object","properties":{"a":{"type":"string"}}},
              {"type":"object","properties":{"b":{"type":"integer"}}}
            ]}
            """;

        Assert.Equal("{\n  \"a\": \"\",\n  \"b\": 0\n}", JsonWriter.Write(Generate(schema)));
    }

    [Theory]
    [InlineData("oneOf")]
    [InlineData("anyOf")]
    public void TakesTheFirstAlternative(string keyword)
    {
        var schema = $$"""{"{{keyword}}":[{"type":"integer"},{"type":"string"}]}""";

        Assert.Equal("0", Generate(schema)?.ToJsonString());
    }

    [Fact]
    public void ResolvesLocalReferences()
    {
        const string root = """
            {"components":{"schemas":{"User":{"type":"object","properties":{"id":{"type":"integer"}}}}}}
            """;

        Assert.Equal(
            "{\n  \"id\": 0\n}",
            JsonWriter.Write(Generate("""{"$ref":"#/components/schemas/User"}""", root)));
    }

    [Fact]
    public void StopsAtReferenceCycles()
    {
        const string root = """
            {"components":{"schemas":{"Node":{"type":"object","properties":{
              "child":{"$ref":"#/components/schemas/Node"},
              "name":{"type":"string"}}}}}}
            """;

        var example = Generate("""{"$ref":"#/components/schemas/Node"}""", root);

        Assert.Equal("{\n  \"child\": null,\n  \"name\": \"\"\n}", JsonWriter.Write(example));
    }

    [Fact]
    public void ReturnsNullForUnresolvableReferences()
        => Assert.Null(Generate("""{"$ref":"#/components/schemas/Missing"}"""));

    [Fact]
    public void StopsAtTheDepthCap()
    {
        // Seven nested arrays: deeper than the depth cap, so the innermost item drops out.
        var schema = string.Concat(Enumerable.Repeat("""{"type":"array","items":""", 7))
            + """{"type":"string"}"""
            + new string('}', 7);

        Assert.Equal("[[[[[[[null]]]]]]]", Generate(schema)?.ToJsonString());
    }

    [Fact]
    public void TreatsSchemasWithoutATypeAsObjects()
        => Assert.Equal("""{"a":""}""", Generate("""{"properties":{"a":{"type":"string"}}}""")?.ToJsonString());
}
