# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] — 2026-08-19

Rewritten from Odin to C#. Single-spec output is byte-identical to 0.2.0.

### Added

- **Multi-module APIs.** A URL may point at a Swagger UI serving several
  definitions rather than at one document. The module list is read from
  Swashbuckle's `swagger-ui-init.js`, springdoc's `swagger-config`, springfox's
  `swagger-resources`, or a hand-written manifest, and every module is merged
  into one collection with module-prefixed resource ids.
- **YAML input** alongside JSON, detected from the content and file extension.
- **`sleepy-GUI`**, a desktop app (Uno Platform) for Linux and Windows: drop a
  spec anywhere in the window, tune the output, watch the preview, save. English
  and Czech translations with correct Czech plural forms.
- Linux launchers for X11, Wayland (via XWayland) and the framebuffer, desktop
  entries, and an installer; the host can be forced with `--display`.
- New CLI options: `--module`, `--list`, `--flat`, `--no-tags`, `--bearer`,
  `--name`, `--header`, `--insecure`, `--license`; several inputs at once.
- Test suite covering the engine, including a golden export fixture that locks
  the output format down byte for byte.
- `mise.toml` and a `justfile` covering build, test, publish, and release.
- BSD 3-Clause licence, embedded in both binaries with the third-party notices.

### Changed

- The CLI fetches over HTTP directly; `curl` is no longer required.
- JSON parse errors report what the parser actually found, instead of an
  internal error name.
- CI gained lint and test stages and publishes the CLI plus both desktop builds.

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

[Unreleased]: https://gitlab.com/kopytkg/sleepy/-/compare/v0.5.0...main
[0.5.0]: https://gitlab.com/kopytkg/sleepy/-/releases/v0.5.0
[0.2.0]: https://gitlab.com/kopytkg/sleepy/-/releases/v0.2.0
[0.1.0]: https://gitlab.com/kopytkg/sleepy/-/releases/v0.1.0
