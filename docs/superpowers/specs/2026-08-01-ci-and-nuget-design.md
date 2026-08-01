# SixtyFiveXX — Woodpecker CI and NuGet publishing

**Goal:** A Woodpecker pipeline that actually runs against `barryw/SixtyFiveXX`, executing
in a container that already contains 64tass, plus tag-driven publishing of the core to
nuget.org as a multi-targeted package.

**Status:** design approved 2026-08-01.

## Established facts — verified before this spec was written, do not re-derive

- **The core compiles clean on `net8.0`.** Proven by building `src/SixtyFiveXX` with
  `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` under `TreatWarningsAsErrors`:
  both TFMs produced assemblies, zero warnings, **no `#if` required**. The language and
  BCL features in use — collection expressions, primary constructors,
  `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative`,
  generic `Enum.GetValues<T>()`, `ref` returns — are all available on net8.0.
- **`SixtyFiveXX` is unclaimed on nuget.org.** Both the flat-container and registration
  endpoints return HTTP 404.
- **`IsPackable=false` is already set** on `tests/SixtyFiveXX.Tests`,
  `tests/SixtyFiveXX.Conformance` and `bench/SixtyFiveXX.Benchmarks`. Only
  `src/SixtyFiveXX` packs. No change needed.
- **`Directory.Build.props` carries the wrong repository URL** —
  `https://github.com/barrywalker/SixtyFiveXX`. The account is `barryw`.
- **`Directory.Build.props` sets singular `<TargetFramework>net10.0</TargetFramework>` for
  every project in the tree.** All three non-library projects *also* set it in their own
  csproj, so the property in the props file is redundant for them.
- **`apt-get install -y --no-install-recommends 64tass` works** in
  `mcr.microsoft.com/dotnet/sdk:10.0` — verified end-to-end in Docker during Phase 2b,
  including a full conformance run.
- **The conformance suite needs a binary built on demand.** Klaus's interrupt test is
  source-only upstream; `tests/SixtyFiveXX.Conformance/klaus/build.sh` assembles it and
  requires 64tass. Without it, `KlausInterruptTests` fails with an instruction rather
  than skipping.
- **The performance gate is machine-dependent.** `Core_SustainsAtLeastFiftyMegahertz`
  measured 6.3 MHz on a machine at load average 37 and passed in 373 ms once load fell.
- **Woodpecker is self-hosted and already runs other GitHub repositories.** The server and
  its GitHub integration exist; only this repository needs enabling.

## Architecture

Four independent pieces:

| Piece | Artifact | Trigger |
| --- | --- | --- |
| CI image | `docker/ci.Dockerfile` + `.woodpecker/image.yml` → `ghcr.io/barryw/sixtyfivexx-ci:1` | changes to `docker/ci.Dockerfile` |
| Packaging metadata | `Directory.Build.props`, `src/SixtyFiveXX/SixtyFiveXX.csproj` | — |
| Build pipeline | `.woodpecker/build.yml` | push, pull_request, manual |
| Release pipeline | `.woodpecker/release.yml` | `refs/tags/v*` |

Woodpecker reads every file in a `.woodpecker/` directory as a separate pipeline, so the
existing single `.woodpecker.yml` is replaced by that directory. Each pipeline has one
job and its own `when:` clause, which keeps the release path incapable of firing on a
push.

## 1. The CI image

`docker/ci.Dockerfile`:

- `FROM mcr.microsoft.com/dotnet/sdk:10.0`
- `64tass` and `git` from apt.
- **The .NET 8 runtime.** The SDK 10 image can *build* `net8.0` but cannot *run* it, so
  `dotnet test` against the net8.0 TFM would fail without it. Installed via the official
  `dotnet-install.sh` with `--channel 8.0 --runtime dotnet`. Multi-targeting without
  executing the net8.0 tests would mean publishing a target nothing verifies.
- Verify inside the build: `64tass --version` and `dotnet --list-runtimes` must both
  succeed, so a broken image fails at build time rather than in a confusing test run.

Published to GitHub Container Registry, which is free for public repositories. Tagged `:1`
— a fixed major tag, bumped by hand when the toolchain intentionally changes. Pipelines
pin that tag so an image rebuild cannot silently alter the toolchain under a green build.

`.woodpecker/image.yml` builds and pushes it, gated on `path: docker/ci.Dockerfile` so
ordinary commits never rebuild it.

**The image must be made public once**, by hand, in GitHub's package settings. Until then
Woodpecker cannot pull it anonymously and every pipeline fails on image pull.

## 2. Packaging

In `Directory.Build.props`:

- Remove `<TargetFramework>` entirely. It is redundant — every non-library project sets its
  own — and leaving it would collide with the library's plural `<TargetFrameworks>`,
  because MSBuild treats a project with both as single-targeted.
- Correct `RepositoryUrl` to `https://github.com/barryw/SixtyFiveXX`.

In `src/SixtyFiveXX/SixtyFiveXX.csproj`:

- `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`
- Package metadata: `Description`, `PackageTags`, `PackageProjectUrl`,
  `PackageReadmeFile` (the root `README.md`, packed via an `ItemGroup`), `RepositoryType`.
- Reproducibility: `PublishRepositoryUrl`, `EmbedUntrackedSources`,
  `ContinuousIntegrationBuild` (set from CI only), `IncludeSymbols` with
  `SymbolPackageFormat=snupkg`.
- **No new `PackageReference`.** SourceLink is built into the SDK from .NET 8, so source
  linking needs no package and the library's zero-dependency rule is preserved.

`<Version>` is deliberately **not** set in the project. It comes from the release tag, so
the tag and the package can never disagree. A local `dotnet pack` produces `1.0.0`, which
is expected and harmless because only the release pipeline pushes.

## 3. The build pipeline

`.woodpecker/build.yml`, all steps on `ghcr.io/barryw/sixtyfivexx-ci:1`:

1. **build** — `dotnet build -c Release`
2. **unit** — `dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category!=Performance"`.
   Runs on both TFMs, because the test project references a multi-targeted library.
3. **conformance** — `klaus/build.sh`, then `dotnet test tests/SixtyFiveXX.Conformance -c Release`.
   2,560,000 Harte vectors plus both Klaus programs.
4. **performance** — `--filter "Category=Performance"` with `failure: ignore`. The number
   stays visible in the log; runner contention cannot turn the build red. A gate that
   flakes is a gate people learn to re-run instead of read.

## 4. The release pipeline

`.woodpecker/release.yml`, `when: event: tag`, `ref: refs/tags/v*`.

Re-runs the full gates — a tag can be pushed at any commit, so the release path must not
assume a build pipeline already passed for that tree — then:

```
VER=${CI_COMMIT_TAG#v}
dotnet pack src/SixtyFiveXX -c Release -p:Version=$VER -p:ContinuousIntegrationBuild=true
dotnet nuget push "**/*.nupkg" -k $NUGET_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate
```

`--skip-duplicate` keeps a re-run of a tag from failing on an already-published version.
The `.snupkg` is pushed by the same command; nuget.org accepts it alongside the `.nupkg`.

**Version 0.2.0 is the first release** — 0.x because the public API is still moving (two
properties were renamed during Phase 2b), and `.2` to match Phase 2 of the roadmap's eight.

## Secrets

Woodpecker repository secrets, named exactly:

| Secret | Value | Used by |
| --- | --- | --- |
| `nuget_api_key` | nuget.org API key, scoped to package glob `SixtyFiveXX` | release |
| `ghcr_token` | GitHub PAT with `write:packages` | image |

The registry username is the literal `barryw` and is not a secret, so it goes in the
pipeline file rather than the secret store.

The release secret must be restricted to the `tag` event so a pull request from a fork
cannot reach it.

## Manual steps — these cannot be automated from here

1. Enable `barryw/SixtyFiveXX` in the Woodpecker UI.
2. Add the two secrets above.
3. Create the nuget.org API key, scoped to package glob `SixtyFiveXX`. Note the expiry —
   nuget.org caps keys at 365 days, and an expired key fails only at release time.
4. Run the image pipeline once, then **set the ghcr package to public**.
5. Release: `git tag v0.2.0 && git push origin v0.2.0`.

## Verification

- The image builds, and `64tass --version` plus a `net8.0` `dotnet test` both succeed
  inside it.
- `dotnet build -c Release` and the full non-performance suite pass locally against the
  multi-targeted library, on both TFMs.
- `dotnet pack -p:Version=0.2.0-local` produces a `.nupkg` and a `.snupkg`; inspect the
  nuspec to confirm the description, the corrected repository URL, the README, and both
  `lib/net8.0` and `lib/net10.0` folders.
- The release pipeline is proven only by tagging. Its push step is the one thing no local
  check covers, so the pack step must be verified locally first and `--skip-duplicate`
  must be present before the first tag.

## Out of scope

- GitHub Actions. Woodpecker is the CI system; a second one is not wanted.
- `netstandard2.0` / Unity / .NET Framework support. No consumer has asked, and it would
  need `#if` polyfills through the core that the conformance suite would not exercise.
- Publishing the benchmark or test projects.
- Signing the package. nuget.org does not require author signing.
- A prerelease feed or per-commit versions.

## Risks

- **nuget.org is append-only.** A published version can be unlisted but never deleted or
  replaced. The pack output must be inspected locally before the first tag.
- **The ghcr package defaults to private**, and the failure mode is every pipeline failing
  on image pull, which reads like a Woodpecker misconfiguration rather than a permissions
  one.
- **The net8.0 TFM is only as trustworthy as the runtime in the image.** If the .NET 8
  runtime install is dropped from the Dockerfile, `dotnet test` silently exercises only
  net10.0 while the package still advertises net8.0.
