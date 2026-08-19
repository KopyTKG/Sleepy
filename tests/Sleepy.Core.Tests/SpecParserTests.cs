using System.Text;
using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class SpecParserTests
{
    [Fact]
    public void ParsesJsonWithCommentsAndTrailingCommas()
    {
        var node = SpecParser.Parse("""{ /* note */ "a": 1, }""", "spec.json");

        Assert.Equal(1, node?["a"]?.GetValue<int>());
    }

    [Fact]
    public void ParsesYamlIntoTheSameShapeAsJson()
    {
        const string yaml = """
            openapi: "3.0.1"
            info:
              title: Twin
            paths:
              /items:
                get:
                  tags: [Items]
            """;
        const string json =
            """{"openapi":"3.0.1","info":{"title":"Twin"},"paths":{"/items":{"get":{"tags":["Items"]}}}}""";

        Assert.Equal(
            JsonWriter.Write(SpecParser.Parse(json, "spec.json")),
            JsonWriter.Write(SpecParser.Parse(yaml, "spec.yaml")));
    }

    [Theory]
    [InlineData("value: 3", "3")]
    [InlineData("value: 3.5", "3.5")]
    [InlineData("value: true", "true")]
    [InlineData("value: ~", "null")]
    [InlineData("value: plain text", "\"plain text\"")]
    [InlineData("value: \"3\"", "\"3\"")]
    public void GivesYamlScalarsTheirImplicitType(string yaml, string expectedJson)
    {
        var node = SpecParser.Parse(yaml, "spec.yaml");

        Assert.Equal(expectedJson, node?["value"]?.ToJsonString() ?? "null");
    }

    [Fact]
    public void FallsBackToYamlWhenJsonFails()
    {
        var node = SpecParser.Parse("a: 1", "spec.txt");

        // YAML integers land as Int64, JSON ones keep their parsed representation.
        Assert.Equal(1L, node?["a"]?.GetValue<long>());
    }

    [Fact]
    public void NamesTheSourceWhenNeitherSyntaxParses()
    {
        var error = Assert.Throws<SleepyException>(() => SpecParser.Parse("{ \"unclosed\": ", "spec.json"));

        Assert.Contains("spec.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StripsTheUtf8ByteOrderMark()
    {
        var data = Encoding.UTF8.GetPreamble().Concat("{\"a\":1}"u8.ToArray()).ToArray();

        Assert.Equal(1, SpecParser.Parse(data, "spec.json")?["a"]?.GetValue<int>());
    }

    [Fact]
    public void TryParseRejectsThingsThatAreNotDocuments()
    {
        Assert.Null(SpecParser.TryParse("window.onload = function () {"));
        Assert.IsType<JsonObject>(SpecParser.TryParse("{\"a\":1}"));
    }
}
