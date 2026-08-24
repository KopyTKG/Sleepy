using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Sleepy.Gui.Services;

/// <summary>
/// Renders the preview: one row per line, syntax coloured, with the lines that
/// open a block foldable.
///
/// Colouring and structure both come from the tokenizer, so this works on a JSON
/// export and on a Bruno file's block syntax alike; whatever is not recognised
/// keeps the plain foreground and folds nothing.
/// </summary>
public static class PreviewHighlighter
{
    /// <summary>Beyond this, the preview is rendered as plain text in one block.</summary>
    private const int RichLimit = 60000;

    /// <summary>Rows are built eagerly, so a very long preview stops being rows.</summary>
    private const int MaxRows = 4000;

    private const double GutterWidth = 18;

    private const double FontSize = 12.5;

    private const double LineHeight = 17;

    private static readonly FontFamily Monospace = new("Consolas, monospace");

    public static void Render(Panel host, string text)
    {
        host.Children.Clear();

        if (text.Length == 0)
        {
            return;
        }

        var outline = text.Length <= RichLimit ? JsonOutline.Compute(text) : null;
        if (outline is null || outline.Count > MaxRows)
        {
            host.Children.Add(PlainBlock(text));
            return;
        }

        var document = new FoldableDocument(text, outline);
        foreach (var row in document.Rows)
        {
            host.Children.Add(row);
        }
    }

    /// <summary>
    /// The rows of one preview, and which blocks are folded. Visibility is
    /// recomputed from that set rather than toggled in place, so unfolding an
    /// outer block does not reveal blocks folded inside it.
    /// </summary>
    private sealed class FoldableDocument
    {
        private readonly string _text;
        private readonly IReadOnlyList<OutlineLine> _outline;
        private readonly List<TextBlock> _contents = [];
        private readonly Dictionary<int, TextBlock> _toggles = [];
        private readonly HashSet<int> _folded = [];

        public FoldableDocument(string text, IReadOnlyList<OutlineLine> outline)
        {
            _text = text;
            _outline = outline;
            Rows = new List<FrameworkElement>(outline.Count);

            for (var index = 0; index < outline.Count; index++)
            {
                Rows.Add(BuildRow(index, outline[index]));
            }
        }

        public List<FrameworkElement> Rows { get; }

        private FrameworkElement BuildRow(int index, OutlineLine line)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var content = LineBlock();
            Highlight(content, LineText(index));
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            _contents.Add(content);

            if (line.IsFoldable)
            {
                var toggle = FoldToggle();
                toggle.Tapped += (_, args) =>
                {
                    args.Handled = true;
                    Toggle(index);
                };

                Grid.SetColumn(toggle, 0);
                row.Children.Add(toggle);
                _toggles[index] = toggle;
            }

            return row;
        }

        private void Toggle(int index)
        {
            if (!_folded.Remove(index))
            {
                _folded.Add(index);
            }

            Apply();
        }

        private void Apply()
        {
            var hidden = JsonOutline.HiddenLines(_outline, _folded);
            for (var index = 0; index < Rows.Count; index++)
            {
                Rows[index].Visibility = hidden[index] ? Visibility.Collapsed : Visibility.Visible;
            }

            foreach (var (index, toggle) in _toggles)
            {
                var folded = _folded.Contains(index);
                toggle.Text = folded ? "▸" : "▾";

                Highlight(_contents[index], LineText(index));
                if (folded)
                {
                    var lines = _outline[index].CloseLine - index - 1;
                    _contents[index].Inlines.Add(new Run
                    {
                        Text = lines == 1 ? " … 1 line" : $" … {lines} lines",
                        Foreground = Resource("MutedTextBrush"),
                    });
                }
            }
        }

        private string LineText(int index) => _text.Substring(_outline[index].Start, _outline[index].Length);
    }

    private static TextBlock LineBlock() => new()
    {
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = Monospace,
        FontSize = FontSize,
        LineHeight = LineHeight,
        Foreground = Resource("CodeTextBrush"),
    };

    private static TextBlock FoldToggle() => new()
    {
        Text = "▾",
        FontSize = 9,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = Resource("SecondaryTextBrush"),
    };

    private static TextBlock PlainBlock(string text)
    {
        var block = LineBlock();
        block.Text = text;
        return block;
    }

    private static void Highlight(TextBlock target, string text)
    {
        target.Inlines.Clear();

        foreach (var token in JsonTokenizer.Tokenize(text))
        {
            var run = new Run { Text = text.Substring(token.Start, token.Length) };
            if (BrushFor(token.Kind) is { } brush)
            {
                run.Foreground = brush;
            }

            target.Inlines.Add(run);
        }
    }

    private static Brush? BrushFor(JsonTokenKind kind) => kind switch
    {
        JsonTokenKind.PropertyName => Resource("JsonKeyBrush"),
        JsonTokenKind.String => Resource("JsonStringBrush"),
        JsonTokenKind.Number => Resource("JsonNumberBrush"),
        JsonTokenKind.Keyword => Resource("JsonKeywordBrush"),
        JsonTokenKind.Punctuation => Resource("JsonPunctuationBrush"),
        _ => null,
    };

    private static Brush? Resource(string key)
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
