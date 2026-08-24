namespace Sleepy.Tests;

public class JsonTokenizerTests
{
    private static List<(string Text, JsonTokenKind Kind)> Tokens(string text)
        => JsonTokenizer.Tokenize(text)
            .Select(token => (text.Substring(token.Start, token.Length), token.Kind))
            .ToList();

    private static List<(string Text, JsonTokenKind Kind)> Significant(string text)
        => Tokens(text).Where(token => token.Kind != JsonTokenKind.Plain).ToList();

    [Fact]
    public void CoversTheWholeInput()
    {
        const string text = """{"a": [1, true, null], "b": "c"}""";

        var tokens = JsonTokenizer.Tokenize(text);

        Assert.Equal(text, string.Concat(tokens.Select(token => text.Substring(token.Start, token.Length))));
        Assert.Equal(0, tokens[0].Start);
    }

    [Fact]
    public void TellsPropertyNamesFromStringValues()
    {
        var tokens = Significant("""{"key": "value"}""");

        Assert.Contains(("\"key\"", JsonTokenKind.PropertyName), tokens);
        Assert.Contains(("\"value\"", JsonTokenKind.String), tokens);
    }

    [Fact]
    public void TreatsAStringBeforeAColonAsAKeyEvenAcrossWhitespace()
        => Assert.Contains(("\"key\"", JsonTokenKind.PropertyName), Significant("\"key\"   : 1"));

    [Fact]
    public void KeepsEscapedQuotesInsideStrings()
    {
        var tokens = Significant("""{"a": "he said \"hi\" once"}""");

        Assert.Contains(tokens, token =>
            token.Kind == JsonTokenKind.String && token.Text.Contains("once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("-0.25")]
    [InlineData("1e-7")]
    public void RecognisesNumbers(string number)
        => Assert.Contains((number, JsonTokenKind.Number), Significant($"[{number}]"));

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void RecognisesKeywords(string keyword)
        => Assert.Contains((keyword, JsonTokenKind.Keyword), Significant($"[{keyword}]"));

    [Fact]
    public void DoesNotFindNumbersOrKeywordsInsideWords()
    {
        var tokens = Significant("baseUrl_1: nullable-thing");

        Assert.DoesNotContain(tokens, token => token.Kind == JsonTokenKind.Number);
        Assert.DoesNotContain(tokens, token => token.Kind == JsonTokenKind.Keyword);
    }

    [Fact]
    public void MarksStructuralCharactersAsPunctuation()
    {
        var punctuation = Significant("""{"a":[1],"b":2}""")
            .Where(token => token.Kind == JsonTokenKind.Punctuation)
            .Select(token => token.Text)
            .ToList();

        Assert.Equal(["{", ":", "[", "]", ",", ":", "}"], punctuation);
    }

    [Fact]
    public void LeavesUnknownTextPlain()
    {
        // A Bruno block: mostly not JSON, with a JSON body inside it.
        const string bru = """
            get {
              url: {{baseUrl}}/users
            }

            body:json {
              {
                "n": ""
              }
            }
            """;

        var tokens = Significant(bru);

        Assert.Contains(("\"n\"", JsonTokenKind.PropertyName), tokens);
        Assert.Contains(tokens, token => token.Kind == JsonTokenKind.Punctuation && token.Text == "{");
        Assert.Equal(bru, string.Concat(JsonTokenizer.Tokenize(bru)
            .Select(token => bru.Substring(token.Start, token.Length))));
    }

    [Fact]
    public void SurvivesAnUnterminatedString()
    {
        const string text = """{"a": "unterminated""";

        var tokens = JsonTokenizer.Tokenize(text);

        Assert.Equal(text, string.Concat(tokens.Select(token => text.Substring(token.Start, token.Length))));
    }

    [Fact]
    public void HandlesEmptyInput() => Assert.Empty(JsonTokenizer.Tokenize(string.Empty));

    [Fact]
    public void TokenizesAWholeExportQuickly()
    {
        var export = CollectionBuilders.Default.Build(
            [new ApiModule("Admin", System.Text.Json.Nodes.JsonNode.Parse(
                """{"openapi":"3.0.1","info":{"title":"A"},"paths":{"/a":{"get":{"operationId":"a"}}}}"""),
                string.Empty, "spec.json")],
            new BuildOptions { SourceName = "test" }).Content;

        var tokens = JsonTokenizer.Tokenize(export);

        Assert.NotEmpty(tokens);
        Assert.Equal(export, string.Concat(tokens.Select(token => export.Substring(token.Start, token.Length))));
    }
}
