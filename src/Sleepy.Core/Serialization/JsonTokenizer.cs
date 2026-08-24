namespace Sleepy.Serialization;

/// <summary>What a stretch of text is, for the purpose of colouring it.</summary>
public enum JsonTokenKind
{
    /// <summary>Whitespace, or anything the scanner does not recognise.</summary>
    Plain,

    /// <summary>A string used as an object key: quoted, and followed by a colon.</summary>
    PropertyName,

    /// <summary>A quoted string that is not a key.</summary>
    String,

    Number,

    /// <summary><c>true</c>, <c>false</c> or <c>null</c>.</summary>
    Keyword,

    /// <summary>One of <c>{}[],:</c>.</summary>
    Punctuation,
}

/// <summary>A run of text of one kind, positioned in the original string.</summary>
public readonly record struct JsonToken(int Start, int Length, JsonTokenKind Kind);

/// <summary>
/// Splits text into runs a viewer can colour. It is deliberately lenient: it
/// scans rather than parses, so anything it does not recognise comes back as
/// <see cref="JsonTokenKind.Plain"/> instead of an error.
///
/// That is what lets the same tokenizer colour a Bruno <c>.bru</c> file — mostly
/// block syntax with a JSON body inside it — as well as a whole export.
/// </summary>
public static class JsonTokenizer
{
    /// <summary>
    /// Every token, in order, covering the whole input: concatenating the runs
    /// gives back the original text.
    /// </summary>
    public static List<JsonToken> Tokenize(string text)
    {
        var tokens = new List<JsonToken>();
        var plainStart = -1;
        var index = 0;

        void FlushPlain(int end)
        {
            if (plainStart >= 0)
            {
                tokens.Add(new JsonToken(plainStart, end - plainStart, JsonTokenKind.Plain));
                plainStart = -1;
            }
        }

        while (index < text.Length)
        {
            var character = text[index];

            if (character == '"')
            {
                var length = StringLength(text, index);
                FlushPlain(index);
                tokens.Add(new JsonToken(
                    index,
                    length,
                    IsFollowedByColon(text, index + length) ? JsonTokenKind.PropertyName : JsonTokenKind.String));
                index += length;
                continue;
            }

            if (IsNumberStart(text, index))
            {
                var length = NumberLength(text, index);
                FlushPlain(index);
                tokens.Add(new JsonToken(index, length, JsonTokenKind.Number));
                index += length;
                continue;
            }

            if (KeywordLength(text, index) is > 0 and var keyword)
            {
                FlushPlain(index);
                tokens.Add(new JsonToken(index, keyword, JsonTokenKind.Keyword));
                index += keyword;
                continue;
            }

            if (character is '{' or '}' or '[' or ']' or ',' or ':')
            {
                FlushPlain(index);
                tokens.Add(new JsonToken(index, 1, JsonTokenKind.Punctuation));
                index++;
                continue;
            }

            if (plainStart < 0)
            {
                plainStart = index;
            }

            index++;
        }

        FlushPlain(text.Length);
        return tokens;
    }

    /// <summary>The length of the quoted string starting at <paramref name="start"/>, quotes included.</summary>
    private static int StringLength(string text, int start)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index += 2;
                continue;
            }

            if (text[index] == '"')
            {
                return index - start + 1;
            }

            index++;
        }

        // An unterminated string runs to the end rather than throwing.
        return text.Length - start;
    }

    private static bool IsFollowedByColon(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index < text.Length && text[index] == ':';
    }

    /// <summary>
    /// A number has to start a token: the <c>1</c> in <c>x1</c> is part of a word,
    /// and the <c>-</c> in <c>a-b</c> is not a sign.
    /// </summary>
    private static bool IsNumberStart(string text, int index)
    {
        var character = text[index];
        if (!char.IsAsciiDigit(character) && character != '-')
        {
            return false;
        }

        if (character == '-' && (index + 1 >= text.Length || !char.IsAsciiDigit(text[index + 1])))
        {
            return false;
        }

        return index == 0 || !IsWordCharacter(text[index - 1]);
    }

    private static int NumberLength(string text, int start)
    {
        var index = start + 1;
        while (index < text.Length
            && (char.IsAsciiDigit(text[index]) || text[index] is '.' or 'e' or 'E' or '+' or '-'))
        {
            index++;
        }

        return index - start;
    }

    private static int KeywordLength(string text, int index)
    {
        if (index > 0 && IsWordCharacter(text[index - 1]))
        {
            return 0;
        }

        foreach (var keyword in (ReadOnlySpan<string>)["true", "false", "null"])
        {
            if (index + keyword.Length <= text.Length
                && text.AsSpan(index, keyword.Length).SequenceEqual(keyword)
                && (index + keyword.Length == text.Length || !IsWordCharacter(text[index + keyword.Length])))
            {
                return keyword.Length;
            }
        }

        return 0;
    }

    private static bool IsWordCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '-';
}
