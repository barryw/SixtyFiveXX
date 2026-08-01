#!/usr/bin/env bash
# Assembles Klaus Dormann's interrupt test, ported from AS65 to 64tass.
# The binary is not committed; run this to produce it.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v 64tass >/dev/null 2>&1; then
    echo "64tass not found. Install it (brew install 64tass / apt-get install 64tass)." >&2
    exit 1
fi

rm -f "$here/6502_interrupt_test.bin" "$here/6502_interrupt_test.lst"

64tass --nostart --long-branch \
       -L "$here/6502_interrupt_test.lst" \
       -o "$here/6502_interrupt_test.bin" \
       "$here/6502_interrupt_test.asm"

size=$(wc -c < "$here/6502_interrupt_test.bin" | tr -d ' ')
if [ "$size" != "65536" ]; then
    echo "Expected a 65536-byte image, got $size." >&2
    exit 1
fi

echo "Built $here/6502_interrupt_test.bin ($size bytes)"
