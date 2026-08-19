using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class JsonWriterTests
{
    [Fact]
    public void SortsObjectKeysOrdinally()
    {
        var obj = new JsonObject
        {
            ["zeta"] = 1,
            ["Alpha"] = 2,
            ["beta"] = 3,
        };

        Assert.Equal("{\n  \"Alpha\": 2,\n  \"beta\": 3,\n  \"zeta\": 1\n}", JsonWriter.Write(obj));
    }

    [Fact]
    public void WritesEmptyContainersInline()
    {
        var obj = new JsonObject
        {
            ["array"] = new JsonArray(),
            ["object"] = new JsonObject(),
        };

        Assert.Equal("{\n  \"array\": [],\n  \"object\": {}\n}", JsonWriter.Write(obj));
    }

    [Fact]
    public void EscapesQuotesBackslashesAndControlCharacters()
    {
        var obj = new JsonObject { ["k"] = "a\"b\\c\nd\te\u0001" };

        Assert.Equal("{\n  \"k\": \"a\\\"b\\\\c\\nd\\te\\u0001\"\n}", JsonWriter.Write(obj));
    }

    [Fact]
    public void KeepsNumbersAndNullsAsParsed()
    {
        var node = JsonNode.Parse("""{"a":1,"b":1.5,"c":null,"d":true,"e":-0.25}""");

        Assert.Equal(
            "{\n  \"a\": 1,\n  \"b\": 1.5,\n  \"c\": null,\n  \"d\": true,\n  \"e\": -0.25\n}",
            JsonWriter.Write(node));
    }

    [Fact]
    public void IsIndependentOfInputKeyOrder()
    {
        var first = JsonWriter.Write(JsonNode.Parse("""{"b":1,"a":{"d":2,"c":[1,2]}}"""));
        var second = JsonWriter.Write(JsonNode.Parse("""{"a":{"c":[1,2],"d":2},"b":1}"""));

        Assert.Equal(first, second);
    }
}
