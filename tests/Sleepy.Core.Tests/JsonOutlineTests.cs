namespace Sleepy.Tests;

public class JsonOutlineTests
{
    private const string Document = """
        {
          "name": "Admin",
          "items": [
            {
              "id": 1,
              "url": "{{ _.baseUrl }}/users"
            }
          ],
          "empty": {},
          "done": true
        }
        """;

    private static List<OutlineLine> Outline(string text = Document) => JsonOutline.Compute(text);

    private static string LineText(string text, OutlineLine line) => text.Substring(line.Start, line.Length);

    [Fact]
    public void ReturnsOneEntryPerLine()
    {
        var outline = Outline();

        Assert.Equal(Document.Split('\n').Length, outline.Count);
        Assert.Equal("{", LineText(Document, outline[0]));
        Assert.Equal("}", LineText(Document, outline[^1]));
    }

    [Fact]
    public void PairsAnOpeningLineWithTheLineThatClosesIt()
    {
        var outline = Outline();

        Assert.True(outline[0].IsFoldable);
        Assert.Equal(outline.Count - 1, outline[0].CloseLine);

        // "items": [ opens on line 2 and closes on line 7.
        Assert.True(outline[2].IsFoldable);
        Assert.Equal(7, outline[2].CloseLine);
        Assert.Equal("  ],", LineText(Document, outline[7]));
    }

    [Fact]
    public void LeavesLinesWithNothingToFoldAlone()
    {
        var outline = Outline();

        Assert.False(outline[1].IsFoldable);                       // "name": "Admin",
        Assert.DoesNotContain(outline, line =>
            LineText(Document, line).Contains("\"empty\"", StringComparison.Ordinal) && line.IsFoldable);
    }

    [Fact]
    public void ReportsTheDepthEachLineSitsAt()
    {
        var outline = Outline();

        Assert.Equal(0, outline[0].Depth);                          // {
        Assert.Equal(1, outline[1].Depth);                          // "name"
        Assert.Equal(2, outline[3].Depth);                          // the object inside items
        Assert.Equal(3, outline[4].Depth);                          // "id"
        Assert.Equal(0, outline[^1].Depth);                         // closing brace
    }

    [Fact]
    public void IsNotFooledByBracesInsideStrings()
    {
        var outline = Outline();
        var urlLine = outline.Single(line =>
            LineText(Document, line).Contains("baseUrl", StringComparison.Ordinal));

        // The line holds "{{ _.baseUrl }}" and opens nothing.
        Assert.False(urlLine.IsFoldable);
        Assert.Equal(3, urlLine.Depth);
    }

    [Fact]
    public void HandlesArraysAsWellAsObjects()
    {
        const string text = "[\n  1,\n  2\n]";

        var outline = Outline(text);

        Assert.True(outline[0].IsFoldable);
        Assert.Equal(3, outline[0].CloseLine);
    }

    [Fact]
    public void SurvivesUnbalancedText()
    {
        const string text = "{\n  \"a\": [\n";

        var outline = Outline(text);

        Assert.All(outline, line => Assert.InRange(line.CloseLine, -1, outline.Count - 1));
    }

    [Fact]
    public void HandlesTextThatIsNotJsonAtAll()
    {
        const string bru = """
            get {
              url: {{baseUrl}}/users
            }
            """;

        var outline = Outline(bru);

        Assert.Equal(3, outline.Count);
        Assert.True(outline[0].IsFoldable);
        Assert.Equal(2, outline[0].CloseLine);
    }

    [Fact]
    public void HidesTheLinesInsideAFoldedBlock()
    {
        var outline = Outline();

        var hidden = JsonOutline.HiddenLines(outline, new HashSet<int> { 2 });   // "items": [

        Assert.False(hidden[2]);                                    // the opening line stays
        Assert.True(hidden[3]);
        Assert.True(hidden[7]);                                     // through its closing line
        Assert.False(hidden[8]);                                    // and no further
    }

    [Fact]
    public void KeepsInnerFoldsFoldedWhenTheOuterOneOpens()
    {
        var outline = Outline();

        // Fold the object inside items, then unfold everything outside it.
        var hidden = JsonOutline.HiddenLines(outline, new HashSet<int> { 3 });

        Assert.False(hidden[2]);                                    // "items": [ is open
        Assert.False(hidden[3]);                                    // its own opening line shows
        Assert.True(hidden[4]);                                     // but its contents stay hidden
        Assert.True(hidden[6]);
        Assert.False(hidden[7]);
    }

    [Fact]
    public void IgnoresFoldsOnLinesThatCannotFold()
    {
        var outline = Outline();

        var hidden = JsonOutline.HiddenLines(outline, new HashSet<int> { 1, -5, 999 });

        Assert.DoesNotContain(true, hidden);
    }

    [Fact]
    public void HandlesEmptyText() => Assert.Single(JsonOutline.Compute(string.Empty));

    [Fact]
    public void OutlinesAWholeExport()
    {
        var export = CollectionBuilders.Default.Build(
            [new ApiModule("Admin", System.Text.Json.Nodes.JsonNode.Parse(
                    """{"openapi":"3.0.1","info":{"title":"A"},"paths":{"/a":{"get":{"operationId":"a"}}}}"""),
                string.Empty, "spec.json")],
            new BuildOptions { SourceName = "test" }).Content;

        var outline = JsonOutline.Compute(export);

        Assert.Equal(export.Split('\n').Length, outline.Count);
        Assert.Contains(outline, line => line.IsFoldable);
        Assert.All(outline, line => Assert.InRange(line.Start + line.Length, 0, export.Length));
    }
}
