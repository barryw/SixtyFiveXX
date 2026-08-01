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

# sed never reports a no-op substitution as an error, so a missing element or
# one that carries an attribute (e.g. a Condition=) would otherwise pass
# silently. Verify each element actually holds the value we just tried to
# write. The check tolerates attributes on the opening tag -- unlike the
# substitution above, which requires a bare <Tag> -- so an element sed
# skipped is read correctly and reported as "wrong value" (or "missing"),
# rather than being re-checked with the same blind pattern that missed it.
verify_stamped() {
    local tag="$1" want="$2"
    local got
    got="$(sed -nE "s|.*<${tag}[^>]*>([^<]*)</${tag}>.*|\1|p" "$props" | head -n1)"
    if [ "$got" != "$want" ]; then
        if ! grep -q "<${tag}" "$props"; then
            echo "stamp-version: verify failed: <${tag}> not found in ${props}" >&2
        else
            echo "stamp-version: verify failed: <${tag}> is '${got}', expected '${want}', in ${props}" >&2
        fi
        exit 1
    fi
}

verify_stamped "Version" "$version"
verify_stamped "AssemblyVersion" "$assembly"
verify_stamped "FileVersion" "${version}.0"

echo "stamp-version: ${version} (AssemblyVersion ${assembly}) -> ${props}"
