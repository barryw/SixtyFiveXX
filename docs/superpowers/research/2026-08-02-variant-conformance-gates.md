# Research: do the named 65C02/6510 certification gates actually exist?

**Date:** 2026-08-02
**Status:** Research only — nothing implemented, nothing committed.
**Scope:** Design doc `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md` §7/§10 names two gates for
phases 4–5:

- 65C02 (three sub-variants): *"Harte 65c02 ×3; Klaus 65C02 extended"*
- 6510: *"Wolfgang Lorenz suite"*

This checks whether those gates exist, are downloadable, and are runnable against a bare CPU core,
**before** a phase plan is written against them — Phase 2b already found one gate (Klaus's interrupt
test) was source-only and needed a port; the goal here is to catch anything like that in advance.

Confidence key: **confirmed** = I fetched the actual file/API response and read it myself. **inferred** =
built from confirmed facts but not directly read. Anything weaker is called out explicitly.

---

## 1. SingleStepTests / Harte — what 65xx variants exist?

**Existing consumption** (read from the project itself): `HarteCache.cs` fetches
`https://raw.githubusercontent.com/SingleStepTests/65x02/main/{set}/v1/{opcode:x2}.json`, where `set` is
already a free parameter (today always called with `"6502"`). `Harte6502Tests.cs` loops all 256 opcodes,
loads each vector file, and for every vector replays it against `Cpu<HarteBus>`, asserting final
registers, named RAM bytes, and the exact per-cycle bus log (address/data/read-or-write) against the
vector's `cycles` array. Nothing in `HarteCache` is 6502-specific — `Load(set, opcode)` works for any set
name that exists in the repo.

**What the repo actually contains** (confirmed, fetched live via the GitHub API today):

```
$ curl -s https://api.github.com/repos/SingleStepTests/65x02/contents
dir 6502
dir nes6502
dir rockwell65c02
dir synertek65c02
dir wdc65c02
file LICENSE   (MIT — "MIT License / Copyright (c) 2024 Thomas Harte et al")
file README.md
```

Each of the three 65C02 directories is a **complete, independent 256-opcode set** — confirmed by listing
`wdc65c02/v1`, `rockwell65c02/v1`, and `synertek65c02/v1`, each returning 256 files (`00.json`…`ff.json`).
Format is identical to the 6502 set: fetched `wdc65c02/v1/00.json`, confirmed 10,000 vectors, same
`name`/`initial`/`final`/`cycles` schema `HarteCase` already deserializes. Fetched `rockwell65c02/v1/07.json`
and `synertek65c02/v1/07.json` (opcode `$07`, which is `RMB0` on Rockwell/WDC but undefined on the base
chip) — both exist with 10,000 vectors each, confirming the sets are independently generated per
sub-variant rather than one set relabeled three times. `wdc65c02/v1/cb.json` (`$CB` = `WAI`, WDC-only) and
`rockwell65c02/v1/cb.json` both exist too (200 OK) — I did not diff their contents, so I can't confirm
*how* Rockwell's file treats an opcode it doesn't implement, only that a file exists for every opcode in
every set.

The repo's README states its generation methodology directly: each set comes from "an implementation …
verified by usage in an emulated machine" that "passes all other published test sets" — i.e., Harte's own
reference implementations per chip, not one generic 6502 core relabeled.

**Org-wide check for a 6510 set** (confirmed, fetched live): the `SingleStepTests` GitHub org publishes 21
repositories today (65x02, 65816, 8088, 680x0, z80, sm83, spc700, v20, sh4, m68000, ARM7TDMI, r3000,
8086/80186/80286/80386, huc6280, gameboy/nes-adjacent sets, etc.). **None is named 6510, and no 6510
directory exists inside `65x02`.** There is no Harte-style per-cycle vector set for the 6510 anywhere in
this organisation, full stop — the 6510's only difference from the 6502 (the on-chip `$00`/`$01` port) is
outside the scope of what this org's opcode-vector generation methodology produces, since it isn't an
opcode.

**What would change to consume `wdc65c02`/`rockwell65c02`/`synertek65c02`:** almost nothing in
`HarteCache.cs` — call `Load("wdc65c02", opcode)` etc. `Harte6502Tests.cs` needs to loop the three set
names and needs a CPU parameterized on the sub-variant, which doesn't exist in code yet: `CpuVariant.cs`
today is only an enum (`Mos6502`, `Mos6510`, `Wdc65C02`, `Rockwell65C02`, `Synertek65C02`) — the
`ICpuVariant` static-abstract-member engine the design calls for (§5.4) hasn't been built. That's
implementation work belonging to the phase-4 plan, not a gap in the gate itself.

**Verdict: usable as-is.** `Harte 65c02 ×3` is real — three full, independently-generated, MIT-licensed,
10,000-vectors-per-opcode sets (7,680,000 vectors total), fetchable with the exact same code path already
in production for the 6502. Reuse is total; the only work is the `ICpuVariant` plumbing the design already
plans. **No Harte 6510 set exists anywhere** — confirmed at the org level, not inferred.

---

## 2. Klaus Dormann's 65C02 extended-opcodes test

**Existing consumption:** `KlausCache.cs` downloads and caches any named file from
`Klaus2m5/6502_65C02_functional_tests/master/bin_files`, validates it's exactly 65536 bytes, nothing
65C02-specific. `KlausFunctionalTests.cs` loads `6502_functional_test.bin`, sets `PC=$0400`,
`S=$FD`, `P=Flag.U|Flag.I`, ticks/steps until PC stops advancing (or jams), and asserts it landed on the
documented success-trap address within a cycle ceiling.

**Confirmed the 65C02 binary is genuinely prebuilt:** fetched the repo's `bin_files` listing directly —

```
file 6502_functional_test.bin          65536
file 6502_functional_test.lst          728468
file 65C02_extended_opcodes_test.bin   65536   <-- exists, prebuilt, same repo/branch/dir as today's fetch
file 65C02_extended_opcodes_test.lst   587345
```

Same base URL the existing `KlausCache.BaseUrl` already points at — this is a zero-new-infrastructure
download, identical shape to the functional test.

**Entry address / success trap** (confirmed by downloading and reading the source and the listing):
source `65C02_extended_opcodes_test.a65c` sets `code_segment = $400`; the `.lst` footer confirms
`Program start address is at $0400 (1024)`. The success trap, read directly from the listing:

```
10731                                success         ;if you get here everything went well
10732 24f1 : 4cf124          >        jmp *           ;test passed, no errors
```

So: entry `$0400`, success trap `$24F1` (`jmp *`), same shape as the functional test's `$3469`.

**Configuration baked into the shipped binary** (confirmed by reading the source's `C O N F I G U R A T I O N`
block, and cross-checked against the `.lst` footer, which shows `RAM check disabled - RAM size not set` and
no I/O-report code path — both match the source's defaults, so the published `.bin` was built with these
defaults, not some other config):

- `wdc_op = 1` → comment: *"added WDC only opcodes WAI & STP (0=test as NOPs, >0=no test)"*. Value `1` means
  **WAI and STP are not tested at all** — not even as NOPs. WDC's two signature opcodes get zero coverage
  from this binary.
- `rkwl_wdc_op = 1` → comment: *"added Rockwell & WDC opcodes BBR, BBS, RMB & SMB … (0=test as NOPs,
  1=full test, >1=no test)"*. Value `1` means **RMB/SMB/BBR/BBS are fully tested** — this binary assumes a
  Rockwell/WDC-featured chip and would fail a Synertek core (which SixtyFiveXX's own `CpuVariant.cs`
  already documents as "CMOS base instruction set only") at every one of those opcodes.
- `report = 0` (self-trap loops only, no I/O channel) and `ram_top = -1` (RAM integrity check disabled) —
  like the functional test, needs nothing but RAM and CPU: no monitor, no I/O device.
- Source's own header: *"NMI, IRQ, STP & WAI are covered in the 6502_interrupt_test"* — and reading
  `6502_interrupt_test.a65` (already in the repo, already the subject of Phase 2b's 64tass port) confirms
  this: it has a `D_clear` config flag (0=NMOS, 1=CMOS) for 65C02 use, and contains explicit **manual**
  WAI/STP sections (*"manual tests for the WAI opcode of the 65c02… step to the WAI opcode, then manually
  tie the IRQ input low"*, *"manual test for the STP opcode"*) — i.e. WAI/STP testing is not automatable
  the same way the rest of Klaus's suite is, consistent with what Phase 2b already found about this file
  needing hand-porting rather than a straight download.

**How much of the existing harness is reusable:** effectively all of it. `KlausCache.cs` needs zero
changes — it already downloads any named file from that directory. `KlausFunctionalTests.cs`'s shape
(load → seed `PC`/`S`/`P` → step-until-PC-stalls loop → assert success address within a cycle ceiling) is
directly reusable for a new `KlausWdc65C02Tests.cs`; only the constants change (`StartAddress = 0x0400`
stays, `SuccessAddress = 0x24F1`, a fresh cycle ceiling) and the CPU needs to run under a CMOS
(`Wdc65C02`-shaped) variant once `ICpuVariant` exists.

**Verdict: usable with work.** The binary is genuinely prebuilt, downloadable today from the exact
infrastructure already in place, and needs no assembling — a strict improvement over the interrupt test
Phase 2b hit. But as shipped it certifies one specific opcode profile (base 65C02 + Rockwell/WDC bit-ops,
minus WAI/STP), not three. Recommendation: run it as-is against WDC and Rockwell (both have the bit-ops
this binary tests); for Synertek, either skip Klaus and rely on Harte's `synertek65c02` vectors (which
already encode Synertek's actual undefined-opcode behavior per-opcode, per §1) rather than reassembling
Klaus with different config flags — that reassembly would hit the same AS65-is-Windows-only wall Phase 2b
already solved once for the interrupt test, and duplicating that effort for a test whose functional
coverage substantially overlaps what Harte already checks more rigorously (full bus/cycle vs.
before/after/right snapshots) isn't obviously worth it. WAI/STP remain uncertified by any prebuilt Klaus
binary; if WDC's WAI/STP need certification, that's a second manual-port project the same shape as the
interrupt test, out of scope for a straight "download and run" gate.

---

## 3. The Wolfgang Lorenz suite — usable against a bare CPU core?

**What it is, and where it comes from:** roughly 200 individual C64 test programs (raw binaries, each with
a 2-byte load-address header) written by Wolfgang Lorenz, "developed with MACRO(SS)ASS+ by Wolfram
Roemhild," originally distributed as a D64 disk image. Primary source, fetched and read directly — the
suite's own header file (`Test Suite 2.15.txt`, mirrored in `tom-seddon/b2`'s
`etc/testsuite-2.15/Test Suite 2.15.txt`, sourced per its own comment from
`http://www.softwolves.com/arkiv/cbm-hackers/7/7114.html`):

> "C64 Emulator Test Suite - **Public Domain, no Copyright** … The suite are a few hundred C64 programs
> which check the details of the C64 they are running on."

**License: confirmed, no problem.** The suite is explicitly public domain by its own header, not merely
"freely distributed" as the design doc's table says.

**The decisive question — does it need a real C64 environment?** Yes for the part that matters, no
(mostly) for the part that doesn't. Two independent primary sources pin this down precisely:

1. A 2002 cbm-hackers mailing-list post by Christer Palm (fetched and read directly,
   `www.softwolves.com/arkiv/cbm-hackers/7/7114.html`), describing the minimal "test bench" he built to run
   the suite outside a real C64. It needs: five memory pokes (`$0002`, `$A002/$A003`, `$FFFE/$FFFF`,
   `$01FE/$01FF`), a 19-byte fake KERNAL IRQ-handler stub placed at `$FF48`, and five trap addresses
   (`$FFD2` print-char, `$E16F` load-next-test, `$FFE4` scan-keyboard, `$8000`/`$A474` exit) — no ROM
   content required for this subset.
2. Tom Harte's own test harness for this exact suite, `WolfgangLorenzTests.swift` in his `CLK` emulator
   (fetched and read in full,
   `TomHarte/CLK/OSBindings/Mac/Clock SignalTests/WolfgangLorenzTests.swift`) — implements precisely that
   recipe (identical trap addresses, identical 19-byte IRQ stub, explicitly cites the same Christer Palm
   post) and runs the general 6502-opcode tests (`lda`, `sta`, branches, flags, stack, flow, illegal
   opcodes, etc.) through it successfully with `hasCIAs: false`-equivalent-shaped bare test machine.

   **But this same file is the load-bearing evidence against using Lorenz for the 6510 delta.** Its own
   top-of-file comment reads, verbatim:

   > `// Unused Lorenz tests:`
   > `//	cpuport (tests the 6510 IO ports, I assume);`
   > `//	cputiming (unclear what this times against, probably requires VIC-II delays?);`
   > `//	mmu (presumably requires C64 paging?)`
   > `//	mmufetch (as above)`
   > `//	nmi`

   Tom Harte — the same author whose Harte/SingleStepTests vectors this project already depends on for the
   6502 — could not, or chose not to, run exactly the tests that would certify a 6510's actual delta
   (`cpuport`) on anything short of a paged C64 machine. The CIA tests (`cia1ta`, `cia1tb`, `icr01`, etc.)
   are run, but only with `CSTestMachine6502(processor: processor, hasCIAs: true)` — a real 6526 model, not
   a bare core.

   The suite's own documentation (`Test Suite 2.15.txt`, read in full) confirms *why* those five are
   excluded:
   - **`MMU`**'s pass criterion is literally the byte value read back from ROM-mapped addresses: *"A000 94
     read Basic, write RAM … E000 86 read Kernal, write RAM"* — `$94`/`$86` are specific bytes from the real
     Commodore BASIC/KERNAL ROM images at those offsets. A synthetic memory map can't satisfy this; it needs
     the actual ROM content.
   - **`CPUPORT`** tests the *board's* analog behavior, not the chip's: *"If both values match, the port
     behaves instable. **On my C64**, this will only happen when a datasette is connected."* This is a
     motherboard-wiring-dependent test (what's electrically attached to the unconnected port pins), not a
     6510-silicon-only property.
   - **`CPUTIMING`**'s and the CIA tests' pass criteria are cycle counts that only make sense against a real
     C64 bus (VIC-II steals, CIA-driven timing).

**What the CPU-only-runnable subset actually buys:** the general-opcode portion that *is* runnable on a
bare core (item 2 above) tests exactly the same 6502 instruction-set behavior the project's existing
2,560,000-vector Harte 6502 suite already certifies, but with Lorenz's coarser
before/after/right register-and-flag snapshots instead of Harte's full per-cycle bus log. It adds
essentially nothing the project doesn't already have, and (unlike Harte/Klaus) there's no ready-made C#
port of even that minimal harness sitting anywhere — it would have to be written from scratch, similar
in kind to the interrupt-test port Phase 2b already did once.

**Minimum needed for the CPU-only portion:** as demonstrated by Palm/Harte above — bare RAM, the five
memory pokes, the 19-byte fake-IRQ stub, and five trap addresses. No CIA, no VIC-II, no real ROM.
**Minimum needed for the actually-6510-specific portion (`cpuport`/`mmu`/`mmufetch`):** real
Commodore BASIC/KERNAL/Character ROM content at the correct addresses plus PLA-accurate bank switching —
i.e., a working (or near-working) C64 personality, which the design doc already scopes as its own,
later, differential-testing problem (§8.4), not a bare-core gate.

**Verdict: not usable as a bare-core 6510 gate, as currently named in the design doc.** The suite exists,
is free of licensing problems, and roughly half of it (the generic-6502-opcode half) is technically
runnable stand-alone — but that half duplicates coverage the project already has at higher rigor and would
still need a from-scratch harness port. The half that actually tests the 6510's real delta needs a real
C64 memory map and real Commodore ROMs, which makes it a personality-level gate, not a core-level one.
Recommend **not** treating "Wolfgang Lorenz suite" as the phase-5 core gate in the design doc; revisit its
`cpuport`/`mmu`/`mmufetch` tests later, if useful, once a C64 personality exists to run them against
differentially (§8.4) — and use something else for the core-only 6510 delta now (§4).

---

## 4. Other independent oracles for 65C02 / 6510

- **No dedicated Harte 6510 vector set exists anywhere** — confirmed at the `SingleStepTests` org level
  in §1, not just absent from the one repo already in use.

- **VICE's own testprogs — the strongest concrete lead for the 6510 port delta.** VICE (the long-standing
  GPL-2.0 C64/6510 emulator) ships a small, purpose-built, independently-authored test directory at
  `testprogs/CPU/cpuport/` (mirrored at `github.com/libsidplayfp/VICE-testprogs`, confirmed via that repo's
  API listing: `bitfade.s`/`.prg`, `test1.s`/`.prg`, `delaytime.s`/`.prg`, `initvalue.s`/`.bin`/`.crt`,
  `readme.txt`). This directory exists *specifically* to test the `$00`/`$01` unused-bit behavior — exactly
  the delta the task asked about. Read `test1.s` in full (fetched directly, 62 lines). It is small,
  deterministic, and self-contained:

  ```asm
  Start:
          sei
          ; output, write 1
          lda #$ff : sta 0 : sta 1
          ; must read back 1
          lda 1 : and #$80 : jeq fail
          ; set to input — must STILL read back 1 (retains last driven value)
          lda #0 : sta 0
          lda 1 : and #$80 : jeq fail
          ; write 0 to the *output latch* while configured as input — pin must NOT move
          lda #0 : sta 1
          lda 1 : and #$80 : jeq fail
          ... (mirror image with 0)
          lda #5 : sta $d020      ; border color, cosmetic
          ldx #0 : stx $d7ff      ; success flag
          jmp *
  ```

  This needs no KERNAL, no CIA, no VIC-II — the two trailing writes (`$D020`, `$D7FF`) are plain memory
  writes that a `FlatBus`-style harness can just treat as RAM, the same trick the project already uses for
  Klaus's tests. It tests exactly the digital latch/retention semantics of the on-chip port, deterministically,
  in a handful of cycles. By the same "execute a GPL test program without deriving source from it"
  reasoning the project already applies to Klaus's tests (see `KlausCache.cs`'s doc comment and
  `tests/SixtyFiveXX.Conformance/klaus/README.md`'s licence section), this looks usable the same way.

  Two caveats, both read directly from `readme.txt` in that same directory:
  - `bitfade.s`/`delaytime.s` measure the *analog* capacitive decay of the floating pins over real elapsed
    time (documented delay: "~200000us" on one specific board) — the readme's own authors call this
    **"partially guesswork based on observed behavior and not necessarily technically correct (corrections
    welcome)."** Not suitable as a deterministic pass/fail regression gate; more a characterization tool.
    Recommend treating this the same way the design doc already treats unstable NMOS opcodes (§ "genuinely
    analogue and vary by chip") — a documented, deliberate non-goal rather than a certification target.
  - `initvalue.crt`/`.bin` (power-on defaults: DDR=`$00`, DATA read-back=`$17` due to pull-ups on bits
    0/1/2/4) looks useful and deterministic, but I only read the theory-of-operation write-up in
    `readme.txt`, not a `.s` source for it (only compiled `.bin`/`.crt` are listed) — **this one is
    inferred, not confirmed**, and would need its source located (or the `.crt` disassembled) before relying
    on it.
  - Licensing: VICE is well-established as GPL-2.0-or-later; I did not find a separate `LICENSE` file in
    this specific testprogs mirror to quote directly, so that detail is carried over from general knowledge
    of the VICE project rather than a file I read myself — flagged as such. Worth a direct check before
    committing to this as a gate.
  - The suite's own readme cross-references Lorenz directly: *"some other things that depend on correct
    behavior: cpuports.prg from the lorenz testsuite"* — confirming this is understood in the community as
    the more tractable, purpose-built substitute for exactly the Lorenz `cpuport` test that turned out to be
    unusable in §3.

- **Perfect6502 / visual6502** — a transistor-level simulation built from a physically photographed die.
  Confirmed (from the project's own description) that this is a **6502** die, not a 6510 one; I found no
  evidence of an equivalent 6510 die reverse-engineering project in the time available. Not pursued further
  — noted here only so it isn't rediscovered and re-investigated later. This is weaker-confidence
  (search-result-level, not read-the-repo-level).

- **Klaus's `6502_interrupt_test.a65`, CMOS-configured** — already in the repo Phase 2b ported from
  (`D_clear` flag supports 65C02), with manual WAI/STP sections. Relevant to 65C02 WDC certification
  (§2's gap), not to the 6510.

**Recommendation for certifying the 6510:** split the claim in two, matching the design's own framing that
a 6510 is "a 6502 plus the on-chip `$00`/`$01` port":

1. **The inherited 6502 instruction set** needs no new oracle. Once `ICpuVariant` exists, a 6510-configured
   core running the plain 6502 opcode set is provably identical to the already-certified 6502 core — the
   `HasIoPort` flag only changes behavior of accesses to `$00`/`$01` themselves; everything else folds away
   at compile time per the design's `TVariant` scheme (§3, §5.4). No vectors needed for this part.
2. **The `$00`/`$01` port register itself** has no Harte-grade oracle anywhere that I found. Recommend
   adopting VICE's `testprogs/CPU/cpuport/test1.s` (and `initvalue`, once its source is confirmed) as a
   Klaus-style executed-GPL-binary gate — same licensing posture already established, same harness shape
   (assemble or fetch prebuilt `.prg`, load, run to a trap, assert). Explicitly scope out `bitfade`/`delaytime`
   as a documented, deliberate non-goal (real hardware is analog and board-dependent here — the same
   posture the design doc already takes on unstable NMOS opcodes). This is a much smaller lift than
   building a Lorenz harness from scratch, tests the actual delta directly instead of by way of a whole C64,
   and doesn't require Commodore ROMs to be a bare-core gate at all — `test1.s`'s two C64-flavored writes
   are trivially satisfiable by any RAM-backed bus.

Replace the design doc's phase-5 gate line — *"Wolfgang Lorenz suite"* — with something like *"VICE
cpuport testprogs (`test1`, `initvalue`); 6502 delta covered by existing Harte/Klaus 6502 certification"*
before planning phase 5. This is a design-doc change, not made here — this file is research only.

---

## Summary table

| Gate as named in design doc | Exists? | Usable? |
| --- | --- | --- |
| Harte 65c02 ×3 | Yes — `rockwell65c02`, `synertek65c02`, `wdc65c02`, each 256×10,000 MIT vectors | **Usable as-is** (needs `ICpuVariant` plumbing, not a suite problem) |
| Klaus 65C02 extended | Yes — prebuilt `.bin`, confirmed entry `$0400` / success `$24F1` | **Usable with work** — as shipped, certifies WDC/Rockwell bit-ops but not WAI/STP; Synertek needs Harte instead, not reassembly |
| Wolfgang Lorenz suite (6510) | Yes, suite exists, public domain | **Not usable** for the 6510-specific delta on a bare core — `cpuport`/`mmu`/`mmufetch` need real C64 ROMs + PLA, confirmed by Tom Harte's own harness excluding them |
| Harte 6510 | **Does not exist** — confirmed org-wide | N/A |
| VICE `testprogs/CPU/cpuport` | Yes, GPL, small, targeted | **Recommended replacement** for the 6510 delta gate |
