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
