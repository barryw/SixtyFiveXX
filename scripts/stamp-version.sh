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
