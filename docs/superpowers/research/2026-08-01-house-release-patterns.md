# House patterns for Woodpecker + cog + GitHub Releases

Survey of local repos with both `cog.toml` and Woodpecker config, done to establish
what SixtyFiveXX's CI/release pipeline should copy rather than invent from scratch.
Read-only research; nothing in any repo was changed.

Repos surveyed: `woodpecker-release`, `sim6502`, `NovaVM`, `NESBasic`, `Chess`,
`vaultwarden-bridge`, `terraform-aws-rds-scheduler`, `vice-macos`, `paperless-webdav`.

**Headline finding: no repo publishes to NuGet.** Every repo in this survey ships
either a GitHub Release with attached binaries, a GHCR Docker image, or both. There is
no local prior art for "push a package to a registry other than GHCR." The NuGet step
specifically has to come from external prior art (`dotnet nuget push`, the
`--skip-duplicate` flag, the NuGet API-key secret pattern), not from a house pattern —
say this plainly to the owner rather than stretching the Docker/GHCR examples to cover it.

**A pre-existing conflict to flag first.** SixtyFiveXX already has a design doc at
`docs/superpowers/specs/2026-08-01-ci-and-nuget-design.md` ("Status: design approved
2026-08-01") that proposes the **opposite** ordering from what this task specifies. Its
`scripts/release.sh` (section 4) does: cog bump → `dotnet pack` → `dotnet nuget push`
→ `git push --follow-tags` → `gh release create` — i.e. **NuGet before git push, and
NuGet before the GitHub Release**, justified as "the repository never claims a version
that failed to publish." That doc's own Secrets section even admits the resulting risk:
"if [the git push] does not hold, releases fail... after having already published to
NuGet — the one ordering the script cannot protect against." This is not what the two
most mature, most current C# repos in this org (NovaVM, NESBasic) actually do — see
below — and it is not what the owner asked for in this task. **This conflict needs to be
resolved with the owner before implementation**, not silently overridden. Flag it, don't
just pick a side.

---

## 1. The dominant `cog.toml` house style

The canonical, actively-maintained template lives at
`/Users/barry/Git/woodpecker-release/onboarding/cog.toml` and is quoted in that repo's
README as "the canonical `cog.toml`, copy-in for every repo":

```toml
from_latest_tag = true
ignore_merge_commits = true
branch_whitelist = ["main"]
tag_prefix = "v"
skip_ci = "[skip ci]"
skip_untracked = false

pre_bump_hooks = []
post_bump_hooks = []

[changelog]
path = "CHANGELOG.md"
template = "remote"
remote = "github.com"
owner = "barryw"
repository = "CHANGEME"

[commit_types]
feat = { changelog_title = "Features" }
fix = { changelog_title = "Bug Fixes" }
docs = { changelog_title = "Documentation" }
refactor = { changelog_title = "Refactoring" }
perf = { changelog_title = "Performance" }
test = { changelog_title = "Tests" }
build = { changelog_title = "Build" }
ci = { changelog_title = "CI/CD" }
style = { changelog_title = "Style" }
chore = { changelog_title = "Miscellaneous" }
revert = { changelog_title = "Reverts" }

[git_hooks.commit-msg]
script = """#!/bin/sh
set -e
cog verify --file $1
"""
```

Note the commit types here carry **no `bump_*` flags** — this relies on cocogitto's
default bump behavior (feat→minor, fix→patch, breaking→major) rather than declaring it
per type.

`terraform-aws-rds-scheduler` and `vice-macos` match this template close to verbatim
(only `repository` differs). Example (`terraform-aws-rds-scheduler/cog.toml`):

```toml
from_latest_tag = true
ignore_merge_commits = true
branch_whitelist = ["main"]
tag_prefix = "v"
skip_ci = "[skip ci]"
skip_untracked = false

pre_bump_hooks = []
post_bump_hooks = []

[changelog]
path = "CHANGELOG.md"
template = "remote"
remote = "github.com"
repository = "terraform-aws-rds-scheduler"
owner = "barryw"
```

**Deviation — explicit `bump_*` flags.** `NovaVM`, `NESBasic`, `paperless-webdav`, and
`vaultwarden-bridge` all set `bump_minor = true` / `bump_patch = true` explicitly on
each commit type instead of relying on cocogitto's defaults. This is the pattern used
by the two newest, most actively-developed C# repos, so treat it as the **current**
convention, superseding the older "no flags" onboarding template. NovaVM's version, with
a load-bearing comment on why `chore` must NOT bump (this is the loop-guard mechanism —
see §7):

```toml
[commit_types]
feat = { changelog_title = "Features", bump_minor = true }
fix = { changelog_title = "Bug Fixes", bump_patch = true }
perf = { changelog_title = "Performance", bump_patch = true }
refactor = { changelog_title = "Refactoring", bump_patch = true }
docs = { changelog_title = "Documentation", bump_patch = true }
style = { changelog_title = "Style", bump_patch = true }
test = { changelog_title = "Tests", bump_patch = true }
ci = { changelog_title = "CI/CD", bump_patch = true }
# `chore` must NOT bump: cog's own version commit is `chore(version): vX.Y.Z`,
# so a bumping `chore` makes every release commit re-trigger ci.yaml and cut the
# next patch — a runaway re-bump loop (WAL-162: v0.2.0→v0.2.1→v0.2.2→…). Keep it
# changelog-only. feat/fix/perf/etc. still cut releases.
chore = { changelog_title = "Miscellaneous" }
build = { changelog_title = "Build", bump_patch = true }
```

`skip_ci`: present and set to `"[skip ci]"` in `woodpecker-release`,
`terraform-aws-rds-scheduler`, `vice-macos`, `vaultwarden-bridge`. **Absent** in
`NovaVM` and `NESBasic` cog.toml — those two rely entirely on the `chore`-doesn't-bump
mechanism plus an explicit HEAD-commit-message check in the pipeline itself (see §7),
not on cog's own `skip_ci` field. `sim6502` sets `skip_ci = "[CI SKIP]"` (different
marker casing/text — an outlier, and it isn't actually the loop-guard there either; see
§7).

`bump_commit_message`: **not set explicitly anywhere** in any surveyed `cog.toml`.
Everyone relies on cocogitto's default (`chore(version): {{version}}` — visible in
release notes as `(**version**) v3.0.2 [skip ci]` in the terraform-aws-rds-scheduler
release body, and referenced directly by pattern-match in NovaVM/NESBasic pipelines:
`git log -1 --pretty=%s | grep -qE '^chore\(version\):'`).

`pre_bump_hooks` vs `post_bump_hooks` for version-file rewriting — split down the
middle; see §2, this is the important one.

`[changelog]` `template = "remote"` + `remote/owner/repository` fields: used by
`woodpecker-release`, `terraform-aws-rds-scheduler`, `vice-macos`, `paperless-webdav`.
**Not used** by `NovaVM`/`NESBasic` (they just set `path` and `authors = []`) or
`sim6502` (just `path`). The `remote` template makes changelog entries link to GitHub
commits/authors — worth keeping, it's what produces the `([3672b1f](https://github.com/...))`
markdown links seen in real release notes (§5).

`[git_hooks.commit-msg]` with `cog verify --file $1`, installed via
`cog install-hook --all`: universal across every repo that has this block
(`woodpecker-release`, `NovaVM`, `NESBasic`, `terraform-aws-rds-scheduler`,
`vice-macos`, `paperless-webdav`). `sim6502`, `Chess`, `vaultwarden-bridge` don't
have this block (older repos, predate the convention or never adopted it).

`ignore_merge_commits`: `true` everywhere except `Chess` (`false`) and it's simply
absent from `sim6502`'s cog.toml (defaults apply).

`branch_whitelist = ["main"]`: universal in every repo that sets it at all. `Chess` sets
`branch_whitelist = []` (unrestricted — an outlier, and Chess's cog config generally
looks like an early, less-hardened example: `ignore_merge_commits = false`, every commit
type including `chore`/`revert` bumps, `tag_prefix = ""` with no `v`).

---

## 2. How the version reaches the project files — THE key mechanism

**Universal answer: `sed` against `<Version>`/`<AssemblyVersion>`/`<FileVersion>`
(and sometimes `<InformationalVersion>`) in a project file, invoked as a cog bump
hook.** No repo uses MinVer, Nerdbank.GitVersioning, GitVersion, or `-p:Version=` at
pack time as the primary mechanism — `-p:Version=` only shows up as a redundant
belt-and-braces re-application inside individual build/publish steps in `sim6502`,
not as the source of truth.

**Two competing conventions for *where* the sed runs, and *what file* it targets:**

**(a) `post_bump_hooks` against the `.csproj` directly — the older pattern.**
`sim6502/cog.toml`:

```toml
post_bump_hooks = [
    "sed -i 's|<Version>.*</Version>|<Version>{{version}}</Version>|' sim6502/sim6502.csproj",
    "sed -i 's|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>{{version}}.0</AssemblyVersion>|' sim6502/sim6502.csproj",
    "sed -i 's|<FileVersion>.*</FileVersion>|<FileVersion>{{version}}.0</FileVersion>|' sim6502/sim6502.csproj",
]
```
Because this is a `post_bump_hook`, the rewritten file is **not** part of cog's own
commit — sim6502's pipeline re-applies the identical sed a second time, by hand, inside
each of `build-binaries`, `docker-publish` (and implicitly relies on it not mattering
for the tag itself, since the version file is cosmetic there, driven off the GitHub
API's already-created tag instead of the git state).

**(b) `pre_bump_hooks` against `Directory.Build.props`, explicitly staged — the
current pattern.** This is what both `NovaVM` and `NESBasic` (the newest C# repos,
and NESBasic's `Directory.Build.props` even carries a header comment declaring it
CI-managed) do, verbatim except for repo name:

```toml
# Pre-bump: sync the new version into Directory.Build.props (.NET assembly
# metadata) and stage it so the bump commit/tag carries the right version.
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

Because this runs **pre**-bump and explicitly `git add`s the file, the version-file
edit is folded into cog's own `chore(version)` commit, atomically, before the tag is
cut — the tag and the committed `<Version>` can never disagree. This is strictly better
than sim6502's post-hook approach (which leaves the rewrite as an untracked/manual
side effect) and is the mechanism to copy. `NESBasic/Directory.Build.props` documents
this contract inline:

```xml
<!--
  Assembly version metadata for the whole solution.

  These values are CI-managed: cog bump rewrites them from the next semantic
  version on each release (see cog.toml pre_bump_hooks). Do not edit them by
  hand. The source of truth for the current version is the latest `v` git tag
  / GitHub Release.
-->
```

SixtyFiveXX already has a `Directory.Build.props` at the repo root — the (b) mechanism
targets exactly that file with zero structural change needed, just add the `<Version>`
etc. properties and the `pre_bump_hooks` block. This directly contradicts the existing
`2026-08-01-ci-and-nuget-design.md` spec's approach (§3 of that doc: "`<Version>` is
deliberately absent [from the csproj]. It is supplied by `scripts/release.sh`... via
`-p:Version=`"), which is a third, no-local-precedent mechanism.

`vaultwarden-bridge` (Rust) does the Cargo-native equivalent for comparison —
`pre_bump_hooks` sedding `version = "..."` in each `Cargo.toml`, plus a `cargo check`
sanity pass. `Chess` (6502 assembly) sat it into `version.asm` via a small helper
script, `pre_bump_hooks = ["scripts/update-version.sh {{version}}"]`. Same shape,
different file format — confirms this is a house-wide idiom, not C#-specific.

---

## 3. The pipeline shape

**Two structurally different eras, both live today:**

**Era A — one flat `.woodpecker.yml`, version bump AND release happen in the same
push-to-main pipeline run.** Used by `sim6502`, `Chess`, `paperless-webdav`. Steps run
serially with `depends_on`, gated with a top-level `when: event: push, branch: main`
plus (in sim6502) an `evaluate:` clause per release-ish step to skip the bump commit's
own re-run.

**Era B — split `.woodpecker/*.yaml` files, version bump and release are separate
pipeline *runs* chained by a git tag.** Used by `NovaVM`, `NESBasic`,
`woodpecker-release`'s own templates, and any repo onboarded via the shared config
service (`terraform-aws-rds-scheduler`, `vice-macos`). This is the current convention;
NovaVM's file split:

```
build-gate.yaml     when: event: tag              — retest, gh release create --draft
build-linux.yaml    when: event: tag, depends_on: [build-gate]  — publish, package, upload
build-macos.yaml    when: event: tag, depends_on: [build-gate]  — (same, macOS)
build-finalize.yaml when: event: tag, depends_on: [build-linux, build-macos] — un-draft
ci.yaml             when: event: push, branch: main — build+test, cog bump, push tag
```

NESBasic's equivalent, three files instead of five (no per-OS split, single matrix
step instead):
```
ci.yaml             when: push, branch: main   — build+test, cog bump --auto, push tag
release-gate.yaml    when: tag                  — retest, gh release create (published directly, no draft)
release-publish.yaml when: tag, depends_on: [release-gate], matrix RID: [linux-x64, linux-arm64, osx-arm64, win-x64] — publish+zip+upload
```

Every Era-B pipeline uses a full, tagged clone explicitly (Woodpecker shallow-clones by
default):
```yaml
clone:
  git:
    image: woodpeckerci/plugin-git
    settings:
      depth: 0
      tags: true
```

**Release is gated to `main` via `when: branch: main` on the push-triggered pipeline
that performs the bump** (not via a separate ACL) — `branch_whitelist = ["main"]` in
`cog.toml` is the second, independent enforcement of the same rule (cog itself refuses
to bump off a feature branch even if the pipeline trigger were misconfigured).

**Images.** No repo builds a custom baked-in CI image with cog/gh pre-installed baked
at the OS layer (contrary to what SixtyFiveXX's existing design doc proposes with
`docker/ci.Dockerfile` → `ghcr.io/barryw/sixtyfivexx-ci:1`). The current C# pattern
(NovaVM, NESBasic) runs stock `mcr.microsoft.com/dotnet/sdk:10.0` and installs cog/gh
on demand, per step, via a small checked-in shell script
(`.woodpecker/install-linux-ci-deps.sh` in NESBasic) that takes a mode argument
(`build`/`release`/`publish`) and installs only what that step needs, pinned by
version variables at the top:
```sh
COG_VERSION="6.5.0"
GH_VERSION="2.74.0"
```
This is a real deviation point to raise with the owner: SixtyFiveXX's own draft design
prefers a baked custom image (defended there as avoiding "a broken image [that] fails
... mid-pipeline"); the house's own newest repos prefer the disposable-install-script
pattern instead. Both are defensible; they are not the same convention, and the design
doc's choice has no local precedent.

There **is** a shared, reusable component that half the surveyed repos already lean
on: `ghcr.io/barryw/woodpecker-release` (see §5) plus a companion **config service**
that expands a 3-18 line `.woodpecker/woodpecker-template.yaml` into a full generated
pipeline. `terraform-aws-rds-scheduler` and `vice-macos` (and NovaVM's static-site leg)
use this — their entire release config is e.g.:
```yaml
template: release-terraform
data:
  terraform_version: "1.14"
  python_version: "3.12"
  docs_check: true
```
There is **no template for a .NET/NuGet-publishing library** in the config service
(`config-service/templates/` only has `release-tag-only`, `release-go-library`,
`release-go-binary`, `release-docker`, `release-terraform`, `release-macos-app`,
`release-static-site`). This is the biggest structural gap for SixtyFiveXX: it can't
just drop in a `template:` line the way the Terraform/Go/Docker repos do.

---

## 4. Release ordering — GitHub Release before artifact publish

**Confirmed as the house pattern in every actively-used mechanism except the two
oldest bespoke pipelines (`sim6502`-era inline scripts and `paperless-webdav`).**
Evidence, most-to-least authoritative:

**`woodpecker-release` plugin itself** (`plugin/entrypoint.sh`) — this is the shared,
maintained component, and its step order is explicit and commented:
```
# --- Step 2: Version bump (all modes) ---
NEW_VERSION=$(cog_bump) ...
# --- Step 3: Push commit and tag (all modes) ---
git_push_commit
git_push_tag "$NEW_VERSION"
# --- Step 4: Generate changelog (release modes) ---
changelog_generate "$NEW_VERSION" "$CHANGELOG_FILE"
# --- Step 5: Build artifacts (mode-specific) ---
case "$MODE" in
  release-go) go_build_all ... ;;
esac
# --- Step 6: Create GitHub Release ---
github_release_create "$NEW_VERSION" "$CHANGELOG_FILE"
# Upload artifacts if any exist
if [ -d "dist" ] ...; then
  github_release_upload "$NEW_VERSION" dist/*
fi
```
So even for artifacts *built inside the same plugin invocation*, `github_release_create`
runs before `github_release_upload` — release-then-attach, not publish-then-announce.

**`release-docker` template** (`config-service/templates/release-docker/pipeline.yaml.template`)
makes the ordering a hard pipeline dependency, not just script-internal sequencing:
```yaml
  - name: release
    ...
    depends_on: [validate-commits, test]

  - name: docker-build
    ...
    depends_on: [release]
```
The Docker image (the artifact-registry publish, GHCR's closest local analogue to
NuGet) cannot start until the `release` step — which creates the GitHub Release —
has completed.

**NovaVM / NESBasic** (the two current C# repos) split this across pipeline *runs*
rather than steps, but the dependency is the same: `build-gate`/`release-gate`
(creates the Release, draft in NovaVM's case) must complete before
`build-linux`/`build-macos`/`release-publish` (the artifact publish + upload) can run —
enforced by `depends_on: [build-gate]` / `depends_on: [release-gate]` at the pipeline
level.

**Deviations (older/bespoke, not the pattern to copy):**
- `sim6502/.woodpecker.yml` — `release` step (creates the GH release) has
  `depends_on: [build-and-test, example-suite, grammar-sync]`, and
  `build-binaries`/`docker-publish`/`docker-amd64` all have `depends_on: [release]`
  or chain off it — so sim6502 actually **also** does release-before-artifact, despite
  being the oldest inline-script style. Consistent with the rest, just not via the
  shared plugin.
- `paperless-webdav/.woodpecker.yml` — the **only repo that does it backwards**:
  `docker-build` → `release` (`depends_on: [docker-build]`). This is flagged explicitly
  as the outlier, not the pattern.
- `vaultwarden-bridge/.woodpecker/release.yml` — `bump` pushes the tag, then
  `docker-bridge`/`docker-bw-serve` build immediately (`depends_on: [bump]`); there is
  no GitHub Release step in this repo at all (Rust crate isn't published anywhere
  either — it's GHCR-only, no crates.io).

**Net: for a NuGet publish specifically, no repo has ever ordered "artifact publish"
before "GitHub Release" as the *intended* house pattern** — the one repo that does
(`paperless-webdav`) reads as an unremediated gap, not a deliberate choice, and it
predates the GHCR release-before-publish convention seen everywhere else. This
directly supports the owner's instruction (GitHub Release before NuGet publish) and
directly contradicts SixtyFiveXX's own draft `ci-and-nuget-design.md`.

---

## 5. How the GitHub Release is created

**Universally `gh release create`, invoked from inside a step running on the same
runner** — no repo uses a dedicated Woodpecker release/GitHub plugin for this. Two
notes-generation strategies:

**(a) `cog changelog --at <tag>` piped to `--notes-file`, falling back to
`--generate-notes`.** This is what `NovaVM`, `NESBasic`, and the `woodpecker-release`
plugin itself all do. NESBasic's version:
```sh
if cog changelog --at "$${TAG}" > release-notes.md 2>/dev/null && [ -s release-notes.md ]; then
  gh release create "$${TAG}" --repo "$$CI_REPO" --title "$${TAG}" --notes-file release-notes.md
else
  gh release create "$${TAG}" --repo "$$CI_REPO" --title "$${TAG}" --generate-notes
fi
```
Real output (`gh api repos/barryw/NESBasic/releases --jq '.[0].body'`):
```
## v0.2.0 - 2026-07-04
#### Features
- add vertical streaming and timing tools - (af17d1d) - Barry Walker
#### Miscellaneous
- (**version**) v0.2.0 - (c445466) - Woodpecker CI
```
With `[changelog] template = "remote"` set (terraform-aws-rds-scheduler), the same
mechanism links commits/authors:
```
## [v3.0.2](https://github.com/barryw/terraform-aws-rds-scheduler/compare/...)  - 2026-03-22
#### Bug Fixes
- add ARN overrides for Lambda and CloudWatch resources in tests - ([3672b1f](https://github.com/.../commit/3672b1f...)) - Barry Walker
...
```

**(b) Hand-written prose release notes**, bypassing `cog changelog` entirely — seen in
`sim6502`'s latest release (v4.0.1), which reads as authored documentation (usage
examples, "Known issue," "Behaviour changes") rather than a commit list. This is a
one-off editorial choice for a release that needed explaining, not a competing
mechanism to copy.

**Idempotency**: every `gh release create` call checks first (`gh release view` or
grep on stderr) and treats "already exists" as success, not failure — necessary because
Era-B pipelines can legitimately re-run the tag-triggered steps.

**Draft vs published — a real deviation, and an observed bug.** NovaVM creates the
release as `--draft`, intending `build-finalize.yaml` to flip it live once both
per-OS artifact legs finish (`gh release edit "$$CI_COMMIT_TAG" --draft=false`).
**Checking live state: every one of NovaVM's last 5 releases is still sitting in
Draft** (`gh release list -R barryw/NovaVM`), meaning `build-finalize` is not
completing successfully in practice — a real operational gap in the pattern, not a
theoretical one. NESBasic does not use a draft stage at all — `release-gate.yaml`
calls `gh release create` without `--draft`, so the release is public and complete
before the binaries even attach; its releases confirm this (all "Latest"/published,
no draft state). **Recommendation: skip the draft/finalize two-step for SixtyFiveXX**
— it's unproven in production and NESBasic's simpler immediately-published-then-upload
approach is what's actually working.

**Assets**: attached via `gh release upload <tag> <files...> --repo <repo> --clobber`
— universal across every repo that attaches binaries (NovaVM, NESBasic, sim6502,
Chess, the `woodpecker-release` plugin's `github_release_upload`). `--clobber` is
always present, to make asset upload idempotent across pipeline re-runs same as the
release-create step above it.

---

## 6. Secrets

**`github_token`** is the near-universal secret name for the token used by both
`git push` and `gh release create/upload` — present in `woodpecker-release`'s own
onboarding table, `terraform-aws-rds-scheduler`, `vice-macos`, `sim6502`, `Chess`,
`paperless-webdav`, `vaultwarden-bridge` (this last one via `github_token` too, plus a
separate `github_username` for the GHCR login). Referenced the same way everywhere:
```yaml
environment:
  GITHUB_TOKEN:
    from_secret: github_token
```
or, for the plugin, `PLUGIN_GITHUB_TOKEN: from_secret: github_token` (the plugin's
entrypoint exports this as `GH_TOKEN` for the `gh` CLI to pick up automatically).

**Deviation, and the newest wrinkle: GitHub App installation-token minting.** `NovaVM`
and `NESBasic` (the two most recently touched C# repos) have moved off a shared admin
PAT and mint a short-lived (~1h), scope-limited token per pipeline run instead, via a
`mint-token` step that reads three org-level secrets:
```yaml
environment:
  GH_APP_ID: {from_secret: gh_app_id}
  GH_APP_INSTALLATION_ID: {from_secret: gh_app_installation_id}
  GH_APP_PRIVATE_KEY: {from_secret: gh_app_private_key}
commands:
  - /plugin/mint-installation-token.sh --profile product-ci > "$CI_WORKSPACE/.ci-token"
```
scoped by a named "profile" (`product-ci` = `contents=write, metadata=read`). **Caveat:
`mint-installation-token.sh` is referenced from `ghcr.io/barryw/woodpecker-release:go`
but does not exist anywhere in the local `woodpecker-release` checkout** (confirmed via
`git log`/`git status` — the local clone is up to date with its own origin, so this
script lives only in a built image, not in this repo's tracked source). Treat this as
an in-flux, not-yet-fully-documented mechanism — don't try to reproduce it for
SixtyFiveXX; the plain `github_token` secret pattern is the one with a complete,
inspectable interface and is still what the shared plugin's own README documents.

**Other secrets seen, all narrowly scoped to what they gate:**
- `ghcr_token` / `ghcr_username` — GHCR push (paperless-webdav, woodpecker-release's
  `release-static-site` template)
- `gpg_private_key` / `gpg_fingerprint` — only for `release-go-binary` with
  `gpg_sign: true`
- `cloudflare_api_token` + a per-repo zone-id secret — static-site cache purge only

**For SixtyFiveXX, by direct analogy: `nuget_api_key`** would be the natural house-
consistent name (matches the `snake_case`, tool-named pattern of `github_token`,
`ghcr_token`, `gpg_private_key` — not `NUGET_API_KEY` or `nuget_token`).
SixtyFiveXX's own draft design doc already proposes exactly `nuget_api_key`, which is
consistent with this naming convention.

---

## 7. Preventing the CI-push-back re-trigger loop

**No single mechanism — three different ones are in live use, and they are not
interchangeable.** This is worth getting right; NovaVM's own cog.toml comment
documents a real incident (WAL-162, an actual infinite bump loop that shipped
v0.2.0→v0.2.1→v0.2.2→… before being caught).

**(a) Non-bumping `chore` type — the primary guard in the current (NovaVM/NESBasic)
pattern.** Cog's own bump commit is `chore(version): vX.Y.Z`. If `chore` is configured
to bump, that commit re-triggers the pipeline, which sees a new "chore" commit and
bumps again, forever. Fix: `chore` gets a `changelog_title` but no `bump_patch`/
`bump_minor` flag (see the quoted cog.toml block in §1).

**(b) Defense-in-depth: explicit HEAD-commit check inside the pipeline script
itself**, independent of (a) — belt-and-braces in case (a) is ever misconfigured.
NovaVM's `version` step:
```sh
# Circuit breaker (defense in depth with cog.toml's non-bumping `chore`):
# never re-bump on cog's own version commit, or ci.yaml would loop
# cutting patch releases (WAL-162).
if git log -1 --pretty=%s | grep -qE '^chore\(version\):'; then
  echo "HEAD is a bot version commit — skipping bump"
  exit 0
fi
```

**(c) `[skip ci]` marker, either in the commit message (cog.toml `skip_ci` +
`bump_commit_message` embeds it) or as a Woodpecker `when.evaluate` guard on the
pipeline.** This is what the *older* pattern uses instead of (a)/(b):
- `woodpecker-release` plugin: `cog bump` is invoked with `--skip-ci`
  (`plugin/lib/cog.sh`: `bump_args="--auto $bump_args"` where `bump_args` always
  includes `--skip-ci`), and the plugin then **verifies** the resulting commit actually
  contains the marker before trusting it:
  ```sh
  if ! echo "$commit_msg" | grep -qF "$skip_ci_marker"; then
    echo "ERROR: Bump commit does not contain '${skip_ci_marker}'. Check cog.toml skip_ci setting." >&2
    return 1
  fi
  ```
- `sim6502`: top-level `when.evaluate: 'CI_COMMIT_MESSAGE not contains "chore(version):"'`
  — actually closer to mechanism (b) in spirit (matching on the commit-message prefix,
  not a `[skip ci]` token), despite `cog.toml` separately declaring `skip_ci = "[CI SKIP]"`
  which appears to be vestigial/unused by the pipeline's actual guard.
- `vaultwarden-bridge`: `when.evaluate: 'not (CI_COMMIT_MESSAGE contains "[skip ci]")'`,
  consistent with its `cog.toml`'s `skip_ci = "[skip ci]"`.

**Recommendation for SixtyFiveXX: use (a) as the primary mechanism (it's structurally
the strongest — the commit that could cause the loop simply can't trigger a bump,
full stop) plus (b) as a cheap second guard in the release script**, matching what
NovaVM/NESBasic actually run in production today. Skip (c)/`skip_ci` — it's the older
convention, redundant with (a)+(b), and sim6502's own cog.toml shows it can silently
stop being the thing that's actually load-bearing. **This contradicts SixtyFiveXX's
existing draft design doc**, which relies on `[skip ci]` alone (§2 of that doc: "This
is the loop guard, and it is load-bearing... has never been exercised on this instance"
— the draft doc itself flags this as unverified risk, and (a)+(b) sidesteps needing to
verify Woodpecker's skip-marker handling at all).

---

## 8. Things not explicitly asked about, worth knowing

- **`cog install-hook --all` is a documented, repeated onboarding step**, not just a
  cog.toml block — every repo with the `[git_hooks.commit-msg]` section also documents
  running this once per clone (README or a dedicated `docs/build-and-release.md`).
  Git hooks aren't cloned, so this is manual per-developer setup; NESBasic's doc calls
  it out as step 1.
- **A dedicated `docs/build-and-release.md`** (NESBasic) or equivalent inline
  documentation exists in the newer repos, explicitly narrating the release sequence
  for humans, separate from the YAML. Worth having one for SixtyFiveXX too — the
  existing `2026-08-01-ci-and-nuget-design.md` mostly serves this purpose already, once
  the ordering conflict (see top) is resolved.
- **NESBasic/NovaVM reference an org-wide "Walker Heavy Industries build & release
  standard (WAL-32)"** pointing at `github.com/barryw/whi-brand`. That repo exists on
  GitHub but its `docs/` only covers design-system/branding material
  (`design-system.md`, `toolchain-cli-conventions.md`) — **no build-and-release
  standard doc was found there**. The "standard" appears to live only as convention
  embedded in each repo's `cog.toml`/pipeline comments (which is what this survey
  reconstructed from), not as a single canonical written spec. Don't go looking for a
  master doc that would settle every open question here — it doesn't exist locally or
  (as far as `gh api`) on GitHub.
- **First-tag bootstrap is a real, recurring gotcha**, documented independently in
  three places: `woodpecker-release`'s README ("Handle existing version tags" — manual
  `git tag`+push before first CI run if the repo has prior tags from another system),
  `NESBasic`'s cog.toml comment ("no existing tags, first bump starts at v0.1.0"), and
  SixtyFiveXX's own draft design doc (§ Bootstrap: `cog bump --version 0.2.0` seeded
  by hand because `--auto` would compute `0.1.0` from a from-scratch history). Keep this
  bootstrap step in whatever plan gets written.
- **Version-bump commits are excluded from the changelog itself**
  ("`chore(version)` bump commit suppressed from the changelog" is explicit intent in
  SixtyFiveXX's draft doc) but this isn't automatic — cocogitto includes it by default
  (see the `(**version**) v0.2.0 - ... - Woodpecker CI` line in the real NESBasic
  release body above). If suppressing it matters, that needs an explicit
  `omit_from_changelog`-style config or changelog post-processing; none of the
  surveyed repos actually suppress it in practice — the "Miscellaneous" section always
  contains the bump commit itself.
- **GHCR packages default to private and must be made public by hand once** — called
  out as a known footgun in `woodpecker-release`'s README and independently in
  SixtyFiveXX's draft doc. Not applicable to NuGet directly, but the same
  "first-run-fails-in-a-confusing-way" shape likely applies to an un-verified/expired
  `nuget_api_key` — SixtyFiveXX's draft doc already anticipates this ("nuget.org caps
  keys at 365 days and an expired key fails only at release time").
  No test-gating deviation worth noting — every repo surveyed runs its full test suite
  before the release/bump step, on every push, not just on release; SixtyFiveXX's
  existing `.woodpecker.yml` already does the same (build → conformance → performance).
- No repo uses a PR template, `CONTRIBUTING.md` enforcement, or branch-protection
  automation — `cog check`/`cog verify` (client + CI side) is the only enforced gate.
  SixtyFiveXX's draft doc mentions a not-yet-written `CONTRIBUTING.md` for
  `cog install-hook --all`; no repo surveyed actually has a CONTRIBUTING.md file for
  this despite several documenting the hook-install step in READMEs instead — a CI
  doc/README section, not a dedicated CONTRIBUTING.md, is the actual pattern.

---

## Recommendation for SixtyFiveXX

**Copy verbatim:**
1. `cog.toml` shell: `from_latest_tag = true`, `ignore_merge_commits = true`,
   `branch_whitelist = ["main"]`, `tag_prefix = "v"`, `[git_hooks.commit-msg]` block,
   `[changelog] template = "remote"` + owner/repository. Use explicit `bump_minor`/
   `bump_patch` flags per commit type (current convention), with **`chore` deliberately
   non-bumping** as the primary loop guard (§7a).
2. Version mechanism: `pre_bump_hooks` sedding `<Version>`/`<AssemblyVersion>`/
   `<FileVersion>`/`<InformationalVersion>` into the existing `Directory.Build.props`,
   followed by `git add Directory.Build.props` in the same hook list — exact NovaVM/
   NESBasic pattern, zero new files needed.
3. Split `.woodpecker/*.yaml` (Era B), full clone with `depth: 0, tags: true`, a
   push-to-main pipeline that builds/tests/bumps/pushes the tag, and a tag-triggered
   pipeline that creates the GitHub Release **before** any artifact-publish step,
   enforced with `depends_on:` — this is the one place local precedent and the owner's
   explicit instruction agree completely.
4. `gh release create` with `cog changelog --at <tag>` piped to `--notes-file`,
   falling back to `--generate-notes`; publish directly (skip NovaVM's draft/finalize
   two-step — it's demonstrably not working in production, see §5).
5. Secret naming: `github_token`, `nuget_api_key` (already what the existing draft doc
   proposes, and it's consistent with house naming).
6. Loop guard: non-bumping `chore` (§7a) plus a defense-in-depth HEAD-commit check in
   the release script (§7b). Skip `[skip ci]` — no local repo has it as the sole,
   verified mechanism.

**Do differently from the existing `2026-08-01-ci-and-nuget-design.md` draft (raise
with the owner explicitly, don't silently overrule):**
- **Ordering**: that draft publishes to NuGet *before* pushing git / creating the
  Release. Every current house pattern (and the owner's stated instruction for this
  task) does the opposite: tag+push → GitHub Release → artifact publish. The draft's
  own stated rationale (avoid claiming a version that failed to publish) is real, but
  the house's actual answer to that risk is `--skip-duplicate`-style idempotency plus
  re-run safety, not reordering — see the plugin's and NESBasic's "already exists,
  skip" checks throughout.
- **Version mechanism**: the draft keeps `<Version>` absent from the csproj and
  supplies it via `-p:Version=` at pack time. No local repo does this; every C# repo
  bakes the version into `Directory.Build.props` via a pre-bump hook instead (§2), and
  SixtyFiveXX already has the exact file this pattern targets.
- **CI image**: the draft proposes a custom baked GHCR image
  (`sixtyfivexx-ci:1`). The current C# house pattern (NovaVM/NESBasic) instead runs
  the stock `mcr.microsoft.com/dotnet/sdk:10.0` image and installs `cog`/`gh`/etc.
  on-demand per step via a small pinned-version shell script. Either can work; the
  house's newest repos chose the latter, and it avoids the "GHCR package defaults to
  private" footgun the draft doc itself calls out as a risk for its own custom image.

**Genuinely new territory, no local precedent to lean on:**
- `dotnet nuget push` usage, `--skip-duplicate`, and the nuget.org API-key secret
  lifecycle (365-day cap) all need to come from NuGet's own docs/prior art — nothing
  in this survey validates or contradicts those specifics, because nothing local has
  ever done it.
