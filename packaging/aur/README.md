# AUR packages

Two packages, both built from the released binaries rather than from source:

| Package | What it installs |
| --- | --- |
| `sleepy-bin` | the command line tool at `/usr/bin/sleepy` |
| `sleepy-gui-bin` | the desktop app under `/usr/lib/sleepy-gui`, with launchers, desktop entries and icon |

The PKGBUILDs here are the source of truth. Publishing copies them into the AUR
repositories, fills in the version and checksums from a release, and pushes.

## How the AUR works

Every AUR package is its own tiny git repository at
`ssh://aur@aur.archlinux.org/<pkgname>.git`, holding only a `PKGBUILD` and a
`.SRCINFO` (the machine-readable summary the site indexes). Pushing to a
repository that does not exist yet is what creates the package — there is no
"new package" button. Authentication is SSH only.

Two rules the AUR enforces, both easy to trip over:

- `.SRCINFO` must be committed and must match the `PKGBUILD`. It is generated:
  `makepkg --printsrcinfo > .SRCINFO`.
- The repository holds packaging only. No binaries, no sources.

## First-time setup

1. Register at https://aur.archlinux.org/register — it needs a username, an
   email, and an SSH public key.

2. Make a key for it if you do not want to reuse an existing one:

   ```sh
   ssh-keygen -t ed25519 -f ~/.ssh/aur -C "aur"
   cat ~/.ssh/aur.pub          # paste this into your AUR account page
   ```

3. Tell SSH to use it for the AUR, in `~/.ssh/config`:

   ```
   Host aur.archlinux.org
     User aur
     IdentityFile ~/.ssh/aur
   ```

4. Check it: `ssh aur@aur.archlinux.org help` should greet you by username.

## Publishing

With that in place, from the repository root:

```sh
just aur-check              # build both packages with makepkg first
just aur-publish 0.6.1      # clone, update, regenerate .SRCINFO, push
```

The first run creates both packages, because pushing to an empty repository is
what registers a name. Later runs update them. If a name is already taken by
someone else the push is rejected, and the package needs renaming here first.

`just aur 0.6.1` does the version and checksum part alone, without pushing, if
you want to inspect the diff before it goes out.

The PKGBUILDs download from the GitHub release for the version, so the release
has to exist — Jenkins creates it when it builds the tag — before
`just aur-publish` can take checksums from it.

## From Jenkins

A tag build runs the same `just aur-publish` in its `AUR` stage, right after
the GitHub release it takes checksums from. It needs:

- the **SSH Agent** plugin, and an *SSH Username with private key* credential
  with id `aur-ssh` holding the key registered with the AUR account. The key's
  passphrase goes in the credential, so the key is never stored unlocked —
  though anyone who can use that credential can publish as you, so keep the job
  restricted;
- **makepkg** on the agent, or **Docker** where there is none: `.SRCINFO` is then
  written by `makepkg` in an `archlinux:base-devel` container, from a copy of
  the PKGBUILD. Git and SSH stay on the host, so no Docker plugin is involved —
  the `jenkins` user just has to be able to run `docker`.

Running `just aur-publish` by hand still works, and is the fallback when the
stage fails: it is safe to run again, and reports a package already at the
version instead of pushing twice.

## Maintaining them

- A package that lags its upstream is worse than no package, so publish each
  release — the Jenkins stage does it, `just aur-publish` when it cannot.
- `pkgrel` goes up when the packaging changes but the version does not; `just
  aur` resets it to 1 for a new version.
- Comments on the AUR page are where users report packaging problems, and they
  are not visible from anywhere else. Watch for them after the first release.
