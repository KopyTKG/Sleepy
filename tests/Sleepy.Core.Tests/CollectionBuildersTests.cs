using System.Text.Json.Nodes;
using Sleepy.Builders;

namespace Sleepy.Tests;

public class CollectionBuildersTests
{
    private const string Spec = """
        {"openapi":"3.0.1","info":{"title":"Admin"},"paths":{
          "/users":{"get":{"tags":["Users"],"operationId":"listUsers"}}}}
        """;

    private static ApiModule Module() => new("Admin", JsonNode.Parse(Spec), string.Empty, "spec.json");

    [Fact]
    public void RegistersInsomniaAsTheDefaultFormat()
    {
        Assert.Equal("insomnia", CollectionBuilders.Default.FormatId);
        Assert.Equal(["insomnia", "postman", "bruno"], CollectionBuilders.Ids);
    }

    [Theory]
    [InlineData("insomnia")]
    [InlineData("Insomnia")]
    [InlineData("INSOMNIA")]
    public void ResolvesFormatIdsCaseInsensitively(string id)
        => Assert.Equal("insomnia", CollectionBuilders.Resolve(id).FormatId);

    [Fact]
    public void NamesTheKnownFormatsWhenOneIsNotRecognised()
    {
        var error = Assert.Throws<SleepyException>(() => CollectionBuilders.Resolve("hoppscotch"));

        Assert.Contains("unknown format: hoppscotch", error.Message, StringComparison.Ordinal);
        Assert.Contains("insomnia", error.Message, StringComparison.Ordinal);
        Assert.Contains("bruno", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBuilderIsRegisteredUnderADistinctId()
    {
        var ids = CollectionBuilders.Ids;

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.All(CollectionBuilders.All, builder =>
        {
            Assert.False(string.IsNullOrWhiteSpace(builder.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(builder.DefaultOutputName));
        });
    }

    [Fact]
    public void InsomniaBuilderProducesTheDocumentTheWriterWouldHave()
    {
        var options = new BuildOptions { SourceName = "test" };
        var expected = JsonWriter.Write(InsomniaBuilder.Build([Module()], options));

        var collection = CollectionBuilders.Resolve("insomnia").Build([Module()], options);

        Assert.True(collection.IsSingleFile);
        Assert.Equal("insomnia.json", collection.Files[0].RelativePath);
        Assert.Equal(expected, collection.Content);
    }

    [Fact]
    public void EveryFormatBuildsSomethingForTheSameModules()
    {
        var options = new BuildOptions { SourceName = "test" };

        foreach (var builder in CollectionBuilders.All)
        {
            var collection = builder.Build([Module()], options);

            Assert.Equal(builder.FormatId, collection.FormatId);
            Assert.NotEmpty(collection.Files);
            Assert.All(collection.Files, file => Assert.NotEmpty(file.Content));
            Assert.Equal(builder.IsFolderFormat, collection.Files.Count > 1);
        }
    }

    [Fact]
    public void ContentIsOnlyAvailableForSingleFileCollections()
    {
        var many = new BuiltCollection("bru", [new BuiltFile("a.bru", "a"), new BuiltFile("b.bru", "b")]);

        Assert.False(many.IsSingleFile);
        Assert.Throws<InvalidOperationException>(() => many.Content);
    }
}
