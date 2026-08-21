# Contributing to sleepy

Thanks for taking a look. Issues and merge requests are welcome — including
"this spec produced something odd", which is the most useful kind of report this
project gets.

## Getting set up

```sh
git clone git@gitlab.com:kopytkg/sleepy.git
cd sleepy
mise install     # .NET 10 SDK + just, as pinned in mise.toml
just             # every task lives here
```

Without [mise](https://mise.jdx.dev/) you need the .NET 10 SDK on `PATH`; without
[just](https://just.systems/) the plain `dotnet build` / `dotnet test` commands
work, they are just longer. The Uno SDK version is pinned in `global.json` and
restores from NuGet.

The usual loop:

```sh
just build-cli                     # engine + CLI only, the fast one
just test                          # the whole suite, about a second
just run tests/Sleepy.Core.Tests/Fixtures/openapi.json
just gui                           # the desktop app
```

Before opening a merge request:

```sh
just lint                          # formatting, same check CI runs
just test
```

`just fmt` applies the formatting fixes.

## Reporting a bug

Include the specification that triggered it, or a trimmed-down version. A spec
that reproduces the problem turns a report into a fix; a description of the
symptom usually turns into a round of questions. Mention which app (`sleepy` or
`sleepy-GUI`), which platform, and the version (`sleepy --version`).

If the specification is not public, a reduced fixture with the shape preserved
and the names changed is perfectly fine.

## Working on the code

**Layout.** `src/Sleepy.Core` is the engine — parsing, discovery, example
generation, the export writer. `src/Sleepy.Cli` and `src/Sleepy.Gui` are thin
shells over it. Anything that changes the *output* belongs in Core, where the
tests can reach it.

**Tests.** `tests/Sleepy.Core.Tests` covers the engine directly, plus a golden
test that converts a fixture and compares it byte for byte with a recorded
export. New behaviour wants a test next to the ones it resembles.

**Changing the output on purpose** means the golden test fails. Re-record it and
read the diff carefully — it is the review of your change:

```sh
just bless
git diff tests/Sleepy.Core.Tests/Fixtures/expected-insomnia.json
```

**Style.** `dotnet format` decides layout, so run `just fmt` and stop thinking
about it. Beyond that: comments explain *why*, names carry the meaning, and code
should look like the file it lives in.

**Determinism is a feature.** Object keys are written in ordinal order, paths are
processed sorted, and dates are fixed placeholders — so converting the same spec
twice gives identical bytes and diffs stay readable. Do not introduce
`DateTime.Now`, dictionary iteration order, or anything else that varies between
runs.

**Native AOT.** The CLI publishes with Native AOT, so the engine must stay free
of reflection-based serialization and runtime code generation. This is why YAML
is parsed through YamlDotNet's representation model rather than by deserializing
into types.

### Translations

The desktop app ships English and Czech. Strings live in
`src/Sleepy.Gui/Strings/<language>/Resources.resw`: XAML text is keyed by
`x:Uid` (`SaveButton.Content`), runtime messages go through
`Services/Localizer.cs`.

To add a language: copy the `en` folder, translate the values, add the tag to
`Localizer.SupportedLanguages`, and add an item to the picker in
`MainPage.xaml`. Counted phrases use `Count_*.One` / `.Few` / `.Many` keys —
languages that need only two forms can leave `.Few` out.

## Merge requests

- Branch off `main`; one topic per merge request.
- Commit messages: a short imperative summary line, then a body explaining why
  the change is right if that is not obvious from the diff. Bullet lists of what
  changed are less useful than one paragraph of reasoning.
- Update `CHANGELOG.md` under `## [Unreleased]` for anything a user would
  notice. Internal refactors do not need an entry.
- CI runs lint and the tests on every push; it must be green. The publish jobs
  run only for tags, so a branch pipeline is quick.

## Releasing

Maintainers only:

```sh
just set-version 0.6.0             # Directory.Build.props, SleepyInfo, the app manifest
# move the Unreleased entries in CHANGELOG.md under the new version
git commit -am "Release 0.6.0"
just tag 0.6.0
git push origin main --follow-tags
```

The tag triggers the release job, which uploads the CLI binary and both desktop
archives to the package registry and creates the GitLab release.

## Licence

sleepy is BSD 3-Clause. Contributions are accepted under the same licence; the
copyright stays with their authors.
