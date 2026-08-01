# SixtyFiveXX — release engineering, on the house standard

**Goal:** Bring SixtyFiveXX onto the existing `woodpecker-release` infrastructure, adding
the one thing that infrastructure does not yet have — a .NET library template that packs
and publishes to NuGet — so that a merge to `main` carrying a `feat:` or `fix:` stamps the
version into the project files, tags it, cuts a GitHub Release with generated notes, and
then publishes the matching package.

**Status:** design revised 2026-08-01 after surveying house patterns and external prior art.
Supersedes the earlier revision of this file, which invented a bespoke pipeline. Three of
its decisions were wrong and are reversed below.

## What changed from the previous revision, and why

| Previous decision | Replaced by | Reason |
| --- | --- | --- |
| NuGet published *before* git push | GitHub Release first, artifacts after, via `depends_on:` | Owner's instruction, and the house rule in every template |
| Version injected at pack time with `-p:Version=` | `cog` `pre_bump_hooks` stamp `Directory.Build.props`, `git add`ed into the bump commit | Owner requires the project files to carry the version; MinVer and Nerdbank.GitVersioning both fail that requirement because neither writes the resolved version to any file |
| Bespoke `.woodpecker/` pipelines in this repo | `woodpecker-release` config-service template | House standard; ~8 other C# repos benefit |
| Tag-comparison to detect a no-op release | `/woodpecker/version.txt` written by the plugin | The house mechanism, already proven in `release-docker` |
| `[skip ci]` hand-rolled into the bump message | cog's native `skip_ci` setting | Already in the canonical `cog.toml` |

## Established facts — verified, do not re-derive

**The house standard** (`~/Git/woodpecker-release`, "Walker Heavy Industries house
standard", ADR 0003):

- Two components: a plugin image `ghcr.io/barryw/woodpecker-release` and a **config
  service** that renders full pipeline YAML from templates, so a repo carries only
  `.woodpecker/woodpecker-template.yaml` naming a template plus data.
- A canonical `onboarding/cog.toml` is copied verbatim into every repo; the only
  repo-specific value is `[changelog].repository`. It sets `from_latest_tag = true`,
  `ignore_merge_commits = true`, `branch_whitelist = ["main"]`, `tag_prefix = "v"`,
  `skip_ci = "[skip ci]"`, the full house `[commit_types]` set with changelog titles, and a
  `[git_hooks.commit-msg]` running `cog verify --file $1`.
- **Existing templates:** `release-tag-only`, `release-go-library`, `release-go-binary`,
  `release-docker`, `release-terraform`, `release-macos-app`, `release-static-site`.
  **There is no .NET template.**
- **Plugin modes:** `release-tag` (`:latest`) bump + changelog + GitHub Release;
  `release-go` (`:go`); `bump` (`:latest`) bump only.
- **The release step is gated `when: event: push, branch: main`** in every template.
- **Artifact steps run after the release step via `depends_on: [release]`.** Confirmed in
  `release-docker`, and house-wide.
- **The plugin writes the new version to `/woodpecker/version.txt`.** Downstream steps read
  it and exit 0 early when it is absent or `NONE`:
  ```
  VERSION=$(cat /woodpecker/version.txt 2>/dev/null || echo "NONE")
  test "$VERSION" != "NONE" || { echo "No new version, skipping build"; exit 0; }
  ```
  This is the house no-op mechanism and removes any need to interrogate `cog` for whether a
  bump happened.
- Templates parameterise their images and inject `setup_commands` / `test_commands`
  (see `release-docker`), rather than baking a bespoke image per repo.
- `clone: git: {tags: true, depth: 0}` — cog needs full history and tags.
- Secret names are house-wide: `github_token`, `ghcr_username`, `ghcr_token`.
- `validate-commits` runs on `ghcr.io/cocogitto/cog:6.2.0` and tolerates a repo with no
  tags yet.

**The .NET version-stamping pattern**, in production in `barryw/NovaVM` (at 0.36.0) and
`barryw/Novus`:

```toml
pre_bump_hooks = [
    """sed -i.bak -E \
      -e 's|<Version>[^<]*</Version>|<Version>{{version}}</Version>|' \
      -e 's|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>{{version}}.0</AssemblyVersion>|' \
      -e 's|<FileVersion>[^<]*</FileVersion>|<FileVersion>{{version}}.0</FileVersion>|' \
      -e 's|<InformationalVersion>[^<]*</InformationalVersion>|<InformationalVersion>{{version}}</InformationalVersion>|' \
      Directory.Build.props && rm -f Directory.Build.props.bak""",
    "git add Directory.Build.props",
]
```

**External prior art:**

- MinVer is the dominant .NET choice (Respawn, Scrutor, RestSharp, Foundatio,
  microsoft/sbom-tool) but **derives the version at build time and writes it to no file** —
  it cannot satisfy the requirement that the repository state its own version. Same for
  Nerdbank.GitVersioning.
- Microsoft's library guidance: pin `AssemblyVersion` for binding stability, float
  `FileVersion` and `PackageVersion`, and leave `InformationalVersion` to SourceLink.
- `barryw/NovaVM` already mitigates "Release created but publish failed" with
  **draft-until-verified**: `gh release create --draft`, flipped public only after the
  artifacts land.
- Pairing cog with .NET is rare but real outside these repos (`keyz182/KeyzAllowUtils`).
  There is no blessed community recipe; the hook is adapted from NovaVM's working block.

**This project:**

- The core compiles clean on `net8.0` — proven by building both TFMs under
  `TreatWarningsAsErrors`, zero warnings, **no `#if`**.
- `SixtyFiveXX` is unclaimed on nuget.org.
- **No repository in the suite publishes to NuGet.** There is no house precedent for that
  step; it is designed here from external guidance.
- `IsPackable=false` is already set on both test projects and the benchmarks.
- `Directory.Build.props` has the wrong `RepositoryUrl` (`barrywalker`, not `barryw`) and
  sets a singular `<TargetFramework>` that will collide with multi-targeting.
- The repo has **no tags**, so per the onboarding guide cog starts clean at `v0.1.0`.
- `apt-get install 64tass` works in `mcr.microsoft.com/dotnet/sdk:10.0`, verified in Docker.
- The performance gate is machine-dependent (6.3 MHz under load 37, passing at idle).

## Scope: two repositories

### Part A — `woodpecker-release`: a `release-dotnet-library` template

A new `config-service/templates/release-dotnet-library/pipeline.yaml.template`, modelled
directly on `release-docker`. Steps:

1. `validate-commits` — `cog check`, unchanged from the house template.
2. `build` — `dotnet build -c Release`.
3. `test` — `dotnet test` with a configurable filter.
4. `conformance` — optional, enabled by data flag, for repos with a slow second suite.
5. `release` — the plugin in `release-tag` mode, `when: event: push, branch: main`.
6. `nuget-publish` — `depends_on: [release]`, reads `/woodpecker/version.txt`, exits 0 if
   `NONE`, else packs and pushes.

Template data, following `release-docker`'s parameterisation conventions:

| Key | Purpose |
| --- | --- |
| `sdk_image` | Build/test image; defaults to `mcr.microsoft.com/dotnet/sdk:10.0` |
| `setup_commands` | Extra tooling installed before build (this repo: 64tass, .NET 8 runtime) |
| `test_project` / `test_filter` | Which tests, and which to exclude |
| `conformance_project` / `conformance_commands` | Optional second suite |
| `pack_project` | Project to pack |
| `nuget_publish` | Whether to push to nuget.org |

The template must stay generic — most .NET repos in the suite need neither 64tass nor a
second suite, and they should be able to adopt it with stock images.

**Publishing uses the version from `/woodpecker/version.txt`, not a re-derived one**, so
the package version, the tag and the stamped `Directory.Build.props` are the same value by
construction rather than by coincidence.

Secrets: reuses `github_token`; adds `nuget_api_key`.

### Part B — `SixtyFiveXX`: onboarding

1. **`cog.toml`** — the canonical file copied verbatim, with `repository = "SixtyFiveXX"`
   and .NET `pre_bump_hooks` (below).
2. **`.woodpecker/woodpecker-template.yaml`** naming `release-dotnet-library`.
3. **Delete `.woodpecker.yml`** — the config service generates the pipeline.
4. **`Directory.Build.props`** — add the four version properties for the hook to rewrite;
   fix `RepositoryUrl`; remove the singular `<TargetFramework>`.
5. **`src/SixtyFiveXX/SixtyFiveXX.csproj`** — `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`,
   package metadata (`Description`, `PackageTags`, `PackageProjectUrl`,
   `PackageReadmeFile`, `RepositoryType`), `IncludeSymbols` with `snupkg`,
   `PublishRepositoryUrl`, `EmbedUntrackedSources`. **No new `PackageReference`** —
   SourceLink is in the SDK from .NET 8, so the zero-dependency rule holds.
6. **`CONTRIBUTING.md`** — documents `cog install-hook --all`, since git hooks do not
   survive a clone.
7. **The CI image** `ghcr.io/barryw/sixtyfivexx-ci` carrying 64tass and the .NET 8 runtime,
   passed to the template as `sdk_image`. The template stays generic; this repo supplies a
   specific image because its conformance suite needs an assembler and because
   multi-targeting requires a runtime the SDK 10 image lacks.

## Assembly versioning

`AssemblyVersion` is pinned to **major.minor** while the package is `0.x`, and to
**major** from 1.0 onward:

| Release | `Version` | `AssemblyVersion` | `FileVersion` |
| --- | --- | --- | --- |
| 0.3.1 | `0.3.1` | `0.3.0.0` | `0.3.1.0` |
| 0.3.2 | `0.3.2` | `0.3.0.0` | `0.3.2.0` |
| 1.4.2 | `1.4.2` | `1.0.0.0` | `1.4.2.0` |

This honours Microsoft's rule — assembly identity changes exactly when binary
compatibility can break — while respecting SemVer's 0.x clause, under which a minor bump
*is* breaking. Pinning to major alone would leave every incompatible 0.x release sharing
identity `0.0.0.0`.

It is a deliberate deviation from NovaVM, which bumps all four properties together. NovaVM
is an application; SixtyFiveXX is a library other code references, so binding stability is
worth the extra hook logic. `InformationalVersion` is left to SourceLink rather than
hand-stamped.

The hook therefore cannot be NovaVM's flat `sed`: deriving `major.minor` from
`{{version}}` needs a small shell expansion. This is the one genuinely new piece of logic
in the design and the one most likely to be wrong — it gets a test.

## Ordering

```
validate-commits → build → test → conformance
                                      ↓
                            release  (plugin: bump, stamp, tag, push, GitHub Release)
                                      ↓  depends_on
                            nuget-publish  (pack at that version, push)
```

The GitHub Release is created **before** the package is published. A NuGet version can be
unlisted but never deleted, so a package with no matching Release is a permanent
inconsistency; a Release without a package is cheap to fix. The residual failure — Release
exists, publish fails — is mitigated the way NovaVM already does it: create the Release as
a **draft**, publish, then flip it public.

Releases are reachable only from `main`, by the template's `when: event: push,
branch: main` on the release step and `branch_whitelist = ["main"]` in `cog.toml`.

## Secrets

| Secret | Value | Used by |
| --- | --- | --- |
| `github_token` | PAT: contents read/write on the repo | release (push, Release) |
| `nuget_api_key` | nuget.org key scoped to glob `SixtyFiveXX` | nuget-publish |
| `ghcr_username` / `ghcr_token` | for the CI image build | image |

House naming is followed exactly. If branch protection is ever enabled on `main`,
`github_token` must be allowed to bypass it, or releases fail at the push step.

## Bootstrap — one-time, in order

1. Land Part A in `woodpecker-release`; confirm the config service serves the new template.
2. Build and publish `ghcr.io/barryw/sixtyfivexx-ci`; **make the GHCR package public** —
   otherwise every pipeline fails at image pull, a failure that reads like a Woodpecker
   misconfiguration rather than a permissions one.
3. Enable `barryw/SixtyFiveXX` in Woodpecker; add the secrets.
4. Create the nuget.org key scoped to glob `SixtyFiveXX`. Note the 365-day cap; an expired
   key fails only at release time.
5. `cog install-hook --all` on each development machine.
6. Land Part B. The first qualifying merge releases `v0.1.0` — cog's clean-start value for
   a repo with no tags, per the onboarding guide. The earlier plan to seed `0.2.0` is
   dropped: deviating from the house bootstrap to encode roadmap phase in the version buys
   nothing and costs a manual step.

## Verification

- The template renders: exercise the config service against SixtyFiveXX's data block and
  inspect the generated YAML before any pipeline runs.
- The CI image: `64tass --version` and a `net8.0` `dotnet test` both succeed inside it.
- **The `AssemblyVersion` hook is unit-tested** against `0.3.1 → 0.3.0.0`,
  `0.10.2 → 0.10.0.0`, `1.4.2 → 1.0.0.0`, `10.0.0 → 10.0.0.0`. Naive string slicing breaks
  on two-digit components; this is the failure this design is most likely to ship.
- `cog bump --auto` on a scratch clone stamps all four properties consistently and the
  resulting `Directory.Build.props` builds.
- `dotnet pack` output: nuspec shows the corrected repository URL, the README, both
  `lib/net8.0` and `lib/net10.0`, and a `.snupkg` alongside.
- The no-op path: a `docs:`-only merge to `main` must produce no tag, no Release and no
  package, with the pipeline green.

## Out of scope

- GitHub Actions.
- `netstandard2.0` / Unity / .NET Framework — would need `#if` polyfills through the core
  that the conformance suite would not exercise.
- Migrating the other C# repos onto the new template. The template is written to serve
  them; adopting it is their own change.
- Package signing; prerelease channels; per-commit versions.

## Risks

- **nuget.org is append-only.** With automatic release on merge, a bad `feat:` reaching
  `main` becomes a permanent public version. The gates run before the publish for exactly
  this reason.
- **The `AssemblyVersion` derivation is new logic**, not copied from anything proven. It is
  the most likely defect in this design, hence the explicit test matrix.
- **No house precedent exists for the NuGet step.** Everything else here is a proven
  pattern; that step is not, and it is the one that writes to an append-only public
  registry.
- **The GHCR package defaults to private**, with a failure mode that misdirects.
- **The net8.0 TFM is only as trustworthy as the runtime in the image.** Drop the .NET 8
  runtime and `dotnet test` silently covers only net10.0 while the package advertises both.
