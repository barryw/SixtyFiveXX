# Klaus Dormann's 6502 interrupt test

`6502_interrupt_test.asm` is Klaus Dormann's `6502_interrupt_test.a65`, ported from
AS65 directives to 64tass. **Only directives were changed** — every 6502 instruction,
label and comment is as published.

Source: https://github.com/Klaus2m5/6502_65C02_functional_tests (GPL-3.0)

The upstream project distributes prebuilt binaries only for the functional test and the
65C02 extended-opcode test; the interrupt test is source-only, and AS65 is a Windows
binary, which is why this port exists.

## Licence

This is a GPL-3.0 **test program**. SixtyFiveXX assembles and executes it; no SixtyFiveXX
source is derived from it and nothing links against it, so its licence does not reach
this project's MIT-licensed code. Klaus's copyright header is retained in the ported file.

## Configuration this port relies on

| Setting | Value | Meaning |
| --- | --- | --- |
| `I_port` | `$BFFC` | Feedback register the test writes to drive its own interrupt pins |
| `IRQ_bit` | 0 | Bit 0 of that register drives IRQ |
| `NMI_bit` | 1 | Bit 1 drives NMI |
| `I_drive` | 1 | Open collector |
| `I_filter` | `$7F` | Bit 7 set means "diagnostic stop" |
| `D_clear` | 0 | NMOS: the decimal flag is not cleared on interrupt entry |
| entry | `$0400` | |
| success trap | `$06F5` | `jmp *`, the nominal "every test passed" trap — see below |

`FeedbackBus` in the parent directory implements that register.

Other useful addresses, all read from the 64tass listing: NMI vector `$0739`,
RESET vector `$0778`, IRQ/BRK vector `$077D`. The zero page block is `$0A`–`$0F`
and the data segment is `$0200`–`$0203` (`$0203` is `I_src`, the expected-interrupt
mask). Any `jmp *` or `beq *`/`bne *` reachable from the `$0400` entry, other than
`$06F5`, is a failure trap — with one documented exception, below.

Two further `jmp *` "test passed" traps exist at `$070F` and `$072C`. They belong to
the manual 65C02 `WAI`/`STP` sections, which are documented as requiring the PC to be
set by hand and sit after a `jmp start`, making them unreachable from `$0400` — `$06F5`
is the only success trap a normal run can reach.

## The NMOS trap at `$075C`

A faithful NMOS core run from `$0400` does not reach `$06F5`. It runs 2,720 of the
program's roughly 2,915 cycles — about 93% — and stops at `$075C`, in the last
sub-test, `;test overlapping NMI, IRQ & BRK` (asm 809–831, assembled `$06C5`–`$06F5`).

That sub-test's `#I_set 8` macro asserts IRQ and NMI in the same `STA $BFFC` write
that immediately precedes a `BRK`. On real NMOS silicon the interrupt poll for that
`STA`→`BRK` boundary samples the pin one cycle before the write that asserts it, so
`BRK` always starts normally: the status byte is pushed with `B` set, and only then
does the pending NMI hijack the vector to the NMI handler — the documented NMOS
BRK/NMI hijack. `$075C` is the handler's check that `B` was *not* set, and it fails
by design at this timing. Klaus's own source says so, on the trapping line itself:

    #trap_ne         ;unexpected B-flag! - this may fail on a real 6502
                      ;due to a hardware bug on concurrent BRK & NMI

and again eleven lines later, at the very next check the same hijack event trips
once `$075C`'s is out of the way (asm 830), which a faithful core therefore never
reaches either:

    ;may fail due to a bug on a real NMOS 6502 - NMI could mask BRK
        #trap_ne      ;lost an interrupt

`KlausInterruptTests.cs` asserts the `$075C` trap together with its exact cycle
count (2,721), so the whole 2,720-cycle prefix — every sub-test before this one —
remains an exact regression gate. See
`docs/superpowers/investigations/investigation-075c.md` for the cycle-level trace and
timing sweep behind this, and
`docs/superpowers/investigations/experiment-past-075c.md` for confirmation that
neutralising just the `$075C` check only buys 192 more cycles before the asm-830 check
above traps in turn.

## Port notes

The AS65 directives `noopt`, `data`, `bss`, `code` and `end start` have no 64tass
equivalent and are commented out rather than deleted. Five lines were added before the
zero page block: two comment lines explaining why, the `$0000` anchor itself, and a
blank separator line:

    ; 64tass emits the binary from the lowest address used. Anchor the image at
    ; $0000 so the output is a full 64 KB memory picture (gaps are zero filled).
        * = $0000
        .byte 0

64tass writes the binary from the lowest address used, which would otherwise be
`zero_page` (`$0A`) and produce a 65,526-byte file. With the anchor, and with the
vectors reaching `$FFFF`, the output is a full 64 KB memory image; 64tass zero-fills
the gaps.

## Building

    ./build.sh

Requires `64tass`. The `.bin` and `.lst` outputs are gitignored.
