# sleepy

`sleepy` turns an **OpenAPI 3** specification — JSON or YAML, a file or a live
URL — into a collection you can import: **Insomnia v4**, **Postman v2.1**, or a
**Bruno** folder.

It handles **multi-module APIs**: when a URL points at a Swagger UI serving
several definitions (the "Select a definition" dropdown), sleepy discovers every
module, fetches them all, and merges them into one collection.

Two apps share one engine:

| | |
| --- | --- |
| **`sleepy`** | command line tool, shipped as a single native binary |
| **`sleepy-GUI`** | desktop app ([Uno Platform](https://platform.uno/)) for Linux and Windows |

## Install

On Arch and derivatives, from the AUR:

```sh
paru -S sleepy-bin        # the command line tool
paru -S sleepy-gui-bin    # the desktop app
```

Otherwise take the binary or the desktop bundle from the
[latest release](https://gitlab.com/kopytkg/sleepy/-/releases), or build from
source as below.

## Quick start

```sh
mise install                                   # .NET 10 SDK + just
just publish-cli                               # -> dist/cli-linux-x64/sleepy

# a local file
sleepy openapi.json -o insomnia.json

# a live API: baseUrl is prefilled from the URL
sleepy https://api.example.com/openapi.json -o insomnia.json

# every module a Swagger UI serves, merged into one collection
sleepy https://api.example.com/swagger/index.html -o insomnia.json
```

Import the result in Insomnia via *Import → From File*, then fill in `bearer`
(and `baseUrl`, if it was not derived) on the "Base Environment".

## sleepy (command line)

```
sleepy <spec|url|-> [<spec|url> ...] [options]
```

| Option | Meaning |
| --- | --- |
| `-o`, `--output <file>` | write the export to a file instead of stdout |
| `--format <id>` | output format: `insomnia` (default), `postman`, `bruno` |
| `--base-url <url>` | override the environment's `baseUrl` for every module |
| `--name <name>` | workspace name (default: the spec title, or the host for several modules) |
| `--module <name>` | only include this module; repeatable, case-insensitive |
| `--flat` | one folder level named `"<Module> / <Tag>"` instead of nesting |
| `--no-tags` | a folder per module only, without tag subfolders |
| `--bearer <token>` | prefill the environment's `bearer` variable |
| `-H`, `--header <h>` | extra request header when fetching, e.g. `"Authorization: Bearer x"`; repeatable |
| `-k`, `--insecure` | do not verify TLS certificates when fetching |
| `--list` | print the discovered modules and exit |
| `--license` | print the licence and third-party notices |
| `-h`, `--help` / `-v`, `--version` | help / version |

- Each input may be a **file path**, an **http(s) URL**, or `-` for **stdin**,
  and several inputs may be given at once. JSON and YAML are both accepted; the
  syntax is detected from the content and the file extension.
- Fetching a URL prefills `baseUrl` with that URL's **origin** (scheme, host,
  optional port), so requests target the live API out of the box.
- Without `-o` the export goes to **stdout** and progress notes to stderr, so
  piping stays clean.
- Bad usage exits with 2; a failed fetch, read, or parse prints to stderr and
  exits with 1.

```sh
# from a file to stdout
sleepy openapi.json

# a Postman collection, or a Bruno folder
sleepy openapi.json --format postman -o postman-collection.json
sleepy openapi.json --format bruno -o ./bruno-collection

# fetch by URL but point the requests at a different host
sleepy https://docs.example.com/openapi.json --base-url https://api.example.com

# pipe it in yourself
curl -s https://api.example.com/openapi.yaml \
  | sleepy - --base-url https://api.example.com -o insomnia.json
```

## Multi-module APIs

Point sleepy at the Swagger UI URL — the same one you have open in the browser —
and it pulls every definition from the dropdown:

```sh
# the whole API: one folder per module, tag subfolders inside
sleepy https://api.example.com:12000/swagger/index.html -o insomnia.json

# see what would be pulled, without converting
sleepy https://api.example.com:12000/swagger/index.html --list

# only some modules (repeatable, case-insensitive)
sleepy https://api.example.com:12000 --module Admin --module Workflow -o insomnia.json

# several specs listed by hand — each becomes a module
sleepy https://a.example.com/openapi.json ./billing.json -o insomnia.json
```

sleepy reads the module list from any of these:

| Source | Shape |
| --- | --- |
| `swagger-ui-init.js` | Swashbuckle: `"urls":[{"url":…,"name":…}]` embedded in the script |
| `/v3/api-docs/swagger-config` | springdoc: `{"urls":[{"url":…,"name":…}]}` |
| `/swagger-resources` | springfox: `[{"name":…,"location":…}]` |
| a manifest you write | the same `{"urls":[{"name":…,"url":…}]}` JSON, with URLs or paths relative to the manifest |

Given a URL that is not itself a document, sleepy follows the page's own
`<script src=…>` tags — which file holds the configuration differs between
Swagger UI packagings (`index.js`, `swagger-ui-init.js`, a bundled app script) —
and then probes `swagger-ui-init.js`, `index.js`, `swagger/v1/swagger.json`,
`v3/api-docs/swagger-config`, `swagger-config`, and `swagger-resources` against
both the URL's directory and the site root. So `https://host:12000`,
`https://host:12000/swagger/`, `https://host:12000/swagger/index.html`, and
`https://host:12000/swagger/index.html?urls.primaryName=Admin` all work, while a
URL naming a document that is not there fails instead of silently converting
something else.

Modules that declare no paths are left out rather than contributing an empty
folder.

Module names come from the dropdown (falling back to `info.title`), and resource
ids carry the module slug, so two modules exposing the same `operationId` never
collide.

**baseUrl across modules**

- All modules on one origin → a single `baseUrl` variable, prefilled with it.
- Modules on different origins → one `baseUrl_<module>` per module, each
  prefilled from its own spec URL, each request wired to its own.
- `--base-url` forces one shared `baseUrl` for everything.

Every request authenticates with `{{ _.bearer }}`, so filling `bearer` in once
covers all modules.

**Specs behind auth or a self-signed certificate**

```sh
sleepy https://api.example.com/swagger/index.html \
  -H "Authorization: Bearer $TOKEN" -H "X-Tenant: acme" --insecure -o insomnia.json
```

`-H` and `-k` apply to fetching the documents only; neither is written into the
export.

## sleepy-GUI (desktop app)

Two panes: controls on the left, a live preview of the export on the right, a
status line along the bottom.

Drop a spec anywhere on the window — or paste a path or Swagger UI URL and press
Enter — and every module it finds is loaded. Then:

- tick the **modules** to include (shown once the input serves more than one);
- pick the **output format**: Insomnia, Postman, or Bruno;
- pick the **folder layout**: module folders with tag subfolders, flat
  `"Module / Tag"` folders, or module folders only;
- set the **workspace name**, **base URL**, and optionally a **bearer token**
  written straight into the environment, then press **Apply** (or Enter in the
  field) — text fields are applied on request rather than on every keystroke, so
  typing a name does not rebuild the preview eleven times;
- open **Fetch options** for a request header (e.g. `Authorization: Bearer …`)
  or to ignore TLS certificate errors while fetching;
- watch the preview — syntax-highlighted, with collapsible blocks and a request
  count — follow every change, then **Save export…** or **Copy JSON**.

Saving or copying with an edit that was never applied asks first, so an export
cannot quietly disagree with the fields above it.

Choosing Bruno turns the save into a folder picker and the preview into the file
tree it will write, each file under its path — a folder format has nothing single
to copy, so the copy button steps aside for it.

Drag and drop uses the platform's own file drop support; where the desktop does
not provide it, the path box and **Browse…** do the same job.

```sh
just gui                                       # run from source

just publish-gui-linux                         # -> dist/gui-linux-x64/
just publish-gui-windows                       # -> dist/gui-win-x64/ (cross-publishes from Linux)
```

On Windows the WinAppSDK head (`-f net10.0-windows10.0.26100`) builds too; it
needs the Windows SDK and is enabled automatically when the build runs there.

### What it remembers

The app keeps its choices in `~/.config/sleepy/settings.json` (`%AppData%\sleepy`
on Windows): the language, the interface scale, the output format, the folder layout, and
the fetch options — the request header and whether TLS errors are ignored. The About
dialog names the file.

The bearer token and the base URL are deliberately not saved. Both are per-API,
and one of them is a credential.

A missing or malformed file means defaults rather than an error, so deleting it
resets the app.

### Languages

English and Czech. The app follows the system language; the picker in the
bottom-right pins it to one and remembers the choice. Counted phrases use real
plural forms, including the Czech one/few/many split (`1 požadavek`,
`3 požadavky`, `8 požadavků`).

Strings live in `src/Sleepy.Gui/Strings/<language>/Resources.resw` — XAML text is
keyed by `x:Uid`, runtime messages go through `Services/Localizer.cs`. To add a
language, copy a `Resources.resw` folder, translate it, then add the tag to
`Localizer.SupportedLanguages` and an entry to the picker.

### Linux: X11, Wayland, or a bare console

The app renders with Skia through Uno's **X11** host, or straight on the
**framebuffer/DRM** with no session at all. Uno has no native Wayland backend
yet, so on a Wayland session the X11 host runs through **XWayland** — which
every common compositor ships, so it works out of the box.

The Linux build carries one binary and three launchers:

| Launcher | Session |
| --- | --- |
| `sleepy-gui-x11` | an X11 session |
| `sleepy-gui-wayland` | a Wayland session, via XWayland (fails clearly if XWayland is off) |
| `sleepy-gui-console` | no session at all — framebuffer/DRM from a TTY |
| `sleepy-gui` | picks a host automatically |

The host can also be forced with `--display x11`, `--display framebuffer`, or
`SLEEPY_GUI_DISPLAY`.

### Interface scale

On a dense display the default size can be too small to read. The scale picker
in the status bar sets it — *Auto scale* follows the display, or pick 100–250 %
— and offers to restart, because the display host reads the scale once, at
startup. The choice is saved with the other preferences.

For a one-off, `sleepy-gui --scale 150%` (also `1.5` or `150`) overrides the
saved value for that run, and an `UNO_DISPLAY_SCALE_OVERRIDE` already in the
environment is respected over the saved value. This applies on Linux, X11 and
Wayland alike; on Windows the app follows Windows' own display scaling.

`install.sh` in the same folder installs the app to `~/.local/share/sleepy-gui`,
puts the launchers on `PATH`, and registers both desktop entries and the icon
(`PREFIX=…` overrides the location). Installing is what gives the window its
icon: the desktop matches it by `StartupWMClass`, since Uno's X11 host cannot
set a window icon itself.

The artwork is `icon.png` in the repository root — replace it and rebuild to
regenerate every icon size and the Windows `.ico`. The splash screen keeps its
own copy at `src/Sleepy.Gui/Assets/Splash/splash.png`, because Uno.Resizetizer
refuses to share one file between icon and splash.

## Output formats

| Format | `--format` | Output |
| --- | --- | --- |
| Insomnia v4 | `insomnia` (default) | one JSON export |
| Postman v2.1 | `postman` | one JSON collection |
| Bruno | `bruno` | a directory of `.bru` files |

Every format is built from the same plan, so a collection describes the same API
whichever one you pick: the same folder layout options, the same modules, the
same base-URL variables, the same generated request bodies. What differs is only
what each client understands — Postman writes URLs as objects and path
parameters as `:id`, Bruno writes one file per request with `bruno.json` at the
root and variables under `environments/`, Insomnia writes a flat resource list.

Because Bruno is a directory, `-o` names a folder rather than a file, and sleepy
reports how many files it wrote.

Adding another format is one class in `src/Sleepy.Core/Builders` and one line in
its registry — see the README there, and `src/Sleepy.Core/README.md` for how the
engine's libraries divide the work.

## What it produces

- A `workspace` named after the spec's `info.title` (or the host, with several
  modules).
- An `environment` called **"Base Environment"** holding `baseUrl` and `bearer`.
- One `request_group` per OpenAPI **tag** (an operation's first tag, or
  `default`), sorted alphabetically; with several modules these nest inside a
  folder per module.
- One `request` per operation (`get`/`post`/`put`/`delete`/`patch`) with:
  - a stable `_id` from `operationId` (or `<method>_<path>`), slugified and
    prefixed `req_`;
  - `name` = `"<METHOD> <path>"` and an uppercase `method`;
  - `url` = `"{{ _.baseUrl }}<path>"`;
  - a `Content-Type: application/json` header when the operation has a
    `requestBody`;
  - query `parameters` (those with `in == "query"`);
  - bearer `authentication`: `{ "type": "bearer", "token": "{{ _.bearer }}" }`;
  - a `body` holding a generated example when the request body has an
    `application/json` schema.

Output is deterministic: object keys are written in ordinal order and paths are
processed sorted, so the same spec always yields byte-identical output.

### Schema → example generation

Request-body examples come from walking the JSON schema:

- `$ref` resolves against the root document (`#/...`), with cycle guarding and a
  depth cap of 6.
- `allOf` merges every subschema object; `oneOf`/`anyOf` take the first entry.
- `enum` takes its first value; an explicit `example` is used as-is.
- `string` → `""`; `format: date-time`, `date`, and `uuid` get placeholders.
- `integer`/`number` → `0`, `boolean` → `false`.
- `array` → `[ example(items) ]`.
- `object` (or an untyped schema) → an object of `property → example(property)`.

> Date placeholders and `__export_date` are fixed strings
> (`2020-01-01T00:00:00Z` / `2020-01-01`) rather than the current time, to keep
> output reproducible.

## Development

The toolchain is pinned with [mise](https://mise.jdx.dev/), every task runs
through [just](https://just.systems/), and the Uno SDK version lives in
`global.json`.

```sh
mise install     # .NET 10 SDK + just, as pinned in mise.toml
just             # list every recipe
```

| Recipe | What it does |
| --- | --- |
| `just build` | build the whole solution |
| `just build-cli` | build only the CLI and the engine (fast loop) |
| `just test` | run the test suite |
| `just coverage` | tests plus a Cobertura report under `dist/coverage` |
| `just lint` / `just fmt` | check / apply formatting |
| `just run openapi.json -o out.json` | run the CLI |
| `just gui` | run the desktop app |
| `just publish-cli` | Native AOT CLI binary |
| `just publish-gui-linux` | self-contained Linux app with its launchers |
| `just publish-gui-windows` | self-contained Windows app |
| `just package` | all three, archived exactly as the release job uploads them |
| `just bless` | re-record the golden export fixture |
| `just set-version 0.6.1` / `just tag 0.6.1` | bump the version everywhere / tag a release |
| `just aur 0.6.1` / `just aur-check` | point the PKGBUILDs at a release / build them with makepkg |
| `just ci` | lint + build + test + package, what CI runs |
| `just clean` | drop `bin/`, `obj/`, and `dist/` |

Plain SDK commands work too:

```sh
dotnet build
dotnet test
dotnet run --project src/Sleepy.Cli -- openapi.json
dotnet publish src/Sleepy.Cli/Sleepy.Cli.csproj -c Release -r linux-x64 -o dist/cli
```

The CLI release build uses **Native AOT**, which links with `clang` and needs the
zlib headers (`apt-get install clang zlib1g-dev` on Debian/Ubuntu). Swap
`-r linux-x64` for `win-x64`, `osx-arm64`, etc. — Native AOT must be built on the
target OS. The desktop app has no such restriction and cross-publishes from
Linux to Windows.

CI (`.gitlab-ci.yml`) runs lint and the tests on every push. The three publish
jobs and the release itself only run for a tag, so branch pipelines stay quick
and the binaries are built once, for the release that ships them.

### Tests

`tests/Sleepy.Core.Tests` covers the engine: slugs, the deterministic JSON
writer, JSON/YAML parsing, schema example generation (`$ref`, cycles, `allOf`,
`oneOf`, formats, the depth cap), the Insomnia resource shapes for every folder
layout, and module discovery against a loopback Swagger UI (Swashbuckle,
springdoc, springfox, and manifests).

A golden test converts `Fixtures/openapi.json`, compares it byte for byte with
the recorded export, and asserts the YAML twin produces the same thing. When the
output changes on purpose, re-record it with `just bless` and read the diff.

### Layout

| Path | Purpose |
| --- | --- |
| `src/Sleepy.Core/` | the vocabulary every library speaks in |
| `src/Sleepy.Core/Serialization/` | JSON and YAML in, one tree out; the deterministic writer |
| `src/Sleepy.Core/Discovery/` | where documents come from: files, URLs, Swagger UIs |
| `src/Sleepy.Core/OpenApi/` | what a specification means: operations, examples, the plan |
| `src/Sleepy.Core/Builders/` | what a collection looks like, one class per format |
| `src/Sleepy.Cli/` | the `sleepy` command line tool |
| `src/Sleepy.Gui/` | the `sleepy-GUI` Uno Platform desktop app |
| `src/Sleepy.Gui/ViewModels/MainViewModel.cs` | load → options → convert → save |
| `src/Sleepy.Gui/Platforms/Desktop/Program.cs` | desktop host selection (`--display`) |
| `src/Sleepy.Gui/Strings/` | English and Czech resources |
| `packaging/linux/` | X11 / Wayland / console launchers, desktop entries, installer |
| `packaging/aur/` | PKGBUILDs for `sleepy-bin` and `sleepy-gui-bin`, and how to publish them |
| `tests/Sleepy.Core.Tests/` | engine tests, including the golden export fixture |
| `icon.png` | app artwork; Uno.Resizetizer generates every icon size from it |
| `justfile`, `mise.toml` | task runner and pinned toolchain |

## Scope

`sleepy` targets **OpenAPI 3** documents in JSON or YAML; trailing commas and
comments in JSON input are tolerated.

Only local `$ref`s (`#/...`) are resolved — remote and file references are not
followed. OpenAPI `servers` entries are ignored: a request URL is
`{{ _.baseUrl }}` plus the raw path from the spec.

## Contributing

Bug reports are most useful with the specification that triggered them; see
[CONTRIBUTING.md](CONTRIBUTING.md) for the development loop, the golden-fixture
workflow, and how to add a translation. Release notes live in
[CHANGELOG.md](CHANGELOG.md).

## Licence

[BSD 3-Clause](LICENSE) — Copyright (c) 2026 Martin Kopecký.

The licence and the [third-party notices](THIRD-PARTY-NOTICES.md) are embedded
in the binaries rather than shipped beside them: `sleepy --license` prints both,
and sleepy-GUI shows them under **About** in the status bar.
