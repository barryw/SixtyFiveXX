# SixtyFiveXX — Woodpecker CI, cog versioning, and releasing

**Goal:** A Woodpecker pipeline that runs against `barryw/SixtyFiveXX` in a container that
already carries its toolchain; Conventional Commits enforced as a gate; versions derived
by `cog` from the commits themselves; and every merge to `main` that contains something
releasable producing a NuGet package and a GitHub Release with real notes.

**Status:** design approved 2026-08-01.

## Established facts — verified before this spec was written, do not re-derive

**Toolchain and code**

- **The core compiles clean on `net8.0`.** Proven by building `src/SixtyFiveXX` with
  `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` under `TreatWarningsAsErrors`:
  both TFMs produced assemblies, zero warnings, **no `#if` required**.
- **`SixtyFiveXX` is unclaimed on nuget.org** (HTTP 404 on both the flat-container and
  registration endpoints).
- **`IsPackable=false` is already set** on both test projects and the benchmarks. Only
  `src/SixtyFiveXX` packs.
- **`Directory.Build.props` has the wrong repository URL** — `barrywalker`, not `barryw`.
- **`Directory.Build.props` sets singular `<TargetFramework>` for every project**, which
  will collide with the library's plural `<TargetFrameworks>`. All three non-library
  projects already set it themselves, so the fix is deletion, not conditions.
- **`apt-get install -y --no-install-recommends 64tass` works** in
  `mcr.microsoft.com/dotnet/sdk:10.0` — verified end-to-end in Docker during Phase 2b.
- **The conformance suite needs a binary built on demand.** Klaus's interrupt test is
  source-only upstream; `klaus/build.sh` assembles it and requires 64tass.
- **The performance gate is machine-dependent** — 6.3 MHz under load average 37, passing
  in 373 ms once load fell.
- **Woodpecker is self-hosted and already runs other GitHub repositories.**

**cog behaviour — probed against the installed `cog 7.0.0`, not assumed**

- **Every commit in the project is already conventional.** The only `cog check` failures
  are the three merge commits, which `ignore_merge_commits` resolves.
- **`cog bump --auto` exits 0 when nothing is releasable.** It prints "No conventional
  commits for your repository that required a bump" and creates no tag. It cannot be used
  as a pass/fail signal — a no-op looks exactly like success, so a naive pipeline would
  proceed to republish the previous version.
- **cog tags without a `v` prefix by default** (`0.1.0`, not `v0.1.0`). `tag_prefix` in
  `cog.toml` changes this.
- **`cog changelog --at <tag>` emits exactly that version's section**, suitable as
  `gh release create --notes-file` input. The tag argument must match the actual tag
  name, prefix included.
- **cog's default changelog already spans more than feat/fix/perf** — it emits
  Documentation and Miscellaneous Chores sections too. It also includes its own
  `chore(version)` bump commit, which is noise that must be suppressed.
- **`cog get-version --tag`** prints the full current tag, and `--fallback` supplies a
  value when no tag exists.
- **First `cog bump --auto` on this history would yield `0.1.0`**, not `0.2.0`, because
  cog computes from `0.0.0` and `feat` maps to a minor bump under 0.x.

## Architecture

| Piece | Artifact | Trigger |
| --- | --- | --- |
| CI image | `docker/ci.Dockerfile` + `.woodpecker/image.yml` → `ghcr.io/barryw/sixtyfivexx-ci:1` | changes to `docker/ci.Dockerfile` |
| Commit + version config | `cog.toml` | — |
| Packaging metadata | `Directory.Build.props`, `src/SixtyFiveXX/SixtyFiveXX.csproj` | — |
| Build pipeline | `.woodpecker/build.yml` | pull_request, manual, push to non-`main` |
| Release pipeline | `.woodpecker/release.yml` | push to `main` |
| Release logic | `scripts/release.sh` | invoked by the release pipeline |

Woodpecker reads each file in `.woodpecker/` as its own pipeline. The existing single
`.woodpecker.yml` is replaced by that directory.

**Gates run exactly once per event.** `build.yml` excludes pushes to `main`; `release.yml`
covers those and runs the same gates itself before releasing. A tag push triggers nothing
— the tag is a record of what shipped, not a trigger.

## 1. The CI image

`docker/ci.Dockerfile`, from `mcr.microsoft.com/dotnet/sdk:10.0`, adding:

- **64tass and git** from apt.
- **The .NET 8 runtime.** The SDK 10 image builds `net8.0` but cannot run it, so
  `dotnet test` against that TFM would fail. Installed via `dotnet-install.sh --channel 8.0
  --runtime dotnet`. Multi-targeting without executing the net8.0 tests would publish a
  target nothing verifies.
- **cog**, from the cocogitto GitHub release tarball, pinned to the version this design was
  probed against (7.0.0).
- **The `gh` CLI**, for creating GitHub Releases.

The Dockerfile ends with a verification layer — `64tass --version`, `cog --version`,
`gh --version`, and `dotnet --list-runtimes` showing both 8 and 10 — so a broken image
fails at build time rather than confusingly mid-pipeline.

Published to GHCR, tagged `:1` — a fixed major tag bumped by hand when the toolchain
intentionally changes. Pipelines pin it so an image rebuild cannot silently alter the
toolchain under a green build. `.woodpecker/image.yml` builds it, gated on
`path: docker/ci.Dockerfile`.

**The GHCR package must be made public once, by hand.** Until then Woodpecker cannot pull
it anonymously and every pipeline fails at image pull — a failure that reads like a
Woodpecker misconfiguration rather than a permissions one.

## 2. `cog.toml`

- `tag_prefix = "v"` — so tags are `v0.2.0`, matching the GitHub Release convention.
- `ignore_merge_commits = true` — the three historical merge commits are the only
  non-conventional commits in the repository.
- `branch_whitelist = ["main"]` — bumps cannot happen from a feature branch.
- `bump_commit_message` carrying **`[skip ci]`**, so the changelog commit CI pushes back to
  `main` does not re-trigger the release pipeline. This is the loop guard, and it is
  load-bearing: without it, every release triggers another release.
- Changelog configured with the GitHub remote, owner and repository so entries link to
  commits and authors.
- The `chore(version)` bump commit suppressed from the changelog.
- Section titles for the types beyond feat/fix/perf, so Refactoring, Tests and Build & CI
  appear as named groups.

Only `feat`, `fix` and `perf` affect the version number. The other types appear in the
notes without triggering a release on their own — a docs-only merge publishes nothing.

## 3. Packaging

In `Directory.Build.props`: delete `<TargetFramework>`; correct `RepositoryUrl` to
`https://github.com/barryw/SixtyFiveXX`.

In `src/SixtyFiveXX/SixtyFiveXX.csproj`:

- `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`
- `Description`, `PackageTags`, `PackageProjectUrl`, `PackageReadmeFile` (the root README,
  packed via an `ItemGroup`), `RepositoryType`.
- `PublishRepositoryUrl`, `EmbedUntrackedSources`, `ContinuousIntegrationBuild` (CI only),
  `IncludeSymbols` with `SymbolPackageFormat=snupkg`.
- **No new `PackageReference`.** SourceLink is in the SDK from .NET 8, so source linking
  costs nothing and the library's zero-dependency rule holds.

`<Version>` is deliberately absent. It is supplied by `scripts/release.sh` from the tag cog
creates, so the tag and the package can never disagree.

## 4. `scripts/release.sh`

The conditional logic lives in one testable shell script rather than spread across
Woodpecker steps, because the no-op case cannot be expressed as a step exit code.

1. Record the current tag (`cog get-version --tag`, with a fallback for the no-tag case).
2. `cog bump --auto`.
3. Record the tag again. **If it did not change, nothing was releasable — exit 0 without
   publishing.** This is the only reliable no-op signal, since `cog bump` exits 0 either
   way; parsing its English output would be brittle.
4. `dotnet pack src/SixtyFiveXX -c Release -p:Version=<new version without prefix>`
5. `dotnet nuget push` with `--skip-duplicate`.
6. `git push --follow-tags` — the changelog commit and the tag.
7. `gh release create <tag> --notes-file <(cog changelog --at <tag>)`, attaching the
   `.nupkg` and `.snupkg` as release assets.

**The ordering is deliberate.** NuGet is published *before* git is pushed, so the
repository never claims a version that failed to publish. If NuGet fails, nothing is
pushed and the next merge retries cleanly. The GitHub Release comes last because it
requires the tag to exist on the remote.

## 5. Pipelines

`.woodpecker/build.yml` — pull requests, manual runs, and pushes to branches other than
`main`. All steps on the pinned CI image:

1. **lint** — `cog check` (merge commits ignored per `cog.toml`)
2. **build** — `dotnet build -c Release`
3. **unit** — `dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category!=Performance"`, both TFMs
4. **conformance** — `klaus/build.sh`, then the full conformance suite
5. **performance** — `--filter "Category=Performance"` with `failure: ignore`. The number
   stays visible; runner contention cannot turn the build red. A gate that flakes is a gate
   people learn to re-run instead of read.

`.woodpecker/release.yml` — pushes to `main`. The same five gates, then `scripts/release.sh`.

## Secrets and tokens

| Secret | Value | Used by |
| --- | --- | --- |
| `nuget_api_key` | nuget.org key, scoped to package glob `SixtyFiveXX` | release |
| `github_token` | GitHub PAT: contents read/write on this repository | release |
| `ghcr_token` | GitHub PAT with `write:packages` | image |

The registry username is the literal `barryw` and is not a secret.

`github_token` needs write access because CI pushes the changelog commit and tag back to
`main` and creates Releases. `gh auth token` prints a usable token from the local keyring;
a dedicated fine-grained PAT scoped to this one repository is preferable to reusing the
CLI's. **If branch protection is ever enabled on `main`, this token must be allowed to
bypass it**, or releases will fail at the push step after having already published to
NuGet — the one ordering the script cannot protect against.

Secrets must be restricted so pull requests from forks cannot reach them.

## Developer discipline

- **`cog check` is a blocking gate** on every pull request and push. A non-conventional
  message cannot reach `main`.
- **`cog install-hook --all`** installs a local `commit-msg` hook that rejects a bad
  message before it is written, rather than after it is pushed. Git hooks are not
  distributed by clone, so this is a documented setup step in `CONTRIBUTING.md`.
- `CHANGELOG.md` is generated, never hand-edited.

## Bootstrap — one-time, in order

1. Enable `barryw/SixtyFiveXX` in the Woodpecker UI.
2. Add the three secrets.
3. Create the nuget.org API key scoped to glob `SixtyFiveXX`. Note the expiry — nuget.org
   caps keys at 365 days and an expired key fails only at release time.
4. Run the image pipeline, then **set the GHCR package public**.
5. **Seed the first version locally:** `cog bump --version 0.2.0`, then push. `--auto` would
   produce `0.1.0` from this history; 0.2.0 is wanted, matching Phase 2 of the roadmap's
   eight. Thereafter CI bumps automatically.
6. `cog install-hook --all` on each development machine.

## Verification

- The image builds; `64tass --version`, `cog --version`, `gh --version` and a `net8.0`
  `dotnet test` all succeed inside it.
- The full non-performance suite passes locally against the multi-targeted library on both
  TFMs.
- `dotnet pack -p:Version=0.2.0-local` produces a `.nupkg` and `.snupkg`; the nuspec shows
  the description, the corrected repository URL, the README, and both `lib/net8.0` and
  `lib/net10.0`.
- `scripts/release.sh` is exercised against a scratch clone with a fake registry — its
  no-op path in particular, which is the branch most likely to be wrong and the one that
  would otherwise republish a stale version.
- The release pipeline's NuGet push is the one step no local check covers. It is guarded by
  `--skip-duplicate` and by publishing to NuGet before pushing git.

## Out of scope

- GitHub Actions. Woodpecker is the CI system.
- `netstandard2.0` / Unity / .NET Framework. No consumer has asked, and it would need `#if`
  polyfills through the core that the conformance suite would not exercise.
- Publishing the benchmark or test projects.
- Package signing. nuget.org does not require author signing.
- Prerelease channels and per-commit versions.

## Risks

- **nuget.org is append-only.** A published version can be unlisted, never deleted or
  replaced. Combined with automatic releases on every merge, a bad `feat:` commit reaching
  `main` becomes a permanent public version. The gates run before the publish for exactly
  this reason.
- **The `[skip ci]` loop guard is load-bearing and must be verified**, not assumed.
  Woodpecker's handling of skip markers is a documented behaviour but has never been
  exercised on this instance; if it does not hold, releases recurse.
- **The GHCR package defaults to private**, and the resulting failure looks like a
  Woodpecker problem rather than a permissions one.
- **The net8.0 TFM is only as trustworthy as the runtime in the image.** Drop the .NET 8
  runtime install and `dotnet test` silently covers only net10.0 while the package still
  advertises net8.0.
