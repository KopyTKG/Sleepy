# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- The project moved to GitHub (`KopyTKG/Sleepy`): issue and pull request
  templates are GitHub's, CI runs on Jenkins (`Jenkinsfile`), and releases are
  GitHub releases. The AUR packages download from there from the next release.

### Added

- **Interface scale** for the desktop app (#1): a picker in the status bar sets
  100–250 % or follows the display, is saved with the other preferences, and
  offers to restart since the display host reads it at startup.
  `sleepy-gui --scale 150%` overrides it for one run. Linux only; Windows
  follows its own display scaling.

## [0.6.1] — 2026-08-31

### Added

- **AUR packages**: `sleepy-bin` and `sleepy-gui-bin`, built from the released
  assets. A tag pipeline updates them when an AUR SSH key is configured;
  `just aur <version>` does the same locally, and `just aur-check` builds both
  with makepkg before anyone else sees them.

### Changed

- The desktop app's text fields — workspace name, base URL, bearer token — are
  applied with a button or Enter rather than on every keystroke. Rebuilding the
  collection and re-rendering a syntax-highlighted preview between two letters of
  a name made typing lag. Saving or copying with an unapplied edit asks what to
  do rather than exporting something that disagrees with the fields on screen.

### Fixed

- The desktop launchers resolve symlinks, so a package can put a link in
  `/usr/bin` while the app lives elsewhere.

## [0.6.0] — 2026-08-24

### Added

- **The desktop app remembers its choices** in
  `~/.config/sleepy/settings.json` (`%AppData%\sleepy` on Windows): language,
  output format, folder layout, request header, and the TLS setting. The bearer
  token and base URL are deliberately not saved — both are per-API and one is a
  credential. A missing or malformed file means defaults.
- **Syntax highlighting and collapsible blocks in the desktop preview.** A
  lenient tokenizer in `Sleepy.Serialization` marks property names, strings,
  numbers, keywords and punctuation, and an outline pairs each line that opens a
  block with the line that closes it. Anything not recognised stays plain and
  folds nothing, so a Bruno file's block syntax reads as well as a JSON export.
  Folding a block shows how many lines it hides, and folds nest: opening an outer
  block leaves the ones folded inside it folded.

- **Postman v2.1 and Bruno output**, chosen with `--format postman` or
  `--format bruno` on the command line, or from the format picker in the desktop
  app. Bruno is a directory of `.bru` files, so `-o` names a folder for it and
  the app asks for one; its preview shows the file tree it will write.
- The engine split into five libraries — `Sleepy.Core` (vocabulary),
  `Sleepy.Serialization`, `Sleepy.Discovery`, `Sleepy.OpenApi` and
  `Sleepy.Builders` — so the dependency direction is enforced by the compiler.
  Each holds one type per file, and `src/Sleepy.Core/README.md` says what belongs
  where.
- `Sleepy.Builders`, a library nested under Core holding one class per format
  behind an `ICollectionBuilder` seam, so a new target is an implementation plus
  a registration. The decisions every format shares — collection name, which
  modules contribute, base-URL variables — moved into `CollectionPlan` in Core,
  so all three formats describe the same API.

## [0.5.0] — 2026-08-21

Rewritten from Odin to C#. Single-spec output is byte-identical to 0.2.0.

### Added

- **Multi-module APIs.** A URL may point at a Swagger UI serving several
  definitions rather than at one document. The module list is found by following
  the page's own script tags, or read from springdoc's `swagger-config`,
  springfox's `swagger-resources`, or a hand-written manifest; every module is
  then merged into one collection with module-prefixed resource ids. Modules
  that declare no paths are left out rather than contributing an empty folder,
  and a URL that serves nothing falls through to probing, so a bare host works.
- **YAML input** alongside JSON, detected from the content and file extension.
- **`sleepy-GUI`**, a desktop app (Uno Platform) for Linux and Windows: drop a
  spec anywhere in the window, tune the output, watch the preview, save. English
  and Czech translations with correct Czech plural forms.
- Linux launchers for X11, Wayland (via XWayland) and the framebuffer, desktop
  entries, and an installer; the host can be forced with `--display`.
- New CLI options: `--module`, `--list`, `--flat`, `--no-tags`, `--bearer`,
  `--name`, `--header`, `--insecure`, `--license`; several inputs at once.
- Build identity: the version is written once, in `Directory.Build.props`, and
  the commit is stamped into it, so `sleepy --version`, the GUI status bar, and
  the About dialog all report something like `0.5.0+c5fac9f` — traceable back to
  the source it was built from.
- Test suite covering the engine, including a golden export fixture that locks
  the output format down byte for byte.
- `mise.toml` and a `justfile` covering build, test, publish, and release.
- BSD 3-Clause licence, embedded in both binaries with the third-party notices.

### Changed

- The CLI fetches over HTTP directly; `curl` is no longer required.
- JSON parse errors report what the parser actually found, instead of an
  internal error name.
- CI gained lint and test stages; the CLI and both desktop builds are published
  from tags, which is also when the release is cut.

### Removed

- The Odin implementation (`main.odin`) and its toolchain.

## [0.2.0] — 2026-07-22

### Added

- Fetch a specification by URL and prefill `baseUrl` with that URL's origin.
- `--base-url` to override the derived value.

## [0.1.0] — 2026-07-21

### Added

- Initial release: convert an OpenAPI 3 JSON document into an Insomnia v4
  export, with tag folders, bearer authentication, and generated request-body
  examples.

[Unreleased]: https://github.com/KopyTKG/Sleepy/compare/v0.6.1...main
[0.6.1]: https://github.com/KopyTKG/Sleepy/releases/tag/v0.6.1
[0.6.0]: https://github.com/KopyTKG/Sleepy/releases/tag/v0.6.0
[0.5.0]: https://github.com/KopyTKG/Sleepy/releases/tag/v0.5.0
[0.2.0]: https://gitlab.com/kopytkg/sleepy/-/releases/v0.2.0
[0.1.0]: https://gitlab.com/kopytkg/sleepy/-/releases/v0.1.0
