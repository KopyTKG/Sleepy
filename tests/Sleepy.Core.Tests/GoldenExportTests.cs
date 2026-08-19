namespace Sleepy.Tests;

/// <summary>
/// Locks the whole pipeline down to a byte-for-byte expected export, so any
/// change in ids, ordering, formatting, or example generation has to be
/// deliberate.
/// </summary>
public class GoldenExportTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string BuildExport(string fixture)
    {
        var spec = SpecParser.Parse(File.ReadAllBytes(FixturePath(fixture)), fixture);
        var document = InsomniaBuilder.Build(
            [new ApiModule("Fixture", spec, string.Empty, fixture)],
            new BuildOptions { SourceName = "fixture" });

        return JsonWriter.Write(document);
    }

    [Fact]
    public void MatchesTheRecordedExport()
    {
        // The fixture is stored with a trailing newline, the writer emits none.
        var expected = File.ReadAllText(FixturePath("expected-insomnia.json")).TrimEnd('\n');

        Assert.Equal(expected, BuildExport("openapi.json"));
    }

    [Fact]
    public void YamlAndJsonFixturesAgree()
        => Assert.Equal(BuildExport("openapi.json"), BuildExport("openapi.yaml"));
}
