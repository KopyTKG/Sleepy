# Ideas

Things worth doing, with enough detail to start. Not commitments — move an entry
to an issue when it becomes one.

## JSON syntax highlighting in the preview

The preview pane shows the export as plain monospace text. Colouring it would
make the structure readable at a glance, and the cost is bounded because the
preview is already truncated at 20,000 characters (a real 62 KB export becomes
roughly 620 visible lines, or about 3,000 spans).

Sketch:

1. `Sleepy.Core/JsonTokenizer.cs` — a scanner over the text the writer just
   produced, yielding `(offset, length, kind)` spans: property name, string,
   number, keyword (`true`/`false`/`null`), punctuation, whitespace. Telling a
   property name from a string value needs one character of lookahead for the
   `:`, which is reliable against our own writer. No UI dependency, so it is
   straightforward to unit-test.
2. `MainPage` swaps the preview `TextBox` for a `TextBlock` with
   `IsTextSelectionEnabled`, one `Run` per span, a brush per kind. Selection and
   copy still work, and **Copy JSON** already copies the untruncated export.
3. A palette in `App.xaml` in the same family as the rest of the dark theme.

Watch for: the inline collection is rebuilt on every option change, so if it
ever feels sluggish, debounce the re-convert — the text fields would benefit
from that anyway.
