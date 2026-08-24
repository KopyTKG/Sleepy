# Sleepy.Core and its libraries

The engine is five libraries, each with one job. They are nested under
`Sleepy.Core/` because they are one engine, but they are separate projects so
that the dependency direction is enforced by the compiler rather than by good
intentions: nothing lower in this list can reach back up.

```
Sleepy.Core            the vocabulary, no dependencies
  └ Serialization      JSON and YAML in, one JsonNode tree out; the writer
      ├ Discovery      where documents come from
      └ OpenApi        what a specification means
          └ Builders   what a collection looks like, one class per format
```

## Sleepy.Core

The types everything else speaks in, and nothing that does work:

| Type | What it is |
| --- | --- |
| `ApiModule` | one parsed specification, its name, base URL, and origin |
| `Operation` | one operation flattened out of a paths object |
| `BuildOptions`, `FolderLayout` | what the user asked the output to look like |
| `SleepyException` | an error meant for the user, not a stack trace |
| `SleepyInfo` | version, build identity, licence |
| `Slug` | identifier slugification, needed by every format |

If a type here starts *doing* something, it belongs in one of the libraries
below.

## Sleepy.Serialization

`SpecParser` reads JSON or YAML and hands back a `JsonNode`, whichever it was.
`Yaml` converts YamlDotNet's representation model rather than deserializing into
types, which keeps it reflection-free and Native AOT safe. `JsonWriter` writes
back out with sorted keys and fixed indentation, which is what makes the output
deterministic. `Json` is the small set of never-throwing read helpers the rest of
the code leans on.

This is the only library that knows YamlDotNet exists.

## Sleepy.Discovery

Turns one input — a path, a URL, stdin, a Swagger UI, a manifest — into the list
of `ApiModule`s it refers to. `SpecSource` owns the HTTP client, the headers, and
the TLS decision; `ModuleDiscovery` owns the search: parse what came back, follow
the page's script tags, probe the well-known endpoints, resolve the references it
finds.

Nothing here knows what a collection is.

## Sleepy.OpenApi

The meaning read out of a specification, shared by every output format.
`Operations` flattens the paths object, deciding tags, operation ids and ordering
once so all formats agree. `ExampleGenerator` walks a schema into a placeholder
request body. `CollectionPlan` holds the decisions a collection needs before any
format is chosen: what it is called, which modules contribute anything, and which
variable each module's base URL hides behind.

## Sleepy.Builders

One class per output format behind `ICollectionBuilder`, plus the registry the
CLI and GUI read their format list from. See its own README for how to add one.
