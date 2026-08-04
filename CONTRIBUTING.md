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

This hook is the only real gate. CI's `validate-commits` step runs `cog check`
too, but falls back to an `echo` on any failure so the step always exits 0 —
it cannot fail the pipeline. A non-conforming commit will not be caught by CI;
skip `cog install-hook --all` and a bad message reaches `main` unopposed.

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

The conformance suite runs 10,220,000 SingleStepTests vectors across four cores,
plus Klaus Dormann's functional, interrupt and 65C02 extended tests. Two things
to know before the first run:

**It downloads roughly 3.8 GB of vectors.** One set per core, fetched on first
use and cached under `tests/SixtyFiveXX.Conformance/.harte-cache/`. They are
never committed. If you already have a clone of
[SingleStepTests/65x02](https://github.com/SingleStepTests/65x02), point
`SIXTYFIVEXX_HARTE_DIR` at it and nothing is downloaded at all:

    export SIXTYFIVEXX_HARTE_DIR=/path/to/65x02

**It fetches two much smaller things over HTTPS**, cached the same way and with
the same escape hatch: Klaus Dormann's prebuilt binaries from GitHub
(`SIXTYFIVEXX_KLAUS_DIR`), and VICE's `testprogs/CPU/cpuport/test1.prg` — 138
bytes, and the only independent oracle the 6510's on-chip port has — from the
VICE project's Subversion repository on SourceForge (`SIXTYFIVEXX_VICE_DIR`,
pointing at a `testprogs` checkout). VICE's `testprogs/` is deliberately not
taken from the `VICE-Team/svn-mirror` GitHub mirror, which does not contain it.

**It needs `64tass`**, for two things. It assembles the interrupt test binary
*before* anything runs it — including the solution-wide test command below, so
skipping this fails the first test command outright on a fresh clone. It is also
invoked *during* the run, by the disassembler round-trip gate, which reassembles
the disassembly of every opcode and requires the original bytes back. Both need
it on `PATH`:

    brew install 64tass          # or: apt-get install 64tass
    tests/SixtyFiveXX.Conformance/klaus/build.sh
    dotnet test tests/SixtyFiveXX.Conformance -c Release

Once that binary exists:

    dotnet build -c Release
    dotnet test -c Release --filter "Category!=Performance"

The conformance run takes roughly two to three minutes per target framework with
the vectors already cached, and the two frameworks overlap. Nearly all of that is
the vector comparison itself; the unit suite is under a second.

`tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` is a byte-faithful
port of Klaus Dormann's GPL-3.0 test, verified character-for-character against
upstream. Only assembler directives differ. **Do not edit it.**

## Releasing

**Any push to `main` without `[skip ci]` in the commit *subject* cuts a public
release.** There is no separate release command and no manual approval step, so
the marker is the only thing standing between a merge and nuget.org. Merge
commits need it in the subject line, not the body.

The pipeline runs on Woodpecker at `ci.barrywalker.io`. It expands
`.woodpecker/woodpecker-template.yaml` — a data block, not a pipeline — through
the shared `release-dotnet-library` template, then runs: commit validation,
build, the unit suite, conformance, and finally the release. Cocogitto derives
the version from the Conventional Commit history, stamps it into
`Directory.Build.props` via `scripts/stamp-version.sh`, tags it `vX.Y.Z`, and
pushes a bump commit carrying `[skip ci]` so the release does not loop. The
package is then packed **from the tag** and pushed to nuget.org.

To see what the next version would be without cutting it:

    cog bump --auto --dry-run

Two things bite:

- **nuget.org is append-only.** A published version can be unlisted but never
  deleted or replaced, so package metadata in `src/SixtyFiveXX/SixtyFiveXX.csproj`
  — description above all — is worth reading before a release rather than after.
- **A pipeline that fails before its config is stored cannot be restarted.**
  Woodpecker replays a restart from the stored definition, and a run that errored
  during validation never persisted one, so it fails with "pipeline definition
  not found" no matter how many times it is retried. Push again instead.

The conformance step re-downloads the vectors on every run until the
`harte-cache` PVC exists; the keys for it are already in the template and are
inert without it.
