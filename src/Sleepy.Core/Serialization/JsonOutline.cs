namespace Sleepy.Serialization;

/// <summary>
/// One line of text, with what it takes to fold it: how deep it sits, and — if
/// it opens a block — the line that closes it.
/// </summary>
/// <param name="Start">Index of the first character, in the original text.</param>
/// <param name="Length">Length of the line, excluding its newline.</param>
/// <param name="Depth">Nesting depth, counting from zero.</param>
/// <param name="CloseLine">
/// The line closing the block this line opens, or -1 when the line opens nothing
/// foldable — either it opens no block at all, or the block ends on the same line.
/// </param>
public readonly record struct OutlineLine(int Start, int Length, int Depth, int CloseLine)
{
    /// <summary>Whether this line can be folded.</summary>
    public bool IsFoldable => CloseLine >= 0;
}

/// <summary>
/// Works out which lines of a document open a block and where it ends, so a
/// viewer can fold them.
///
/// It walks the tokenizer's output rather than the raw characters, so a brace
/// inside a string — <c>"{{ _.baseUrl }}"</c> appears in every export sleepy
/// writes — is never mistaken for structure.
/// </summary>
public static class JsonOutline
{
    public static List<OutlineLine> Compute(string text)
    {
        var lines = SplitLines(text);
        var outline = new List<OutlineLine>(lines.Count);
        var closes = new int[lines.Count];
        Array.Fill(closes, -1);
        var depths = new int[lines.Count];

        var brackets = JsonTokenizer.Tokenize(text)
            .Where(token => token.Kind == JsonTokenKind.Punctuation)
            .Where(token => text[token.Start] is '{' or '[' or '}' or ']')
            .ToList();

        var openLines = new Stack<int>();
        var depth = 0;
        var bracket = 0;

        for (var line = 0; line < lines.Count; line++)
        {
            var (start, length) = lines[line];
            var end = start + length;

            var opensOnThisLine = 0;
            var startsByClosing = false;
            var first = true;

            while (bracket < brackets.Count && brackets[bracket].Start < end)
            {
                var character = text[brackets[bracket].Start];
                if (character is '{' or '[')
                {
                    openLines.Push(line);
                    opensOnThisLine++;
                }
                else if (openLines.Count > 0)
                {
                    var openLine = openLines.Pop();

                    // A block opened and closed on one line has nothing to fold.
                    if (line > openLine)
                    {
                        closes[openLine] = line;
                    }

                    if (first && opensOnThisLine == 0)
                    {
                        startsByClosing = true;
                    }
                }

                first = false;
                bracket++;
            }

            // A line that begins by closing a block belongs to the depth outside it.
            depths[line] = startsByClosing ? Math.Max(0, depth - 1) : depth;
            depth = Math.Max(0, depth + opensOnThisLine - CountClosers(text, lines[line]));
        }

        for (var line = 0; line < lines.Count; line++)
        {
            outline.Add(new OutlineLine(lines[line].Start, lines[line].Length, depths[line], closes[line]));
        }

        return outline;
    }

    /// <summary>
    /// Which lines are hidden when the given lines are folded. Folds nest, so a
    /// line is hidden if it sits inside any folded block — which is why a viewer
    /// should recompute this rather than toggle visibility in place: unfolding an
    /// outer block must not reveal blocks folded inside it.
    /// </summary>
    public static bool[] HiddenLines(IReadOnlyList<OutlineLine> outline, IReadOnlySet<int> foldedLines)
    {
        var hidden = new bool[outline.Count];
        foreach (var open in foldedLines)
        {
            if (open < 0 || open >= outline.Count || !outline[open].IsFoldable)
            {
                continue;
            }

            for (var line = open + 1; line <= outline[open].CloseLine && line < hidden.Length; line++)
            {
                hidden[line] = true;
            }
        }

        return hidden;
    }

    private static int CountClosers(string text, (int Start, int Length) line)
    {
        var count = 0;
        foreach (var token in JsonTokenizer.Tokenize(text.Substring(line.Start, line.Length)))
        {
            if (token.Kind == JsonTokenKind.Punctuation
                && text[line.Start + token.Start] is '}' or ']')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Line spans, excluding the newline itself.</summary>
    private static List<(int Start, int Length)> SplitLines(string text)
    {
        var lines = new List<(int, int)>();
        var start = 0;

        for (var index = 0; index <= text.Length; index++)
        {
            if (index == text.Length || text[index] == '\n')
            {
                var end = index;
                if (end > start && text[end - 1] == '\r')
                {
                    end--;
                }

                lines.Add((start, end - start));
                start = index + 1;
            }
        }

        // A trailing newline produces one empty line, which is not worth showing.
        if (lines.Count > 1 && lines[^1].Item2 == 0 && text.Length > 0 && text[^1] == '\n')
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
