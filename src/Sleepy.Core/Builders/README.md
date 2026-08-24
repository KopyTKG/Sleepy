# Builders

Everything before this folder is format-agnostic: discovery finds the modules,
`SpecParser` reads them, `ExampleGenerator` fills in request bodies. A builder is
the last step — it turns `IReadOnlyList<ApiModule>` plus `BuildOptions` into one
API client's collection.

Adding a format means writing an `ICollectionBuilder` and adding it to
`CollectionBuilders.All`. Nothing else in the pipeline changes, and the CLI's
`--format` and the GUI's format list pick it up from the registry.

## What a builder receives

- **`ApiModule`** — name, the parsed specification as a `JsonNode`, the base URL
  discovery derived, and where it came from.
- **`BuildOptions`** — workspace name, folder layout, bearer token, and the
  source string recorded in the export.

`InsomniaBuilder` shows the shape: collect operations per module, decide folder
structure from the layout, give every resource a stable id, and keep the output
deterministic (sorted keys, sorted paths, fixed date placeholders) so the same
spec always produces identical bytes.

## What is here

**`InsomniaBuilder`** — a flat list of resources, each with an id and a
`parentId`. The reference implementation; the golden test pins its bytes.

**`PostmanV21Builder`** — one JSON document whose `item` array nests folders and
requests. URLs are objects (`{raw, host, path, query}`), path parameters become
`:id` segments with a matching `variable` entry, and variables are `{{baseUrl}}`
rather than Insomnia's `{{ _.baseUrl }}`. The collection id is derived from the
name so re-runs stay byte-identical.

**`BrunoBuilder`** — a directory: `bruno.json` at the root, one `.bru` file per
request in its own block syntax, folders on disk mirroring the collection tree,
and variables under `environments/`. This is what `BuiltCollection`'s file list
exists for. Folder names are sanitised, because a layout that reads
`Admin / Users` in a JSON document would be two directories on disk.

## Formats worth adding

**Hoppscotch** — a single JSON document, closer to Postman than to Insomnia.

**`.http` / REST Client** — plain text, one file per module or one file overall,
with `###` separating requests. The simplest possible builder, and a good check
that the abstraction is not JSON-shaped by accident.

## Keeping formats honest

The golden test in `tests/Sleepy.Core.Tests` pins the Insomnia output byte for
byte. A new builder deserves the same: a fixture converted and compared against a
recorded file, so a change to shared code that shifts one format's output cannot
pass unnoticed.
