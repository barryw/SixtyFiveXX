# SixtyFiveXX Release Engineering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A merge to `main` carrying a `feat:` or `fix:` stamps the new version into SixtyFiveXX's project files, tags it, cuts a GitHub Release with generated notes, and then publishes the matching package to nuget.org.

**Architecture:** SixtyFiveXX onboards onto the existing `woodpecker-release` infrastructure rather than growing a bespoke pipeline. That infrastructure has no .NET template and nothing in the suite publishes to NuGet, so this plan spans **two repositories**: Part A adds a reusable `release-dotnet-library` template (plus draft-release support in the shared plugin), and Part B onboards SixtyFiveXX to it.

**Tech Stack:** Woodpecker CI, cocogitto (`cog` 7.0.0 local / `ghcr.io/cocogitto/cog:6.2.0` in CI), bash + bats, Go `text/template`, .NET 10 SDK, 64tass, `gh` CLI, Docker/GHCR.

**Spec:** `docs/superpowers/specs/2026-08-01-ci-and-nuget-design.md`

## Repositories

| Repo | Path | Part |
| --- | --- | --- |
| `woodpecker-release` | `/Users/barry/Git/woodpecker-release` | A (Tasks 1–3) |
| `SixtyFiveXX` | `/Users/barry/Git/SixtyFiveXX` | B (Tasks 4–8) |

**Both repos use conventional commits and have `cog.toml` with `branch_whitelist = ["main"]`.** Commit messages in both must be conventional or `cog check` fails the pipeline.

## Global Constraints

- **Conventional Commits, always.** `feat:` → minor, `fix:` → patch, `feat!:`/`BREAKING CHANGE` → major. `docs:`/`chore:`/`refactor:`/`test:`/`ci:`/`build:`/`style:` do not bump.
- **`SixtyFiveXX/src/SixtyFiveXX` has zero NuGet dependencies.** SourceLink ships in the SDK from .NET 8 — do not add a `PackageReference` for it.
- **Warnings are errors** across SixtyFiveXX (`TreatWarningsAsErrors` in `Directory.Build.props`). Every public member of `src/SixtyFiveXX` needs an XML doc comment.
- **The GitHub Release is created before the package is published**, enforced by `depends_on:`. nuget.org is append-only: a version can be unlisted, never deleted or replaced.
- **Releases only ever happen from `main`** — `when: event: push, branch: main` on the release step, and `branch_whitelist = ["main"]` in `cog.toml`.
- **The version in the tag, in `Directory.Build.props`, and in the published package must be the same value by construction**, never re-derived independently.
- **Do not modify `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm`.** It is a byte-faithful port of Klaus Dormann's GPL-3.0 test, verified character-for-character against upstream.
- House secret names are `github_token`, `nuget_api_key`, `ghcr_username`, `ghcr_token`. Do not invent new names.
- The house `cog.toml` is copied verbatim from `woodpecker-release/onboarding/cog.toml`; only `[changelog].repository` and `*_bump_hooks` may differ per repo.

## Established facts — verified before this plan was written, do not re-derive

- **The plugin writes the new version to `/woodpecker/version.txt`**, and writes the literal string `NONE` and exits 0 when no bump was needed (`plugin/entrypoint.sh:43-62`). Downstream steps detect a no-op with:
  ```bash
  VERSION=$(cat /woodpecker/version.txt 2>/dev/null || echo "NONE")
  test "$VERSION" != "NONE" || { echo "No new version, skipping"; exit 0; }
  ```
- **The plugin's `release-tag` mode already does** git config → full history → `cog bump` → write version file → push commit → push tag → generate changelog → create GitHub Release. It needs no change except draft support.
- **`plugin/lib/github_release.sh` has no draft support.** `github_release_create()` builds an args array and calls `gh release create`. It already auto-detects prereleases from the version string and already has a `github_release_upload()` helper using `gh release upload --clobber`.
- **NovaVM proves the draft-until-verified pattern** in hand-written pipelines: `gh release create --draft` (`.woodpecker/build-gate.yaml:53-64`) then `gh release edit "$TAG" --draft=false` (`.woodpecker/build-finalize.yaml`). It is not yet in the shared plugin.
- **Config-service templates are Go `text/template`**, one directory per template under `config-service/templates/<name>/`, files named `*.yaml.template`. `config-service/src/template.go` reads the directory named by the `template:` field and renders every `*.yaml.template` in it. There is no allow-list of template names in code — the directory's existence *is* the validation.
- **Templates parameterise images**, e.g. `image: {{or .test_image "python:3.12-slim"}}`, and inject `{{range .setup_commands}}` / `{{range .test_commands}}` blocks (`release-docker`).
- **`clone: git: {tags: true, depth: 0}`** appears in every template; cog needs full history and tags.
- **The plugin has a bats test suite** at `plugin/test/*.bats` covering `changelog`, `cog`, `git`, `github_release`.
- **`cog changelog --at <tag>` emits exactly one version's section.** The tag argument must include the `v` prefix, matching `tag_prefix = "v"`.
- **SixtyFiveXX compiles clean on `net8.0`** — proven by building both TFMs under `TreatWarningsAsErrors`: zero warnings, no `#if`.
- **SixtyFiveXX has no git tags**, so cog starts clean at `v0.1.0` per `woodpecker-release/README.md` Step 2.
- **`apt-get install -y --no-install-recommends 64tass` works** in `mcr.microsoft.com/dotnet/sdk:10.0`, verified end-to-end in Docker.
- **The SDK 10 image has no .NET 8 runtime**, so `dotnet test` against the `net8.0` TFM fails without one being installed.
- **`IsPackable=false`** is already set on `tests/SixtyFiveXX.Tests`, `tests/SixtyFiveXX.Conformance` and `bench/SixtyFiveXX.Benchmarks`.
- **NovaVM's working .NET stamp hook** (production, at 0.36.0):
  ```toml
  pre_bump_hooks = [
      """sed -i.bak -E \
        -e 's|<Version>[^<]*</Version>|<Version>{{version}}</Version>|' \
        -e 's|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>{{version}}.0</AssemblyVersion>|' \
        ... Directory.Build.props && rm -f Directory.Build.props.bak""",
      "git add Directory.Build.props",
  ]
  ```
  SixtyFiveXX cannot use this verbatim — see Task 4.

## Noted, deliberately out of scope

NovaVM has moved to short-lived **GitHub App installation tokens** (`/plugin/mint-installation-token.sh --profile product-ci`, from the `:go` plugin image) in place of the shared `github_token` PAT. Every config-service template still uses `github_token`. This plan follows the config-service convention so the new template matches its siblings; migrating the templates to App tokens is a separate change to `woodpecker-release`.

## File Structure

**Part A —** `/Users/barry/Git/woodpecker-release`

| File | Responsibility |
| --- | --- |
| `plugin/lib/github_release.sh` | Modify: add draft support to `github_release_create()` |
| `plugin/test/test_github_release.bats` | Modify: cover the draft flag |
| `plugin/entrypoint.sh` | Modify: pass `PLUGIN_DRAFT` through |
| `config-service/templates/release-dotnet-library/pipeline.yaml.template` | Create: the .NET pipeline |
| `README.md` | Modify: document the template and its data keys |

**Part B —** `/Users/barry/Git/SixtyFiveXX`

| File | Responsibility |
| --- | --- |
| `scripts/stamp-version.sh` | Create: derive and write the four version properties |
| `scripts/test-stamp-version.sh` | Create: its test |
| `Directory.Build.props` | Modify: version block, fix `RepositoryUrl`, drop `TargetFramework` |
| `src/SixtyFiveXX/SixtyFiveXX.csproj` | Modify: multi-target, package metadata |
| `cog.toml` | Create: house file + this repo's stamp hook |
| `CONTRIBUTING.md` | Create: commit convention and hook install |
| `docker/ci.Dockerfile` | Create: SDK 10 + .NET 8 runtime + 64tass |
| `.woodpecker/woodpecker-template.yaml` | Create: template reference |
| `.woodpecker.yml` | Delete: replaced by the config service |

---

## PART A — `woodpecker-release`

### Task 1: Draft support in the shared plugin

**Files:**
- Modify: `/Users/barry/Git/woodpecker-release/plugin/lib/github_release.sh`
- Modify: `/Users/barry/Git/woodpecker-release/plugin/entrypoint.sh`
- Test: `/Users/barry/Git/woodpecker-release/plugin/test/test_github_release.bats`

**Interfaces:**
- Consumes: `gh` CLI, `CI_REPO`, the existing `github_release_create()` contract.
- Produces: `github_release_create()` honouring `PLUGIN_DRAFT=true`; a new `github_release_publish()` that flips a draft public.

Publishing to nuget.org is irreversible. Creating the GitHub Release as a **draft**, publishing the package, then flipping the Release public means a failed publish leaves no public Release claiming a version that does not exist. NovaVM already does this by hand; this task moves it into the shared plugin so every template gets it.

- [ ] **Step 1: Write the failing test**

Append to `/Users/barry/Git/woodpecker-release/plugin/test/test_github_release.bats`:

```bash
@test "github_release_create passes --draft when PLUGIN_DRAFT is true" {
  export CI_REPO="barryw/testrepo"
  export PLUGIN_DRAFT="true"
  gh() { echo "$@" >> "$BATS_TEST_TMPDIR/gh-args"; }
  export -f gh

  source "$BATS_TEST_DIRNAME/../lib/github_release.sh"
  github_release_create "v1.2.3" ""

  run cat "$BATS_TEST_TMPDIR/gh-args"
  [[ "$output" == *"--draft"* ]]
}

@test "github_release_create omits --draft by default" {
  export CI_REPO="barryw/testrepo"
  unset PLUGIN_DRAFT
  gh() { echo "$@" >> "$BATS_TEST_TMPDIR/gh-args"; }
  export -f gh

  source "$BATS_TEST_DIRNAME/../lib/github_release.sh"
  github_release_create "v1.2.3" ""

  run cat "$BATS_TEST_TMPDIR/gh-args"
  [[ "$output" != *"--draft"* ]]
}

@test "github_release_publish flips a draft public" {
  export CI_REPO="barryw/testrepo"
  gh() { echo "$@" >> "$BATS_TEST_TMPDIR/gh-args"; }
  export -f gh

  source "$BATS_TEST_DIRNAME/../lib/github_release.sh"
  github_release_publish "v1.2.3"

  run cat "$BATS_TEST_TMPDIR/gh-args"
  [[ "$output" == *"release edit v1.2.3"* ]]
  [[ "$output" == *"--draft=false"* ]]
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/barry/Git/woodpecker-release && bats plugin/test/test_github_release.bats
```

Expected: FAIL — the two draft assertions fail because no `--draft` is ever emitted, and `github_release_publish` does not exist (`command not found`).

- [ ] **Step 3: Add draft support**

In `plugin/lib/github_release.sh`, inside `github_release_create()`, after the `prerelease_flag` block is appended to `args`, add:

```bash
  if [ "${PLUGIN_DRAFT:-false}" = "true" ]; then
    args+=(--draft)
    echo "Creating as draft; publish with github_release_publish after artifacts land."
  fi
```

Then append this function to the end of the file:

```bash
# Flip a draft Release public. Called after artifacts have been published, so a
# failed artifact step never leaves a public Release claiming a version that
# does not exist.
github_release_publish() {
  local version="$1"
  local repo="${CI_REPO:?CI_REPO not set}"

  if [ -z "$version" ]; then
    echo "ERROR: github_release_publish requires a version" >&2
    return 1
  fi

  echo "Publishing draft Release ${version}..."
  gh release edit "$version" --draft=false --repo "$repo"
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd /Users/barry/Git/woodpecker-release && bats plugin/test/test_github_release.bats
```

Expected: PASS — all tests, including the three new ones.

- [ ] **Step 5: Pass `PLUGIN_DRAFT` through the entrypoint**

`plugin/entrypoint.sh` already exports plugin settings as environment variables to the sourced libs, so `PLUGIN_DRAFT` needs no plumbing. **Verify this** rather than assuming:

```bash
cd /Users/barry/Git/woodpecker-release && grep -n "PLUGIN_DRAFT\|github_release_create" plugin/entrypoint.sh
```

If `github_release_create` is invoked in a subshell that does not inherit the environment, export `PLUGIN_DRAFT` explicitly next to the existing `export GH_TOKEN=...` line. If it already inherits, change nothing and say so in your report.

- [ ] **Step 6: Run the whole plugin test suite**

```bash
cd /Users/barry/Git/woodpecker-release && bats plugin/test/
```

Expected: PASS — all files. Draft support must not disturb the existing changelog, cog or git tests.

- [ ] **Step 7: Commit**

```bash
cd /Users/barry/Git/woodpecker-release
git add plugin/lib/github_release.sh plugin/test/test_github_release.bats plugin/entrypoint.sh
git commit -m "feat: add draft Release support to the release plugin

Publishing to an append-only registry after creating a public Release
leaves a permanent inconsistency when the publish fails. Creating the
Release as a draft and flipping it public once artifacts land makes the
failure recoverable. NovaVM already does this by hand; this moves it
into the shared plugin."
```

---

### Task 2: The `release-dotnet-library` template

**Files:**
- Create: `/Users/barry/Git/woodpecker-release/config-service/templates/release-dotnet-library/pipeline.yaml.template`

**Interfaces:**
- Consumes: the plugin's `release-tag` mode, `/woodpecker/version.txt`, `PLUGIN_DRAFT` from Task 1.
- Produces: a template named `release-dotnet-library` accepting the data keys below.

Modelled directly on `config-service/templates/release-docker/pipeline.yaml.template`. Read that file first — this template must match its conventions exactly.

Data keys:

| Key | Default | Purpose |
| --- | --- | --- |
| `sdk_image` | `mcr.microsoft.com/dotnet/sdk:10.0` | Build and test image |
| `setup_commands` | none | Extra tooling before build |
| `test_project` | `` (whole solution) | Unit test project path |
| `test_filter` | none | `--filter` expression |
| `conformance_project` | none | Optional second suite; step omitted when absent |
| `conformance_setup` | none | Commands before the conformance suite |
| `pack_project` | required | Project to pack |
| `nuget_publish` | `false` | Whether to push to nuget.org |

- [ ] **Step 1: Create the template**

```yaml
labels:
  platform: linux/amd64

when:
  - event: [push, pull_request, manual]

clone:
  git:
    image: woodpeckerci/plugin-git
    settings:
      tags: true
      depth: 0

steps:
  - name: validate-commits
    image: ghcr.io/cocogitto/cog:6.2.0
    commands:
      - git fetch --tags origin 2>/dev/null || true
      - cog check --from-latest-tag 2>/dev/null || cog check 2>/dev/null || echo "Commit validation skipped (no tags yet)"
    when:
      - event: push

  - name: build
    image: {{or .sdk_image "mcr.microsoft.com/dotnet/sdk:10.0"}}
    commands:{{range .setup_commands}}
      - {{.}}{{end}}
      - dotnet build -c Release

  - name: test
    image: {{or .sdk_image "mcr.microsoft.com/dotnet/sdk:10.0"}}
    commands:{{range .setup_commands}}
      - {{.}}{{end}}
      - dotnet test {{or .test_project ""}} -c Release{{if .test_filter}} --filter "{{.test_filter}}"{{end}}
    depends_on: [build]
{{if .conformance_project}}
  - name: conformance
    image: {{or .sdk_image "mcr.microsoft.com/dotnet/sdk:10.0"}}
    commands:{{range .setup_commands}}
      - {{.}}{{end}}{{range .conformance_setup}}
      - {{.}}{{end}}
      - dotnet test {{.conformance_project}} -c Release
    depends_on: [build]
{{end}}
  - name: release
    image: ghcr.io/barryw/woodpecker-release:latest
    pull: true
    environment:
      PLUGIN_GITHUB_TOKEN:
        from_secret: github_token
      PLUGIN_GIT_EMAIL: ci@barrywalker.io
      PLUGIN_MODE: release-tag
      PLUGIN_DRAFT: "{{if .nuget_publish}}true{{else}}false{{end}}"
    when:
      - event: push
        branch: main
    depends_on: [validate-commits, test{{if .conformance_project}}, conformance{{end}}]
{{if .nuget_publish}}
  - name: nuget-publish
    image: {{or .sdk_image "mcr.microsoft.com/dotnet/sdk:10.0"}}
    when:
      - event: push
        branch: main
    environment:
      NUGET_KEY:
        from_secret: nuget_api_key
    commands:
      - 'VERSION=$(cat /woodpecker/version.txt 2>/dev/null || echo "NONE")'
      - 'test "$VERSION" != "NONE" || { echo "No new version, skipping publish"; exit 0; }'
      - 'echo "Publishing $VERSION"'
      - 'git fetch --tags origin'
      - 'git checkout "$VERSION"'
      - 'dotnet pack {{.pack_project}} -c Release -o ./artifacts -p:ContinuousIntegrationBuild=true'
      - 'dotnet nuget push "./artifacts/*.nupkg" -k $NUGET_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate'
    depends_on: [release]

  - name: finalize-release
    image: ghcr.io/barryw/woodpecker-release:latest
    pull: true
    when:
      - event: push
        branch: main
    environment:
      GH_TOKEN:
        from_secret: github_token
    commands:
      - 'VERSION=$(cat /woodpecker/version.txt 2>/dev/null || echo "NONE")'
      - 'test "$VERSION" != "NONE" || { echo "No new version, nothing to finalize"; exit 0; }'
      - '. /plugin/lib/github_release.sh'
      - 'ASSETS=$(ls ./artifacts/*.nupkg ./artifacts/*.snupkg 2>/dev/null | tr "\n" " " || true)'
      - 'if [ -n "$ASSETS" ]; then github_release_upload "$VERSION" $ASSETS; fi'
      - 'github_release_publish "$VERSION"'
    depends_on: [nuget-publish]
{{end}}
```

Three details that are load-bearing:

`git checkout "$VERSION"` — the release step created a commit (the stamped `Directory.Build.props`) and a tag. The workspace is still on the pre-bump commit, so packing without checking out the tag would build the *previous* version's files. This is how the tag, the project files and the package are guaranteed to carry the same version.

**`finalize-release` runs on the plugin image, not `sdk_image`.** The `gh` CLI is **not** present in `mcr.microsoft.com/dotnet/sdk:10.0` — verified by running it — so calling `gh` from the publish step would fail with `command not found` for any repo using a stock SDK image. The plugin image carries `gh` 2.67.0 and exposes its helpers at `/plugin/lib/*.sh` (`plugin/Dockerfile:31`), so sourcing `github_release.sh` gives this step `github_release_upload` and `github_release_publish` — the same implementations the plugin itself uses, rather than a second hand-rolled copy of the same `gh` invocations.

`github_release_publish` is the **last** command in the pipeline. Everything that can fail has already succeeded, so the Release only becomes public once the package is genuinely on nuget.org.

**The asset list is built with `ls`, not passed as a bare glob.** A project that does not set `IncludeSymbols` produces no `.snupkg`, and an unmatched shell glob is passed through *literally* — `gh` would then fail on a nonexistent path, aborting the step before `github_release_publish` runs. The package would be on nuget.org while its Release stayed a draft forever. Guarding with `if [ -n "$ASSETS" ]` also covers the case where neither file exists, and keeps the step from failing under `set -e` when the test is false.

- [ ] **Step 2: Verify the template renders**

Go `text/template` fails loudly on a malformed template but silently on a missing key. Render it with a minimal and a maximal data set:

```bash
cd /Users/barry/Git/woodpecker-release/config-service
cat > /tmp/render_test.go <<'EOF'
package main

import ("os";"text/template")

func main() {
    t := template.Must(template.ParseFiles("templates/release-dotnet-library/pipeline.yaml.template"))
    minimal := map[string]any{"pack_project": "src/Foo"}
    maximal := map[string]any{
        "sdk_image": "ghcr.io/barryw/x:1",
        "setup_commands": []string{"apt-get update"},
        "test_project": "tests/Foo.Tests", "test_filter": "Category!=Slow",
        "conformance_project": "tests/Foo.Conformance",
        "conformance_setup": []string{"./build.sh"},
        "pack_project": "src/Foo", "nuget_publish": true,
    }
    os.Stdout.WriteString("--- MINIMAL ---\n"); t.Execute(os.Stdout, minimal)
    os.Stdout.WriteString("\n--- MAXIMAL ---\n"); t.Execute(os.Stdout, maximal)
}
EOF
go run /tmp/render_test.go
```

Expected: both render. **Check by eye:** the minimal output has no `conformance`, `nuget-publish` or `finalize-release` step, and its `release` step has `PLUGIN_DRAFT: "false"`; the maximal output has all three, `PLUGIN_DRAFT: "true"`, `depends_on: [validate-commits, test, conformance]` on `release`, and a `finalize-release` step running on `ghcr.io/barryw/woodpecker-release:latest` rather than on `sdk_image`.

- [ ] **Step 3: Verify both renderings are valid YAML**

```bash
go run /tmp/render_test.go | sed -n '/--- MINIMAL ---/,/--- MAXIMAL ---/p' | sed '1d;$d' | python3 -c "import sys,yaml; yaml.safe_load(sys.stdin); print('minimal: valid YAML')"
go run /tmp/render_test.go | sed -n '/--- MAXIMAL ---/,$p' | sed '1d' | python3 -c "import sys,yaml; yaml.safe_load(sys.stdin); print('maximal: valid YAML')"
```

Expected: both print `valid YAML`. A template that renders but produces invalid YAML fails at pipeline time with a far worse error message.

- [ ] **Step 4: Clean up and commit**

```bash
rm -f /tmp/render_test.go
cd /Users/barry/Git/woodpecker-release
git add config-service/templates/release-dotnet-library/
git commit -m "feat: add release-dotnet-library config-service template

Builds, tests, optionally runs a second conformance suite, cuts a draft
GitHub Release, then packs and publishes to nuget.org before flipping
the Release public.

The publish step checks out the tag the release step created, so the
package version, the tag and the stamped project files are the same
value by construction rather than by coincidence."
```

---

### Task 3: Document the template

**Files:**
- Modify: `/Users/barry/Git/woodpecker-release/README.md`

**Interfaces:**
- Consumes: the template from Task 2.
- Produces: onboarding documentation for every other .NET repo in the suite.

- [ ] **Step 1: Add the onboarding stanza**

In the "Onboarding a New Repo → Step 3" section, after the `release-docker` block, add:

````markdown
**For a .NET library** (build + test + optional conformance suite + release + NuGet publish):
```yaml
template: release-dotnet-library
data:
  sdk_image: "mcr.microsoft.com/dotnet/sdk:10.0"
  test_project: "tests/MyLib.Tests"
  test_filter: "Category!=Performance"
  pack_project: "src/MyLib"
  nuget_publish: true
  # optional:
  # setup_commands: ["apt-get update && apt-get install -y some-tool"]
  # conformance_project: "tests/MyLib.Conformance"
  # conformance_setup: ["tests/MyLib.Conformance/build.sh"]
```
> Requires the `nuget_api_key` secret when `nuget_publish: true`. The Release is
> created as a **draft** and flipped public only after the package is on
> nuget.org — nuget.org versions can be unlisted but never deleted, so a public
> Release must never claim a version that failed to publish.
>
> The repo's `cog.toml` needs `pre_bump_hooks` that stamp the version into its
> project files; see the .NET example in `onboarding/cog.toml`.
````

- [ ] **Step 2: Add the template to the Available Templates table**

In the "Available Templates" table, after the `release-docker` row:

```markdown
| `release-dotnet-library` | validate-commits → build → test → [conformance] → release (draft) → nuget-publish → finalize-release | .NET libraries published to NuGet |
```

- [ ] **Step 3: Add `nuget_api_key` to the secrets table**

In the "Step 5: Add Woodpecker secrets" table, after the `github_token` row:

```markdown
| `nuget_api_key` | Only if `nuget_publish: true` | Publishing packages to nuget.org |
```

- [ ] **Step 4: Verify no other doc contradicts the new flow**

```bash
cd /Users/barry/Git/woodpecker-release && grep -n "dotnet\|nuget\|NuGet" README.md
```

Expected: only the lines you just added. If the README asserts elsewhere that no .NET template exists, fix it in this commit — stale documentation is a defect.

- [ ] **Step 5: Commit**

```bash
cd /Users/barry/Git/woodpecker-release
git add README.md
git commit -m "docs: document the release-dotnet-library template"
```

---

## PART B — `SixtyFiveXX`

### Task 4: The version stamping script

**Files:**
- Create: `/Users/barry/Git/SixtyFiveXX/scripts/stamp-version.sh`
- Test: `/Users/barry/Git/SixtyFiveXX/scripts/test-stamp-version.sh`

**Interfaces:**
- Consumes: a semver string as `$1`, `Directory.Build.props` as `$2`.
- Produces: `scripts/stamp-version.sh <version> <props-file>`, invoked from `cog.toml`'s `pre_bump_hooks`.

**This is the only genuinely new logic in the plan and the most likely thing to be wrong.** NovaVM's flat `sed` cannot be reused, because `AssemblyVersion` is not `{{version}}.0` here — it is pinned to `major.minor` while the package is `0.x`, and to `major` from 1.0 onward, so assembly identity changes exactly when binary compatibility can break.

| Input | `Version` | `AssemblyVersion` | `FileVersion` |
| --- | --- | --- | --- |
| `0.3.1` | `0.3.1` | `0.3.0.0` | `0.3.1.0` |
| `0.10.2` | `0.10.2` | `0.10.0.0` | `0.10.2.0` |
| `1.4.2` | `1.4.2` | `1.0.0.0` | `1.4.2.0` |
| `10.20.30` | `10.20.30` | `10.0.0.0` | `10.20.30.0` |

Naive string slicing breaks on two-digit components — `0.10.2` must give `0.10.0.0`, not `0.1.0.0`.

- [ ] **Step 1: Write the failing test**

Create `/Users/barry/Git/SixtyFiveXX/scripts/test-stamp-version.sh`:

```bash
#!/usr/bin/env bash
# Tests for stamp-version.sh. Run: scripts/test-stamp-version.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
failures=0

check() {
    local version="$1" want_assembly="$2" want_file="$3"
    local tmp; tmp="$(mktemp)"
    cat > "$tmp" <<'EOF'
<Project>
  <PropertyGroup>
    <Version>0.0.0</Version>
    <AssemblyVersion>0.0.0.0</AssemblyVersion>
    <FileVersion>0.0.0.0</FileVersion>
  </PropertyGroup>
</Project>
EOF
    "$here/stamp-version.sh" "$version" "$tmp"

    local got_version got_assembly got_file
    got_version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$tmp")
    got_assembly=$(sed -n 's|.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*|\1|p' "$tmp")
    got_file=$(sed -n 's|.*<FileVersion>\(.*\)</FileVersion>.*|\1|p' "$tmp")

    if [ "$got_version" = "$version" ] && [ "$got_assembly" = "$want_assembly" ] && [ "$got_file" = "$want_file" ]; then
        echo "ok   $version -> Version=$got_version Assembly=$got_assembly File=$got_file"
    else
        echo "FAIL $version"
        echo "     want Version=$version Assembly=$want_assembly File=$want_file"
        echo "     got  Version=$got_version Assembly=$got_assembly File=$got_file"
        failures=$((failures + 1))
    fi
    rm -f "$tmp"
}

check 0.3.1     0.3.0.0    0.3.1.0
check 0.10.2    0.10.0.0   0.10.2.0
check 0.1.0     0.1.0.0    0.1.0.0
check 1.4.2     1.0.0.0    1.4.2.0
check 1.0.0     1.0.0.0    1.0.0.0
check 10.20.30  10.0.0.0   10.20.30.0
check 2.0.1     2.0.0.0    2.0.1.0

# A malformed version must fail loudly rather than writing nonsense.
tmp="$(mktemp)"; echo "<Project></Project>" > "$tmp"
if "$here/stamp-version.sh" "not-a-version" "$tmp" 2>/dev/null; then
    echo "FAIL rejects malformed version"; failures=$((failures + 1))
else
    echo "ok   rejects malformed version"
fi
rm -f "$tmp"

# A missing props file must fail loudly.
if "$here/stamp-version.sh" "1.0.0" "/nonexistent/path" 2>/dev/null; then
    echo "FAIL rejects missing props file"; failures=$((failures + 1))
else
    echo "ok   rejects missing props file"
fi

if [ "$failures" -eq 0 ]; then
    echo "All stamp-version tests passed."
else
    echo "$failures test(s) failed."; exit 1
fi
```

```bash
chmod +x /Users/barry/Git/SixtyFiveXX/scripts/test-stamp-version.sh
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/barry/Git/SixtyFiveXX && scripts/test-stamp-version.sh
```

Expected: FAIL — `scripts/stamp-version.sh: No such file or directory` for every case.

- [ ] **Step 3: Write the script**

Create `/Users/barry/Git/SixtyFiveXX/scripts/stamp-version.sh`:

```bash
#!/usr/bin/env bash
# Stamp a semver into Directory.Build.props.
#
# AssemblyVersion is deliberately NOT the full version. It is pinned to the
# range within which binary compatibility holds, so consumers do not need a
# binding redirect for a release that is compatible:
#
#   0.x  -> major.minor.0.0   (SemVer says a 0.x minor bump may break)
#   >=1  -> major.0.0.0       (Microsoft's library guidance)
#
# Usage: stamp-version.sh <version> <props-file>
set -euo pipefail

version="${1:?usage: stamp-version.sh <version> <props-file>}"
props="${2:?usage: stamp-version.sh <version> <props-file>}"

if [[ ! "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
    echo "stamp-version: '$version' is not a bare semver (expected N.N.N)." >&2
    exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"

[ -f "$props" ] || { echo "stamp-version: no such file: $props" >&2; exit 1; }

if [ "$major" -eq 0 ]; then
    assembly="0.${minor}.0.0"
else
    assembly="${major}.0.0.0"
fi

sed -i.bak -E \
    -e "s|<Version>[^<]*</Version>|<Version>${version}</Version>|" \
    -e "s|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>${assembly}</AssemblyVersion>|" \
    -e "s|<FileVersion>[^<]*</FileVersion>|<FileVersion>${version}.0</FileVersion>|" \
    "$props"
rm -f "${props}.bak"

echo "stamp-version: ${version} (AssemblyVersion ${assembly}) -> ${props}"
```

```bash
chmod +x /Users/barry/Git/SixtyFiveXX/scripts/stamp-version.sh
```

Note `InformationalVersion` is **not** stamped — the SDK derives it, and SourceLink appends the commit SHA. Hand-setting it would discard that.

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd /Users/barry/Git/SixtyFiveXX && scripts/test-stamp-version.sh
```

Expected: PASS — 9 `ok` lines and `All stamp-version tests passed.`

- [ ] **Step 5: Commit**

```bash
cd /Users/barry/Git/SixtyFiveXX
git add scripts/stamp-version.sh scripts/test-stamp-version.sh
git commit -m "build: add version stamping for Directory.Build.props

AssemblyVersion is pinned to the range within which binary
compatibility holds - major.minor while 0.x, major from 1.0 - so
consumers do not need a binding redirect for a compatible release.
Tested against two-digit components, which naive slicing gets wrong."
```

---

### Task 5: Multi-target and package metadata

**Files:**
- Modify: `/Users/barry/Git/SixtyFiveXX/Directory.Build.props`
- Modify: `/Users/barry/Git/SixtyFiveXX/src/SixtyFiveXX/SixtyFiveXX.csproj`

**Interfaces:**
- Consumes: `scripts/stamp-version.sh` from Task 4 (it rewrites the properties added here).
- Produces: a packable, multi-targeted library.

`Directory.Build.props` currently sets a singular `<TargetFramework>net10.0</TargetFramework>` for **every** project. That collides with the library's plural `<TargetFrameworks>` — MSBuild treats a project with both as single-targeted. All three non-library projects set their own already, so the property is deleted, not conditioned.

- [ ] **Step 1: Verify the non-library projects set their own TFM**

```bash
cd /Users/barry/Git/SixtyFiveXX && grep -l "<TargetFramework>" tests/*/*.csproj bench/*/*.csproj
```

Expected: all three paths listed. **If any is missing, add `<TargetFramework>net10.0</TargetFramework>` to it before continuing** — otherwise deleting the property from the props file leaves that project with no TFM and the build fails confusingly.

- [ ] **Step 2: Rewrite `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Version>0.0.0</Version>
    <AssemblyVersion>0.0.0.0</AssemblyVersion>
    <FileVersion>0.0.0.0</FileVersion>
  </PropertyGroup>

  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Authors>Barry Walker</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/barryw/SixtyFiveXX</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>
</Project>
```

Three changes: the version block is new and is what `stamp-version.sh` rewrites; `RepositoryUrl` is corrected from `barrywalker` to `barryw`; `<TargetFramework>` is gone.

`0.0.0` is a deliberate placeholder — the first release stamps a real value. A local `dotnet pack` before then produces `0.0.0`, which is expected and harmless because only CI publishes.

- [ ] **Step 3: Rewrite `src/SixtyFiveXX/SixtyFiveXX.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <PropertyGroup>
    <PackageId>SixtyFiveXX</PackageId>
    <Description>A cycle-accurate NMOS 6502 emulator core. All 256 opcodes, including the undocumented ones, certified against 2,560,000 SingleStepTests vectors and Klaus Dormann's functional test. Interrupts, RDY and SO with hardware-correct timing.</Description>
    <PackageTags>6502;emulator;nmos;cpu;retro;c64;nes;cycle-accurate</PackageTags>
    <PackageProjectUrl>https://github.com/barryw/SixtyFiveXX</PackageProjectUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="SixtyFiveXX.Tests" />
    <InternalsVisibleTo Include="SixtyFiveXX.Conformance" />
    <InternalsVisibleTo Include="SixtyFiveXX.Benchmarks" />
  </ItemGroup>

</Project>
```

No `PackageReference` — SourceLink ships in the SDK from .NET 8, so the zero-dependency rule holds.

- [ ] **Step 4: Verify the build and the full suite on both TFMs**

```bash
cd /Users/barry/Git/SixtyFiveXX
dotnet build -c Release -v q
dotnet test -c Release --filter "Category!=Performance" -v q
```

Expected: build reports 0 warnings and 0 errors; tests all pass. The test projects now exercise the library on both `net8.0` and `net10.0` — confirm the log shows both.

If `net8.0` tests fail to *run* with a missing-runtime error, that is expected on a machine without the .NET 8 runtime and is what Task 7's image solves. Confirm with `dotnet --list-runtimes | grep 8\\.` and say so in your report rather than working around it.

- [ ] **Step 5: Verify the package contents**

```bash
cd /Users/barry/Git/SixtyFiveXX
dotnet pack src/SixtyFiveXX -c Release -o /tmp/packtest -p:Version=0.3.1 -p:AssemblyVersion=0.3.0.0 -p:FileVersion=0.3.1.0
ls /tmp/packtest
cd /tmp/packtest && unzip -o -q SixtyFiveXX.0.3.1.nupkg -d unpacked && ls unpacked/lib && cat unpacked/SixtyFiveXX.nuspec
```

Expected: a `.nupkg` **and** a `.snupkg`; `unpacked/lib` contains both `net8.0` and `net10.0`; the nuspec shows the description, `https://github.com/barryw/SixtyFiveXX` (not `barrywalker`), the MIT licence, and a `readme` element.

```bash
rm -rf /tmp/packtest
```

- [ ] **Step 6: Commit**

```bash
cd /Users/barry/Git/SixtyFiveXX
git add Directory.Build.props src/SixtyFiveXX/SixtyFiveXX.csproj
git commit -m "build: multi-target net8.0 and net10.0 and add package metadata

net8.0 is the widely deployed LTS; publishing net10.0 only would put the
package out of reach of most consumers. The core compiles clean on both
with no conditional compilation.

Also corrects RepositoryUrl, which pointed at a barrywalker account that
does not exist, and removes the solution-wide TargetFramework that would
collide with the library's TargetFrameworks."
```

---

### Task 6: `cog.toml` and contributor documentation

**Files:**
- Create: `/Users/barry/Git/SixtyFiveXX/cog.toml`
- Create: `/Users/barry/Git/SixtyFiveXX/CONTRIBUTING.md`

**Interfaces:**
- Consumes: `scripts/stamp-version.sh` from Task 4.
- Produces: the version-bump contract the release plugin executes.

Copy `/Users/barry/Git/woodpecker-release/onboarding/cog.toml` verbatim, then make exactly two changes: set `repository`, and add this repo's `pre_bump_hooks`. **Read the canonical file first** — do not retype it from this plan, so any upstream change is picked up.

- [ ] **Step 1: Copy the canonical file and apply the two changes**

```bash
cd /Users/barry/Git/SixtyFiveXX
cp /Users/barry/Git/woodpecker-release/onboarding/cog.toml ./cog.toml
```

Then edit `cog.toml`:

Set the repository:

```toml
repository = "SixtyFiveXX"
```

Replace the empty `pre_bump_hooks` with:

```toml
# Stamp the version into Directory.Build.props so the repository states its own
# version and the tag, the project files and the published package cannot
# disagree. AssemblyVersion is pinned to the binary-compatibility range rather
# than the full version - see scripts/stamp-version.sh.
pre_bump_hooks = [
    "scripts/stamp-version.sh {{version}} Directory.Build.props",
    "git add Directory.Build.props",
]
```

Leave everything else exactly as copied: `from_latest_tag`, `ignore_merge_commits`, `branch_whitelist`, `tag_prefix`, `skip_ci`, `skip_untracked`, the whole `[commit_types]` table, and the `[git_hooks.commit-msg]` block.

- [ ] **Step 2: Verify cog accepts the config and the history**

```bash
cd /Users/barry/Git/SixtyFiveXX && cog check
```

Expected: PASS. The three historical merge commits are tolerated by `ignore_merge_commits = true`. If `cog check` reports a non-merge commit as invalid, **stop and report it** — do not rewrite history.

- [ ] **Step 3: Verify the bump hook end-to-end on a scratch clone**

Never test a bump against the real repository — it creates tags.

```bash
cd /tmp && rm -rf cogtest && git clone /Users/barry/Git/SixtyFiveXX cogtest && cd cogtest
git commit --allow-empty -m "feat: scratch commit to force a minor bump"
cog bump --auto
grep -E "<Version>|<AssemblyVersion>|<FileVersion>" Directory.Build.props
git tag
git show --stat HEAD | head -20
```

Expected: a `v0.1.0` tag; `Version` `0.1.0`, `AssemblyVersion` `0.1.0.0`, `FileVersion` `0.1.0.0`; the bump commit contains `Directory.Build.props` and `CHANGELOG.md`; and its message carries `[skip ci]`.

Then confirm the stamped tree still builds:

```bash
cd /tmp/cogtest && dotnet build -c Release -v q && cd /tmp && rm -rf cogtest
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Write `CONTRIBUTING.md`**

```markdown
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
| `docs:` `test:` `refactor:` `perf:` `build:` `ci:` `style:` `chore:` | no bump |

Everything except `chore` and `style` still appears in the release notes.

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

    dotnet build -c Release
    dotnet test -c Release --filter "Category!=Performance"

The conformance suite runs 2,560,000 SingleStepTests vectors plus Klaus
Dormann's functional and interrupt tests. It needs `64tass` to assemble the
interrupt test:

    brew install 64tass          # or: apt-get install 64tass
    tests/SixtyFiveXX.Conformance/klaus/build.sh
    dotnet test tests/SixtyFiveXX.Conformance -c Release

`tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` is a byte-faithful
port of Klaus Dormann's GPL-3.0 test, verified character-for-character against
upstream. Only assembler directives differ. **Do not edit it.**
```

- [ ] **Step 5: Commit**

```bash
cd /Users/barry/Git/SixtyFiveXX
git add cog.toml CONTRIBUTING.md
git commit -m "build: adopt the house cog configuration

Copied verbatim from woodpecker-release/onboarding/cog.toml with the
repository name set and a pre_bump_hook that stamps the version into
Directory.Build.props."
```

---

### Task 7: The CI image

**Files:**
- Create: `/Users/barry/Git/SixtyFiveXX/docker/ci.Dockerfile`

**Interfaces:**
- Produces: `ghcr.io/barryw/sixtyfivexx-ci:1`, passed to the template as `sdk_image`.

The stock SDK 10 image cannot serve this repo: the conformance suite needs `64tass` to assemble Klaus's interrupt test, and running the `net8.0` tests needs a .NET 8 runtime the SDK 10 image does not carry. Without the runtime, `dotnet test` silently covers only `net10.0` while the package advertises both.

- [ ] **Step 1: Write the Dockerfile**

Create `/Users/barry/Git/SixtyFiveXX/docker/ci.Dockerfile`:

```dockerfile
# CI image for SixtyFiveXX.
#
# Two things the stock SDK image lacks:
#   64tass         - assembles Klaus Dormann's interrupt test, which upstream
#                    ships as source only, so the conformance suite builds it
#                    on demand.
#   .NET 8 runtime - the SDK 10 image can BUILD net8.0 but not RUN it, so the
#                    net8.0 half of the test matrix would not execute.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends 64tass git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

# Fail at image build time rather than confusingly mid-pipeline.
RUN 64tass --version \
 && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 8\.' \
 && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'
```

- [ ] **Step 2: Build the image and verify it**

```bash
cd /Users/barry/Git/SixtyFiveXX
docker build -f docker/ci.Dockerfile -t ghcr.io/barryw/sixtyfivexx-ci:1 .
```

Expected: build succeeds. The final `RUN` is the verification — if either runtime or 64tass is missing, the build fails there with a clear message.

- [ ] **Step 3: Verify the conformance suite runs inside the image**

This is the real test of the image, and the reason it exists:

```bash
cd /Users/barry/Git/SixtyFiveXX
docker run --rm -v "$PWD":/src -w /src ghcr.io/barryw/sixtyfivexx-ci:1 \
  bash -c "tests/SixtyFiveXX.Conformance/klaus/build.sh && dotnet test tests/SixtyFiveXX.Conformance -c Release -v q"
```

Expected: the Klaus binary builds (65,536 bytes) and the conformance suite passes. This takes several minutes.

- [ ] **Step 4: Verify the net8.0 tests actually execute**

```bash
cd /Users/barry/Git/SixtyFiveXX
docker run --rm -v "$PWD":/src -w /src ghcr.io/barryw/sixtyfivexx-ci:1 \
  bash -c "dotnet test tests/SixtyFiveXX.Tests -c Release --filter 'Category!=Performance' -v n 2>&1 | grep -iE 'net8|net10'"
```

Expected: output mentions **both** `net8.0` and `net10.0`. If only `net10.0` appears, the runtime install did not take effect and the package would advertise a target nothing tests — stop and report it.

- [ ] **Step 5: Commit**

```bash
cd /Users/barry/Git/SixtyFiveXX
git add docker/ci.Dockerfile
git commit -m "ci: add the SixtyFiveXX CI image

Carries 64tass for Klaus's interrupt test and the .NET 8 runtime, which
the SDK 10 image lacks - without it the net8.0 half of the test matrix
silently does not run while the package still advertises that target."
```

**Publishing the image to GHCR and making the package public is a manual bootstrap step** — see the Bootstrap section. Do not attempt it from this task.

---

### Task 8: Onboard to the config service

**Files:**
- Create: `/Users/barry/Git/SixtyFiveXX/.woodpecker/woodpecker-template.yaml`
- Delete: `/Users/barry/Git/SixtyFiveXX/.woodpecker.yml`

**Interfaces:**
- Consumes: the `release-dotnet-library` template from Task 2, `ghcr.io/barryw/sixtyfivexx-ci:1` from Task 7.
- Produces: the repository's complete CI configuration.

- [ ] **Step 1: Write the template reference**

Create `/Users/barry/Git/SixtyFiveXX/.woodpecker/woodpecker-template.yaml`:

```yaml
# SixtyFiveXX - .NET library published to NuGet.
# The config service (woodpecker-release) expands this into the full pipeline.
#
# sdk_image carries 64tass (to assemble Klaus Dormann's interrupt test, which
# upstream ships as source only) and the .NET 8 runtime (the SDK 10 image can
# build net8.0 but not run it). Built from docker/ci.Dockerfile.
#
# The performance gate is excluded from the unit run: it asserts a 50 MHz
# throughput floor and is sensitive to runner contention, so as a blocking step
# it would go red for reasons unrelated to the code.
template: release-dotnet-library
data:
  sdk_image: "ghcr.io/barryw/sixtyfivexx-ci:1"
  test_project: "tests/SixtyFiveXX.Tests"
  test_filter: "Category!=Performance"
  conformance_project: "tests/SixtyFiveXX.Conformance"
  conformance_setup:
    - "tests/SixtyFiveXX.Conformance/klaus/build.sh"
  pack_project: "src/SixtyFiveXX"
  nuget_publish: true
```

- [ ] **Step 2: Remove the old pipeline**

```bash
cd /Users/barry/Git/SixtyFiveXX && git rm .woodpecker.yml
```

The config service generates the pipeline; leaving the old file would run a second, conflicting one.

- [ ] **Step 3: Verify the rendered pipeline**

Render the real data block through the real template before anything runs in CI:

```bash
cd /Users/barry/Git/woodpecker-release/config-service
cat > /tmp/render_sixtyfive.go <<'EOF'
package main

import ("os";"text/template")

func main() {
    t := template.Must(template.ParseFiles("templates/release-dotnet-library/pipeline.yaml.template"))
    t.Execute(os.Stdout, map[string]any{
        "sdk_image": "ghcr.io/barryw/sixtyfivexx-ci:1",
        "test_project": "tests/SixtyFiveXX.Tests",
        "test_filter": "Category!=Performance",
        "conformance_project": "tests/SixtyFiveXX.Conformance",
        "conformance_setup": []string{"tests/SixtyFiveXX.Conformance/klaus/build.sh"},
        "pack_project": "src/SixtyFiveXX",
        "nuget_publish": true,
    })
}
EOF
go run /tmp/render_sixtyfive.go | tee /tmp/rendered.yaml
python3 -c "import yaml; yaml.safe_load(open('/tmp/rendered.yaml')); print('valid YAML')"
```

Then read `/tmp/rendered.yaml` and confirm, by eye:

- `release`, `nuget-publish` and `finalize-release` all carry `when: event: push, branch: main`.
- `nuget-publish` has `depends_on: [release]`; `finalize-release` has `depends_on: [nuget-publish]`.
- `release` has `PLUGIN_DRAFT: "true"`.
- `finalize-release` runs on `ghcr.io/barryw/woodpecker-release:latest`, **not** on `sdk_image` — the SDK image has no `gh` CLI.
- The last command in `finalize-release` is `github_release_publish "$VERSION"`.
- The conformance step runs `klaus/build.sh` before `dotnet test`.

```bash
rm -f /tmp/render_sixtyfive.go /tmp/rendered.yaml
```

- [ ] **Step 4: Verify nothing else references the old pipeline**

```bash
cd /Users/barry/Git/SixtyFiveXX && grep -rn "woodpecker.yml" --include="*.md" . | grep -v "^./docs/superpowers/"
```

Expected: no results outside the planning documents. If `README.md` describes the old CI setup, update it in this commit.

- [ ] **Step 5: Commit**

```bash
cd /Users/barry/Git/SixtyFiveXX
git add .woodpecker/woodpecker-template.yaml
git commit -m "ci: onboard to the woodpecker-release config service

Replaces the hand-written pipeline with a template reference, matching
every other repo in the suite."
```

---

## Bootstrap — manual, in this order

These cannot be done from the plan; they need UI access or credentials.

1. **Land Part A** in `woodpecker-release` and confirm the config service is serving the new template.
2. **Publish the CI image:**
   ```bash
   cd /Users/barry/Git/SixtyFiveXX
   echo "$GHCR_TOKEN" | docker login ghcr.io -u barryw --password-stdin
   docker build -f docker/ci.Dockerfile -t ghcr.io/barryw/sixtyfivexx-ci:1 .
   docker push ghcr.io/barryw/sixtyfivexx-ci:1
   ```
   Then **set the GHCR package public** in GitHub's package settings. Until then Woodpecker cannot pull it anonymously and every pipeline fails at image pull — a failure that reads like a Woodpecker misconfiguration rather than a permissions one.
3. **Enable `barryw/SixtyFiveXX`** in the Woodpecker UI.
4. **Add secrets:** `github_token` (PAT, contents read/write on this repo), `nuget_api_key` (nuget.org key scoped to package glob `SixtyFiveXX` — note the 365-day cap; an expired key fails only at release time).
5. **Install the commit hook** on each development machine: `cog install-hook --all`.
6. **Land Part B.** The first merge to `main` carrying a `feat:` or `fix:` releases `v0.1.0` — cog's clean-start value for a repo with no tags.
7. **Watch the first release end to end** and confirm: the tag exists; `Directory.Build.props` on `main` shows the released version; the GitHub Release is public with grouped notes; nuget.org lists the package with both TFMs; and the `.nupkg`/`.snupkg` are attached to the Release.

## Self-review notes

Checked against `docs/superpowers/specs/2026-08-01-ci-and-nuget-design.md`:

- **Part A** delivers the `release-dotnet-library` template (Task 2), documented (Task 3), plus the draft support the spec's ordering mitigation requires (Task 1) — the spec assumed the plugin already had drafts; it does not, so Task 1 was added.
- **Part B** delivers all seven onboarding items: stamping script (4), `Directory.Build.props` and csproj (5), `cog.toml` and `CONTRIBUTING.md` (6), CI image (7), template reference and old-pipeline deletion (8).
- **Assembly versioning** is Task 4, with the spec's exact test matrix plus `0.1.0`, `1.0.0` and `2.0.1`.
- **Ordering** is enforced twice: `depends_on: [release]` in the template, and the draft flip as the final command.
- **The no-op path** uses `/woodpecker/version.txt`, verified in Task 2's rendering check and exercised by the Bootstrap step 7 watch.
- **Global constraints** — no new `PackageReference`; the Klaus `.asm` is untouched and `CONTRIBUTING.md` says so; house secret names used throughout.

Two things this plan deliberately does not settle, because only a live run can:
whether Woodpecker's `[skip ci]` handling stops the bump commit from re-triggering the
pipeline on this instance (Bootstrap step 7 is where that shows up), and whether
`git checkout "$VERSION"` in the publish step behaves as expected against a tag the
same pipeline just pushed.
