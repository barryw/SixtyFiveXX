# Contributing to SixtyFiveXX

## Commit messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/).
The message decides the next version, so it is part of the change, not
paperwork.

| Prefix | Effect on the version |
| --- | --- |
| `feat:` | minor bump |
| `fix:` | patch bump |
| `feat!:` or a `BREAKING CHANGE:` footer | major bump |
| `docs:` `test:` `refactor:` `perf:` `build:` `ci:` `style:` `chore:` `revert:` | no bump |

Everything except `chore` appears in the release notes. `chore` is omitted
entirely (`cog.toml` sets `omit_from_changelog = true` for it) so cog's own
version-bump commit — `chore(version): vX.Y.Z [skip ci]` — doesn't clutter
every release with a "Miscellaneous" entry nobody needs. `style` is not
suppressed and does show up.

Install the hook that rejects a non-conforming message before it is written —
git hooks do not survive a clone, so this is per-machine:

    cog install-hook --all

CI runs `cog check` on every push, so a bad message cannot reach `main`.

## Versioning

Versions are derived from the commits by [cocogitto](https://docs.cocogitto.io/),
never set by hand. On a merge to `main` containing a `feat:` or `fix:`, CI stamps
`Directory.Build.props`, tags the commit, cuts a GitHub Release, and publishes to
nuget.org.

`AssemblyVersion` is pinned to the range within which binary compatibility holds
(`major.minor` while `0.x`, `major` from 1.0) so consumers do not need a binding
redirect for a compatible release. `scripts/stamp-version.sh` implements that;
`scripts/test-stamp-version.sh` tests it.

## Before you push

The conformance suite runs 2,560,000 SingleStepTests vectors plus Klaus
Dormann's functional and interrupt tests. It needs `64tass` to assemble the
interrupt test binary *before* anything runs it — including the solution-wide
test command below. Skip this and the first test command fails outright on a
fresh clone:

    brew install 64tass          # or: apt-get install 64tass
    tests/SixtyFiveXX.Conformance/klaus/build.sh
    dotnet test tests/SixtyFiveXX.Conformance -c Release

Once that binary exists:

    dotnet build -c Release
    dotnet test -c Release --filter "Category!=Performance"

`tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` is a byte-faithful
port of Klaus Dormann's GPL-3.0 test, verified character-for-character against
upstream. Only assembler directives differ. **Do not edit it.**
