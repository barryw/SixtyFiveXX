# Research: .NET automated versioning & NuGet release, for SixtyFiveXX

Read-only research. No repo changes. Compiled 2026-08-01.

**Headline finding before the details:** the single most relevant prior art isn't a
random OSS blog post — it's the owner's **own** existing, working pattern, already
proven across `barryw/Novus`, `barryw/NovaVM`, and `barryw/PaperlessMCP`: cocogitto's
`pre_bump_hooks` running `sed` against `Directory.Build.props`/`.csproj`, on Woodpecker,
with a GitHub App–minted token and a draft-until-verified GitHub Release. That pattern
already satisfies the owner's two hard requirements (version lands in the repo's files;
tag and package version can never drift) for two apps and one library-shaped project.
What's genuinely new for SixtyFiveXX is the **NuGet-specific** tail: none of the owner's
existing C# repos (`Novus`, `NovaVM`/`e6502`, `sim6502`, `ImmichMCP`, `PaperlessMCP`,
`ViceMCP`) actually publish a package to nuget.org today. That part has no internal
precedent and had to be researched from the wider .NET ecosystem.

---

## 1. How does the .NET ecosystem get a git-derived version into the build?

**Contenders, evidence, and — critically — does the version land in the repo's files?**

| Tool | Mechanism | Version in repo files? | Real users found |
|---|---|---|---|
| **MinVer** | Reads the latest `git describe`-style tag at build time via an MSBuild task; computes `Version`/`AssemblyVersion`/etc. in memory | **No.** Never touches `.csproj`/`Directory.Build.props`. The version exists only in the build output (assembly, `.nupkg`) and in `git tag`. | `jbogard/Respawn`, `khellang/Scrutor`, `restsharp/RestSharp`, `FoundatioFx/Foundatio`, `exceptionless/Exceptionless`, `reactiveui/Akavache`, `FakeItEasy/FakeItEasy`, `CarterCommunity/Carter`, `DuendeSoftware/products` (IdentityServer et al.), `xoofx/zio`, `RehanSaeed/Schema.NET`, `jellyfin/TMDbLib`, and **`microsoft/sbom-tool`** (a Microsoft repo). Confirmed via `PackageReference Include="MinVer"` in each `.csproj` (GitHub code search, primary source). |
| **Nerdbank.GitVersioning (NBGV)** | `version.json` at repo root defines `major.minor` + a `nugetPackageVersion` scheme; build height (commit count since the version last changed) supplies the patch/prerelease tail | **Partial.** `version.json` *is* a committed file and *is* the source of truth, but it holds `major.minor` only — the full three-part version is still computed at build time, never re-written into `version.json` or the `.csproj`. Bumping requires a human (or `nbgv prepare-release`) editing `version.json`. | Primarily Microsoft's own tooling ecosystem: `dotnet/Nerdbank.GitVersioning` itself (self-hosted), `dotnet/Nerdbank.Streams`, VS-threading-adjacent repos. Two of the owner's own repos (`barryw/ImmichMCP`, `barryw/PaperlessMCP`) have a `version.json` at the root, but its schema (`{"version": "3.2.1"}`) does **not** match NBGV's schema — it's a bespoke file, not NBGV. |
| **GitVersion** | Branch-aware; infers version from branch name + config YAML (GitFlow/GitHubFlow/Mainline modes) | No — same as MinVer, purely a build-time computation. Heavier config surface, and multiple GitHub issues surfaced tag/branch detection footguns in CI (tag pushes get misread as branches, `CI_COMMIT_REF_NAME` confusion in GitLab/Azure DevOps). | Common in enterprise Azure DevOps pipelines; less visible in the exemplary OSS libraries surveyed here than MinVer. |
| **Directory.Build.props rewriting via a commit hook** (the owner's own pattern) | A pre-release hook (cocogitto `pre_bump_hooks`, or equivalent) runs `sed`/`dotnet build -p:Version=` and **commits** the mutated file back to the branch before tagging | **Yes — the only option that does.** | `barryw/Novus` (`sed` on two `.csproj` files), `barryw/NovaVM` (`sed` on `Directory.Build.props`, four tags: `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`), `keyz182/KeyzAllowUtils` (third-party, non-owner: rebuilds `.csproj` via `-p:Version=` and commits the resulting DLLs — but notably does **not** edit `<Version>` in the `.csproj` itself, so even this real independent example falls short of "version in the project file"). |
| **Pack-time `-p:Version=` only, no commit** | CI passes `-p:Version=$(git tag)` (or similar) at `dotnet pack`; the `.csproj`/`Directory.Build.props` keeps a permanent placeholder | **No.** `Tyrrrz/CliWrap`'s `Directory.Build.props` literally reads `<Version>0.0.0-dev</Version>` in the repo at all times; the real version is injected only via a reusable Actions workflow (`-p:Version=${{ github.ref_name }}` on tag push, or `0.0.0-ci-<sha>` otherwise). This is functionally MinVer's philosophy, hand-rolled. | `Tyrrrz/CliWrap` and the rest of Oleksii Holub's (Tyrrrz) libraries share one reusable `nuget.yml` workflow that does this. |

**Assessment for SixtyFiveXX:** MinVer is the clear ecosystem favorite for .NET *libraries specifically* (five-plus well-known, actively maintained NuGet packages, including one inside Microsoft's own GitHub org) — but it is disqualified by the owner's explicit, non-negotiable requirement that the version live in the repository's files, not just the artifact. NBGV gets partway there (`version.json` is committed) but still doesn't write the resolved three-part version back anywhere, and doesn't match the tool the owner has already standardized on (cocogitto) — adding NBGV would mean running two versioning systems that both claim authority. GitVersion is heavier and has real, documented CI tag/branch-detection bugs that MinVer's simpler model avoids by design; no evidence found of GitVersion being preferred by the specific class of small, well-run libraries this project resembles.

**Recommendation:** Extend the owner's own proven pattern (`pre_bump_hooks` + `sed` into `Directory.Build.props`) rather than adopt any of the git-native tools. It is the only mechanism that satisfies the hard requirement, it is already battle-tested in this owner's Woodpecker environment (Novus, NovaVM), and switching to MinVer/NBGV would mean re-litigating a tool decision the owner has already made for a property (file mutation) those tools deliberately don't support.

---

## 2. How is the tag-to-package-version match guaranteed?

There is no single named "consistency-check" idiom in the wider .NET ecosystem — the tools that dominate (MinVer, NBGV) sidestep the problem entirely by deriving *everything* from one source at build time, so there is structurally nothing to drift. That is the real answer to "what do projects do": **derive both from one source**, not **verify two sources agree**.

For projects (like this one) that must instead write the version into a file *before* tagging, the owner's own `NovaVM`/`Novus` pipelines show the guarantee mechanism directly, and it is procedural, not a separate check step:

1. `cog bump --auto` computes the next version from conventional commits since the last tag.
2. Its `pre_bump_hooks` rewrite `Directory.Build.props`/`.csproj` with that *same* version, in the *same* commit cog is about to create.
3. `git add` stages the file so it becomes part of `cog`'s version-bump commit.
4. Only after that commit is made does `cog` create the git tag, on that exact commit.
5. Woodpecker's `01-bump.yml` pushes commit and tag together (`git push origin HEAD:main --tags`).

Because the file mutation happens as a `pre_bump_hook` — before the tag is cut, in the hook's own working tree, staged into the bump commit — there is no window where the tag and the file can name different versions; they are two effects of the same `cog bump` invocation. This is a **structural** guarantee, not a lint check bolted on afterward, and it is stronger than most CI "grep the tag, grep the version, diff them" verification steps found in blog posts (e.g., the generic Python packaging pattern that showed up in search results: extract tag, extract package metadata version, fail the pipeline if they differ). That generic pattern is a reasonable *belt-and-suspenders* addition but isn't what any of the real .NET-library examples surveyed (MinVer/NBGV-based) needed, because they don't have two sources to compare.

**Recommendation:** Copy the "hook mutates the file inside the same bump operation" structure exactly — do not add a separate post-hoc "does the tag match the csproj" CI assertion; it would be redundant given the ordering guarantee. If the release template is later shared across repos (as `woodpecker-release` already does for Go/static-site templates), it *would* be reasonable to add one grep-based assertion as defense-in-depth against a future config edit that reorders `pre_bump_hooks` — this mirrors the "circuit breaker" pattern already used in `NovaVM/.woodpecker/ci.yaml` for the CI-loop problem (see §5), where cog.toml config is backed up by an explicit commit-message check in the pipeline itself.

---

## 3. AssemblyVersion vs FileVersion vs InformationalVersion vs PackageVersion

This is the one question with an unambiguous, authoritative primary source: Microsoft's own **[Versioning and .NET libraries](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning)** guidance page. Quoting directly:

> **NuGet package version** — "Because the NuGet package version is the most visible version to developers, it's a good idea to update it using Semantic Versioning (SemVer)... ✔️ DO use the NuGet package version in public documentation as it's the version number that users will commonly see."
>
> **Assembly version** — "The assembly version is what the CLR uses at runtime to select which version of an assembly to load... ✔️ CONSIDER only including a major version in the AssemblyVersion. For example, Library 1.0 and Library 1.0.1 both have an AssemblyVersion of `1.0.0.0`, while Library 2.0 has AssemblyVersion of `2.0.0.0`. When the assembly version changes less often, it reduces binding redirects. ✔️ CONSIDER keeping the major version number of the AssemblyVersion and the NuGet package version in sync. ❌ DO NOT have a fixed AssemblyVersion" (a *permanently* unchanging one — it should still bump on major).
>
> **Assembly file version** — "has no effect on runtime behavior... ✔️ CONSIDER including a continuous integration build number as the AssemblyFileVersion revision... ✔️ DO use the format `Major.Minor.Build.Revision`."
>
> **Assembly informational version** — "has no effect on runtime behavior... ❌ AVOID setting the assembly informational version yourself. Allow SourceLink to automatically generate the version containing NuGet and source control metadata," e.g. `1.0.0-beta1+204ff0a` (SemVer + commit hash).

**So the convention is:** `AssemblyVersion` pinned to **major-only** (floats rarely, minimizes binding-redirect churn for consumers); `PackageVersion`/`Version` floats with every release (full SemVer, what users see); `FileVersion` floats with every build (cosmetic, Windows Explorer only); `InformationalVersion` is best left to SourceLink to populate automatically with the SemVer + commit SHA, rather than hand-set.

**Where this diverges from what's actually observed in the wild:** neither of the owner's own C# repos nor most of the MinVer-based libraries surveyed actually implement the "pin AssemblyVersion to major" rule:
- `barryw/NovaVM`'s `cog.toml` bumps `AssemblyVersion` on *every* release (`{{version}}.0`, so `1.2.3` → `1.2.3.0`, not pinned to `1.0.0.0`/`2.0.0.0`).
- `barryw/NovaVM`'s hook also hand-sets `InformationalVersion` via `sed`, which Microsoft explicitly recommends against (SourceLink should own it).
- Respawn and Scrutor (MinVer-based) don't set `AssemblyVersion` at all in their `.csproj` — when only `<Version>` is set (or MinVer derives it), the SDK defaults `AssemblyVersion`/`FileVersion`/`InformationalVersion` all to the *same* floating value unless explicitly overridden. In practice most MinVer users don't override, so AssemblyVersion floats too, in violation of Microsoft's own "consider major-only" guidance. Respawn/Scrutor **do** both set `IncludeSymbols`+`SymbolPackageFormat=snupkg`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`, `Deterministic=true`, `ContinuousIntegrationBuild` — i.e., they follow the SourceLink/reproducible-build half of the guidance closely, just not the AssemblyVersion-pinning half.

This is a genuine "ecosystem consensus vs. common practice" gap: the *documented best practice* (major-only AssemblyVersion) is real and well-reasoned (it matters specifically for a library other projects reference — every AssemblyVersion bump is technically a breaking change for consumers who did an assembly-qualified type reference or strong-name binding, even on modern .NET where redirects are usually auto-handled), but it is inconsistently followed even by well-regarded libraries, and not followed by the owner's own prior C# work (which are apps/tools, not consumed libraries, so the omission mattered less there).

**Recommendation for SixtyFiveXX (this project *is* a consumed library, so this matters more than it did for Novus/NovaVM):**
- `<Version>` — full SemVer from cog, floats every release. Feeds `PackageVersion` by MSBuild default.
- `<AssemblyVersion>` — pin to **major.0.0.0** only, updated by the `pre_bump_hook` only when the major segment actually changes (a slightly smarter sed than NovaVM's, or a small script comparing old/new major).
- `<FileVersion>` — let it float with `<Version>` (or `{{version}}.0`); harmless, cosmetic.
- `<InformationalVersion>` — do **not** hand-set via sed. Add `Microsoft.SourceLink.GitHub` + `PublishRepositoryUrl`/`EmbedUntrackedSources`/`ContinuousIntegrationBuild=true` (exactly Respawn/Scrutor's block) and let the SDK/SourceLink populate it with SemVer+commit SHA automatically. This is strictly less code in the `pre_bump_hooks` sed, not more — a net simplification versus NovaVM's four-tag rewrite.

---

## 4. Release ordering: GitHub Release before NuGet

**What was actually found in the wild:** most MinVer-based .NET libraries surveyed (`Respawn`, `Carter`) don't automate a `gh release create` step in CI *at all* — they push straight to NuGet on tag push and leave the GitHub Release as a separate, often manual or unautomated artifact (Respawn's `release.yml` has no release-creation step whatsoever; same for Carter's `dotnetcore.yml`). So "Release-before-NuGet as an enforced CI gate" is **not** a distinctly .NET-ecosystem pattern — it's far more associated with cross-language release tooling (GoReleaser, semantic-release, Changesets) and, concretely for this project, with the owner's **own** `woodpecker-release` plugin, which already implements it for Go/static-site/macOS templates.

**The strongest argument for Release-first** (synthesized from that tooling's design, since no .NET-specific source argues it explicitly): the GitHub Release is the durable, human-facing record of "this version happened" — release notes, changelog, tag annotation — and it's cheap and safe to create even if downstream artifact publishing fails partway. Publishing to NuGet first and *then* trying to generate a release afterward risks a package existing with no changelog/notes if that second step fails or is skipped — and nuget.org packages can be unlisted but **never deleted**, so a package published without a matching, findable Release is a permanent, uncorrectable gap. Release-first makes the release notes the object of record and the package push the (retriable) side effect.

**Its failure mode, and how the owner's own tooling already mitigates it:** a Release can exist for a version that then fails to publish to NuGet (build breaks, NuGet auth fails, network error). `barryw/NovaVM`'s pipeline solves this with **draft-until-verified**:
- `build-gate.yaml` (on tag push) creates the GitHub Release with `gh release create "$TAG" --draft ...` — visible to nobody but collaborators, not "released" in any real sense.
- Only after all downstream build/upload legs succeed does `build-finalize.yaml` run `gh release edit "$CI_COMMIT_TAG" --draft=false --repo "$CI_REPO"`, flipping it public.

This is the correct, low-cost mitigation, and it directly answers the owner's own failure-mode concern: a draft Release that never gets un-drafted because NuGet publish failed is invisible and harmless; it's not "a Release exists for a version that fails to publish," because a draft isn't a public release. The remaining edge case — NuGet publish itself succeeding but the finalize step failing to flip the draft — is a much smaller, easily-alarmed-on gap (package is public, Release draft lags by seconds until a retry), versus the reverse ordering's edge case (package permanently public and unlisted-only with zero release notes ever created).

**Recommendation:** Adopt the draft → build/test/pack → verify NuGet push succeeded → `dotnet nuget push` → flip Release non-draft sequence, matching NovaVM's finalize step almost verbatim, adapted for a `dotnet pack`/`dotnet nuget push` leg instead of Go cross-compilation.

---

## 5. Avoiding the CI-pushes-a-commit-triggers-CI loop

Catalogue of real mechanisms found, with CI-system-agnosticism noted (the task explicitly warns not to assume Woodpecker's skip-marker behavior):

| Mechanism | How it works | CI-agnostic? | Evidence |
|---|---|---|---|
| `[skip ci]` / `[ci skip]` commit-message marker | CI system special-cases commits containing the string, doesn't schedule a pipeline for them at all | **No — must be verified per CI system.** Woodpecker does support it (confirmed in Woodpecker's own docs/issues: case-insensitive `[SKIP CI]`/`[CI SKIP]`), but this is a Woodpecker behavior, not a git or cocogitto guarantee. Cocogitto's `skip_ci` config (`cog.toml`) just controls what *string* gets appended to the bump commit message (default `[skip ci]`, overridable) — cocogitto has no opinion on whether any given CI actually honors it. | [cocogitto docs, "Skip CI Configuration"](https://docs.cocogitto.io/guide/misc.html); `barryw/Novus/cog.toml` and `barryw/PaperlessMCP/cog.toml` both set `skip_ci = "[skip ci]"`. |
| Path/branch filters | Pipeline only triggers on specific branches/paths; a bump commit to `main` that touches only `Directory.Build.props`/`CHANGELOG.md` can be excluded by path filter | Yes, structurally — path filtering is a config-file feature most CI systems have, though syntax differs. | `keyz182/KeyzAllowUtils/release.yml` uses `paths-ignore: ['CHANGELOG.md']` (GitHub Actions) to stop the *changelog* commit from re-triggering — but note this only excludes commits that touch *exclusively* that path; a version-bump commit touching `.csproj` too would still trigger. |
| Bot-author / commit-message-pattern detection ("circuit breaker") | The pipeline step itself inspects `git log -1 --pretty=%s` (or commit author) and exits early if it matches the bot's known commit pattern | **Yes — fully CI-agnostic**, since it's a shell check inside the pipeline step, not a CI-platform feature. This is the most robust option because it doesn't depend on any CI vendor's skip-marker support at all. | `barryw/NovaVM/.woodpecker/ci.yaml`, quoted directly: `if git log -1 --pretty=%s | grep -qE '^chore\(version\):'; then echo "HEAD is a bot version commit — skipping bump"; exit 0; fi` — explicitly commented as "Circuit breaker (defense in depth with cog.toml's non-bumping `chore`): never re-bump on cog's own version commit, or ci.yaml would loop cutting patch releases (WAL-162)." |
| Commit-type bump-eligibility config | Configure the tool itself so the bump commit's own conventional-commit type (`chore`) is *not* one that triggers another bump | Yes — this is a cocogitto config property (`bump_minor`/`bump_patch` flags per commit type in `[commit_types]`), tool-level not CI-level. | `barryw/NovaVM/cog.toml`: `chore = { changelog_title = "Miscellaneous" }` has **no** bump directive, with the comment: *"`chore` must NOT bump: cog's own version commit is `chore(version): vX.Y.Z`, so a bumping `chore` makes every release commit re-trigger ci.yaml and cut the next patch — a runaway re-bump loop (WAL-162: v0.2.0→v0.2.1→v0.2.2→…)."* This is a real bug the owner hit and fixed, not a theoretical concern. |
| Separate tokens / minted short-lived tokens | Using a distinct machine identity (bot account, GitHub App installation token) for the push, so the CI's own "ignore pushes by CI's own user" heuristics (where they exist) apply, and so the push is auditable/scoped separately from a human PAT | Partially — some CI platforms (notably GitHub Actions, when `GITHUB_TOKEN` is used) refuse to re-trigger workflows on pushes made with the default token, specifically to prevent this loop. Woodpecker has no equivalent built-in behavior, so this mechanism alone doesn't help there. | `barryw/Novus` and `barryw/NovaVM` mint a scoped GitHub App installation token per pipeline run (`mint-installation-token.sh --profile product-ci`) — this is about credential scoping/security (WAL-70/WAL-72), not loop-avoidance per se, though it's good practice regardless. |

**Assessment:** for Woodpecker specifically, since its `[skip ci]` support is real but is the *single point of failure* if it were ever to change or be misconfigured, the owner's own NovaVM already layers **two independent, CI-agnostic mechanisms** (non-bumping `chore` type + explicit commit-message circuit breaker in the pipeline) on top of the CI-level `[skip ci]` marker. That's the strongest pattern found anywhere in this research, ecosystem-wide or internal.

**Recommendation:** Copy all three layers for SixtyFiveXX: `skip_ci = "[skip ci]"` in `cog.toml`, a non-bumping `chore` commit type, and the `git log -1 --pretty=%s | grep -qE '^chore\(version\):'` circuit breaker in whichever Woodpecker step runs `cog bump`.

---

## 6. What does a good generated .NET release look like?

Three exemplars, all MinVer-based, all real, all fetched directly from GitHub:

**`jbogard/Respawn`** (Jimmy Bogard — also AutoMapper/MediatR author; well-regarded .NET OSS maintainer)
- `.csproj`: `MinVerTagPrefix=v`, `IncludeSymbols=true` + `SymbolPackageFormat=snupkg` (publishes symbol packages), `PublishRepositoryUrl=true` + `EmbedUntrackedSources=true` + `Deterministic=true` + `ContinuousIntegrationBuild` conditioned on `GITHUB_ACTIONS` (SourceLink-correct, reproducible build).
- Release flow: tag push (`*.*.*`) → build/test → push to a pre-release MyGet feed → push to NuGet.org. **No automated GitHub Release step at all** — Releases, if made, are a separate/manual action. `0.x` isn't really used; the project is well past 1.0.

**`khellang/Scrutor`** — same shape: `MinVerTagPrefix=v`, full SourceLink block, symbol packages, `PackageReadmeFile` embedding the README into the NuGet package page. Also no automated GitHub Release step observed.

**`Tyrrrz/CliWrap`** — doesn't use MinVer at all; uses a hand-rolled equivalent (placeholder `<Version>0.0.0-dev</Version>` in `Directory.Build.props`, real version injected via `-p:Version=` from a shared reusable workflow keyed off `github.ref_name` on tag push, or `0.0.0-ci-<sha>` on other pushes). `IsPackable=false` at the root with a per-project opt-in (`IsPackable=true` only in the actual library project) — clean separation between packable and non-packable projects in a multi-project repo, directly relevant since SixtyFiveXX has `src/`, `tests/`, and `bench/` projects and only one should ever be packed.

**Honest gap:** none of the three actually demonstrates "GitHub Release with attached `.nupkg`, published *before* NuGet" as an automated step — that specific combination (which is exactly what the owner wants) was **not found** in any real .NET OSS repository surveyed. It was found, fully implemented, in the owner's own `woodpecker-release` templates (for Go/static-site/macOS, not yet .NET). The `.snupkg`/SourceLink/deterministic-build hygiene, by contrast, **is** ecosystem consensus (both Respawn and Scrutor do it identically) and should be copied outright regardless of the release-ordering question.

**Recommendation:** Borrow packaging hygiene (SourceLink + snupkg + deterministic builds + `IsPackable=false` at root, `true` only on `src/SixtyFiveXX`) from Respawn/Scrutor verbatim; borrow the release-before-publish, draft-until-verified *ordering* from the owner's own NovaVM pipeline, since no .NET library example does that ordering as well or at all.

---

## 7. cog specifically with .NET

**Yes, but rare, and split into "owner's own repos" vs. "genuinely independent."**

*Owner's own prior art (primary source, most relevant):*
- `barryw/Novus/cog.toml` — `pre_bump_hooks` rewrites two `.csproj` files' `<Version>` via `sed -i`, with an explicit comment about the GNU-vs-BSD `sed -i` syntax gotcha (cog runs in a Linux container, so `sed -i 's/…/…/g' file`, not the BSD `sed -i ''` form).
- `barryw/NovaVM/cog.toml` — the more complete example, rewriting all four version-related MSBuild properties (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`) in one `Directory.Build.props`, plus the `chore`-must-not-bump lesson from §5.
- Both are C# projects, but neither publishes to NuGet — they're a CLI compiler and a game/emulator app respectively.

*Independent, non-owner evidence (via GitHub code search for `pre_bump_hooks` + `Directory.Build.props`, and `cog bump` + `.csproj`):*
- `keyz182/KeyzAllowUtils` (a RimWorld mod, `.csproj`-based .NET, GitHub Actions not Woodpecker) — real, independent, confirms cog+.NET isn't unique to this owner. Its `cog.toml` `pre_bump_hooks`:
  ```toml
  pre_bump_hooks = [
      "sed -i 's|<modVersion>.*</modVersion>|<modVersion>{{version}}</modVersion>|' About/About.xml",
      "dotnet build 1.6/Source/KeyzAllowUtilities/KeyzAllowUtilities.csproj -c Release -p:Version={{version}} -p:TargetFramework=net48",
      "dotnet build 1.6/Source/KeyzAllowUtilities.Multiplayer/KeyzAllowUtilities.Multiplayer.csproj -c Release -p:Version={{version}} -p:TargetFramework=net48",
      "git add About/About.xml 1.6/Assemblies/KeyzAllowUtilities.dll Compatibility/rwmt.Multiplayer/1.6/Assemblies/KeyzAllowUtilities.Multiplayer.dll",
  ]
  ```
  Notably, even this real independent example does **not** write `<Version>` into the `.csproj` — it passes `-p:Version=` as an MSBuild override at build time and commits the resulting compiled DLLs instead. So it satisfies "reproducible artifact matches the tag" but not "the version string is readable in the project file," which is the owner's actual requirement.

**Conclusion:** cocogitto+.NET pairings exist in the wild beyond this owner's own repos, but they're uncommon, and in every real example found (owner's and independent alike), the `pre_bump_hooks` script is bespoke `sed`/MSBuild-property-override glue, not a documented or reusable recipe — there is no cocogitto "dotnet plugin" or blessed community pattern the way there might be for, say, Rust/Cargo (cocogitto's home ecosystem, where `pre_bump_hooks` commonly runs `cargo set-version`). **This means the `pre_bump_hooks` script for SixtyFiveXX must be hand-written from first principles** (as the task anticipated), but "from first principles" here means "adapted from NovaVM's already-working four-property sed block," not invented from nothing.

---

## Summary recommendation for SixtyFiveXX

1. **Version-in-files mechanism:** cocogitto `pre_bump_hooks` rewriting `Directory.Build.props` via `sed`, staged with `git add`, exactly as in `barryw/NovaVM/cog.toml` — not MinVer/NBGV/GitVersion (all three fail the owner's file-mutation requirement).
2. **Tag/package consistency:** structural, not a bolted-on check — the hook runs inside the same `cog bump` operation that creates the tag, so file and tag can't drift. No extra CI assertion needed initially.
3. **Version properties:** `Version` floats every release (feeds `PackageVersion`); `AssemblyVersion` pinned to major-only per Microsoft's official guidance (a deliberate improvement over NovaVM's own every-release bump); `FileVersion` floats; `InformationalVersion` left to SourceLink, not hand-set (also an improvement over NovaVM's sed-based approach — less code, not more).
4. **Release ordering:** GitHub Release created as `--draft` when the tag is pushed, flipped to public only after `dotnet nuget push` succeeds — copying NovaVM's `build-gate`/`build-finalize` split, since no surveyed .NET library does this automated ordering at all.
5. **CI-loop avoidance:** three independent, mostly CI-agnostic layers — non-bumping `chore` commit type, `[skip ci]` cocogitto config, and a `git log -1` commit-message circuit breaker in the pipeline step itself — all copied directly from NovaVM's hard-won WAL-162 fix.
6. **Packaging hygiene:** SourceLink + `IncludeSymbols`/`SymbolPackageFormat=snupkg` + `Deterministic`/`ContinuousIntegrationBuild`, copied from Respawn/Scrutor, which is genuine, consistent ecosystem consensus for well-run NuGet libraries.
7. **cog+.NET pairing:** real but rare; no blessed community recipe exists, so the `pre_bump_hooks` script must be adapted from NovaVM's working example rather than found off-the-shelf.

### Sources
- [Versioning and .NET libraries — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning)
- [Cocogitto — Skip CI configuration](https://docs.cocogitto.io/guide/misc.html)
- [MinVer](https://github.com/adamralph/minver)
- [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)
- `barryw/Novus` — `cog.toml`, `Directory.Build.props`, `.woodpecker/01-bump.yml`, `.woodpecker/02-build.yml`
- `barryw/NovaVM` — `cog.toml`, `.woodpecker/ci.yaml`, `.woodpecker/build-gate.yaml`, `.woodpecker/build-finalize.yaml`
- `barryw/woodpecker-release` — `README.md`, `onboarding/cog.toml`
- `barryw/PaperlessMCP` — `cog.toml`, `version.json`
- `keyz182/KeyzAllowUtils` — `cog.toml`, `.github/workflows/release.yml`
- `Tyrrrz/CliWrap` — `Directory.Build.props`, `.github/workflows/main.yml`
- `jbogard/Respawn` — `Respawn/Respawn.csproj`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- `khellang/Scrutor` — `src/Scrutor/Scrutor.csproj`
- `CarterCommunity/Carter` — `.github/workflows/dotnetcore.yml`
- GitHub code search for `PackageReference Include="MinVer"` (real-world adoption survey) and `pre_bump_hooks` + `Directory.Build.props` (cog+.NET pairing survey)
