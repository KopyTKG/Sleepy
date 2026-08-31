set shell := ["bash", "-euo", "pipefail", "-c"]
set dotenv-load := false

# Where publish output lands. Override with `just dist=/tmp/out publish-all`.
dist := "dist"
configuration := "Release"

cli := "src/Sleepy.Cli/Sleepy.Cli.csproj"
gui := "src/Sleepy.Gui/Sleepy.Gui.csproj"
core := "src/Sleepy.Core/Sleepy.Core.csproj"
tests := "tests/Sleepy.Core.Tests/Sleepy.Core.Tests.csproj"
desktop_tfm := "net10.0-desktop"

# List the available recipes.
default:
    @just --list --unsorted

# --------------------------------------------------------------------------
# Toolchain
# --------------------------------------------------------------------------

# Install the pinned toolchain (dotnet, just) with mise.
tools:
    mise install

# Restore NuGet packages.
restore:
    dotnet restore

# --------------------------------------------------------------------------
# Build & test
# --------------------------------------------------------------------------

# Build the whole solution.
build:
    dotnet build -c {{configuration}}

# Build just the CLI and its engine (fast loop; skips the Uno app).
build-cli:
    dotnet build {{cli}} -c {{configuration}}

# Build the desktop app for this machine.
build-gui:
    dotnet build {{gui}} -c {{configuration}} -f {{desktop_tfm}}

# Run the test suite.
test *args:
    dotnet test {{tests}} -c {{configuration}} --nologo {{args}}

# Run the tests and write a Cobertura coverage report under dist/coverage.
coverage:
    dotnet test {{tests}} -c {{configuration}} --nologo \
        --collect:"XPlat Code Coverage" --results-directory {{dist}}/coverage
    @echo "coverage written to {{dist}}/coverage"

# Re-record the golden export fixture after a deliberate output change.
bless:
    dotnet run --project {{cli}} -c {{configuration}} -- \
        tests/Sleepy.Core.Tests/Fixtures/openapi.json \
        | sed 's|"__export_source": ".*"|"__export_source": "sleepy:fixture"|' \
        > tests/Sleepy.Core.Tests/Fixtures/expected-insomnia.json
    @echo "fixture re-recorded — review the diff before committing"

# Fail if anything is formatted off-style.
lint:
    dotnet format --verify-no-changes --no-restore

# Apply formatting fixes.
fmt:
    dotnet format --no-restore

# --------------------------------------------------------------------------
# Run
# --------------------------------------------------------------------------

# Run the CLI, e.g. `just run openapi.json -o insomnia.json`.
run *args:
    dotnet run --project {{cli}} -c {{configuration}} -- {{args}}

# Run the desktop app. Pass --display x11|framebuffer to force a host.
gui *args:
    dotnet run --project {{gui}} -c {{configuration}} -f {{desktop_tfm}} -- {{args}}

# --------------------------------------------------------------------------
# Publish
# --------------------------------------------------------------------------

# Native AOT CLI for this machine's OS (needs clang + zlib headers).
publish-cli rid="linux-x64":
    dotnet publish {{cli}} -c {{configuration}} -r {{rid}} -o {{dist}}/cli-{{rid}}
    @echo "-> {{dist}}/cli-{{rid}}/sleepy"

# Self-contained desktop app for Linux, with the X11/Wayland/console launchers.
publish-gui-linux rid="linux-x64":
    dotnet publish {{gui}} -c {{configuration}} -f {{desktop_tfm}} -r {{rid}} \
        --self-contained -o {{dist}}/gui-{{rid}}
    @echo "-> {{dist}}/gui-{{rid}}/sleepy-gui (launchers: -x11, -wayland, -console)"

# Self-contained desktop app for Windows; cross-publishes from Linux.
publish-gui-windows rid="win-x64":
    dotnet publish {{gui}} -c {{configuration}} -f {{desktop_tfm}} -r {{rid}} \
        --self-contained -o {{dist}}/gui-{{rid}}
    @echo "-> {{dist}}/gui-{{rid}}/sleepy-gui.exe"

# The WinAppSDK head. Windows only — needs the Windows SDK.
publish-gui-winappsdk rid="win-x64":
    dotnet publish {{gui}} -c {{configuration}} -f net10.0-windows10.0.26100 -r {{rid}} \
        -o {{dist}}/gui-winappsdk-{{rid}}

# Everything a release ships.
publish-all: publish-cli publish-gui-linux publish-gui-windows

# Pack the published output into the archives the release job uploads.
package: publish-all
    cd {{dist}}/gui-linux-x64 && tar -czf ../sleepy-gui-linux-x64.tar.gz .
    cd {{dist}}/gui-win-x64 && zip -qr ../sleepy-gui-win-x64.zip .
    cp {{dist}}/cli-linux-x64/sleepy {{dist}}/sleepy
    @ls -lh {{dist}}/sleepy {{dist}}/sleepy-gui-linux-x64.tar.gz {{dist}}/sleepy-gui-win-x64.zip

# Install the desktop app and its launchers for the current user.
install-gui: publish-gui-linux
    {{dist}}/gui-linux-x64/install.sh

# --------------------------------------------------------------------------
# Release plumbing
# --------------------------------------------------------------------------

# Set the version. Directory.Build.props is the only place it is written; the
# apps read it back from the assembly.
set-version version:
    sed -i 's|<Version>.*</Version>|<Version>{{version}}</Version>|' Directory.Build.props
    @grep -n "<Version>" Directory.Build.props

# Show what a built binary reports about itself.
version:
    @dotnet run --project {{cli}} -c {{configuration}} -- --version

# Point the AUR packages at a released version, checksums and all.
aur version:
    #!/usr/bin/env bash
    set -euo pipefail
    registry="https://gitlab.com/api/v4/projects/84670021/packages/generic/sleepy/v{{version}}"
    work=$(mktemp -d)
    trap 'rm -rf "$work"' EXIT

    curl --fail --silent --show-error --location -o "$work/sleepy" "$registry/sleepy"
    curl --fail --silent --show-error --location -o "$work/gui.tar.gz" "$registry/sleepy-gui-linux-x64.tar.gz"
    curl --fail --silent --show-error --location -o "$work/LICENSE"         "https://gitlab.com/kopytkg/sleepy/-/raw/v{{version}}/LICENSE"

    cli=$(sha256sum "$work/sleepy" | cut -d' ' -f1)
    gui=$(sha256sum "$work/gui.tar.gz" | cut -d' ' -f1)
    licence=$(sha256sum "$work/LICENSE" | cut -d' ' -f1)

    for package in sleepy-bin sleepy-gui-bin; do
        file="packaging/aur/$package/PKGBUILD"
        sed -i "s|^pkgver=.*|pkgver={{version}}|; s|^pkgrel=.*|pkgrel=1|" "$file"
        if [ "$package" = "sleepy-bin" ]; then asset="$cli"; else asset="$gui"; fi
        sed -i "0,/^sha256sums=(/{s|^sha256sums=('.*'|sha256sums=('$asset'|}" "$file"
        sed -i "s|^            '.*')|            '$licence')|" "$file"
        echo "$file -> {{version}}"
    done

# Push the AUR packages from this machine. Needs an AUR account whose SSH key
# is registered; see packaging/aur/README.md.
aur-publish version:
    #!/usr/bin/env bash
    set -euo pipefail
    just aur {{version}}

    work=$(mktemp -d)
    trap 'rm -rf "$work"' EXIT

    for package in sleepy-bin sleepy-gui-bin; do
        echo "==> $package"
        git clone --quiet "ssh://aur@aur.archlinux.org/$package.git" "$work/$package"
        cp "packaging/aur/$package/PKGBUILD" "$work/$package/PKGBUILD"

        (
            cd "$work/$package"
            makepkg --printsrcinfo > .SRCINFO
            git add PKGBUILD .SRCINFO
            if git diff --cached --quiet; then
                echo "    already at {{version}}"
            else
                git commit -qm "Update to {{version}}"
                git push --quiet
                echo "    pushed {{version}}"
            fi
        )
    done

# Build the AUR packages locally, to check them before they reach anyone.
aur-check:
    #!/usr/bin/env bash
    set -euo pipefail
    for package in sleepy-bin sleepy-gui-bin; do
        work=$(mktemp -d)
        cp "packaging/aur/$package/PKGBUILD" "$work/"
        (cd "$work" && makepkg --nodeps --clean)
        echo "built: $(ls "$work"/*.pkg.tar.zst)"
        rm -rf "$work"
    done

# Tag the current commit; CI builds and publishes the release from the tag.
tag version:
    @test -z "$(git status --porcelain)" || { echo "working tree is dirty"; exit 1; }
    git tag -a "v{{version}}" -m "sleepy v{{version}}"
    @echo "tagged v{{version}} — push it with: git push origin v{{version}}"

# What CI runs.
ci: lint build test package

# Remove build and publish output.
clean:
    dotnet clean -c {{configuration}} || true
    rm -rf {{dist}}
    find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
