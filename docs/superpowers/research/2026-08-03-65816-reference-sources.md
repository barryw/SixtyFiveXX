# Research: authoritative sources for the 65816, and what they actually say

**Date:** 2026-08-03
**Status:** Research only — nothing implemented, nothing committed beyond this file.
**Scope:** Establish the documentary basis for phase 7 (the W65C816S core) *before* a design or plan is
written, so that every cycle count, effective-address formula and wrapping rule in phase 7 traces to a
named source rather than to recollection.

Confidence key: **confirmed** = I fetched the file and read the relevant text myself. **inferred** = built
from confirmed facts but not directly stated by a source. Anything weaker is called out explicitly.

---

## 1. Why this document exists

Phases 1–6 were built against per-cycle vectors with the datasheet as a secondary check, and that order
produced at least three findings where the documentation was wrong or silent and the vectors were right —
recorded in the code as such:

- CMOS `ADC`/`SBC` decimal: *"Derived from the vectors, not a datasheet … `V` is computed exactly as NMOS
  does — so 'correct N/V/Z' overstates it"* (`Op.AdcCmos` doc comment).
- CMOS `JMP (abs)`: the buggy address is still read and discarded; *"Adding a generic dummy cycle instead
  would produce the right cycle count and the wrong addresses"* (`MicroOp.JmpIndBugDummy`).
- The CMOS indexed-RMW split: `INC`/`DEC abs,X` always pay the fixup, the shifts do not. *"Measured across
  all six opcodes' vectors; no reading of 'read-modify-write does a dummy read instead of a dummy write'
  predicts the split"* (`MicroOpTable.IndexedRmwAlwaysPaysFixup`).

The 65816 is a much larger surface than any prior variant — 24-bit addressing, two modes, two independent
register widths, ten mode combinations, and per-cycle pin outputs the 8-bit parts do not have. Guessing
at that scale does not fail loudly; it fails as a slow drip of vector mismatches with no obvious cause.
So the sources are pinned first.

## 2. The sources

### 2.1 Primary — WDC W65C816S datasheet

**Confirmed.** Fetched from `https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf`;
55 pages, revision dated **March 13, 2024** (the current WDC release, not the 1980s NMOS-era document).

The part that matters is **Table 5-7, "Instruction Operation"** (pp. 36–42), which is a *per-cycle* table.
For every addressing mode it lists, for each cycle: the cycle number, **VPB**, **MLB**, **VDA**, **VPA**,
what is on the address bus, what is on the data bus, and **RWB**. Verbatim, the first block:

```
Table 5-7 Instruction Operation (continued on following 6 pages)
  Address Mode              Note   Cycle  VPB  MLB  VDA  VPA   Address Bus   Data Bus    RWB
1a. Absolute a                     1      1    1    1    1     PBR,PC        OpCode      1
ADC, AND, BIT, CMP, CPX, CPY,      2      1    1    0    1     PBR,PC+1      AAL         1
EOR, LDA, LDX LDY ORA, SBC,        3      1    1    0    1     PBR,PC+2      AAH         1
STA, STX, STY, STZ,                4      1    1    1    0     DBR,AA        Data Low    1/0
18 OpCodes, 3 bytes, 4 & 5  (1)    4a     1    1    1    0     DBR,AA+1      Data High   1/0
cycles
```

This is the same information the conformance vectors encode in their per-cycle pin strings. It is
therefore not merely a cross-check — it is a *specification of the thing being asserted*, which is
exactly what phases 1–6 lacked for the 8-bit parts and had to reverse out of the vectors instead.

**Table 5-7's notes** (p. 43), verbatim and complete, because several are load-bearing and at least two
are not what a careful reader would otherwise assume:

> Notes: Be aware that notes #4-7, 9 and 10 apply to the W65C02S and W65C816S. All other notes apply to
> the W65C816S only.
>
> 1. Add 1 byte (for immediate only) for M=0 or X=0 (i.e. 16-bit data), add 1 cycle for M=0 or X=0. REP,
>    SEP are always 3 cycle instructions and VPA is low during the third cycle. The address bus is PC+1
>    during the third cycle.
> 2. Add 1 cycle for direct register low (DL) not equal 0.
> 3. Special case for aborting instruction. This is the last cycle which may be aborted or the Status, PBR
>    or DBR registers will be updated.
> 4. Add 1 cycle for indexing across page boundaries, or write, or X=0. When X=1 or in the emulation mode,
>    this cycle contains invalid addresses.
> 5. Add 1 cycle if branch is taken.
> 6. Add 1 cycle if branch is taken across page boundaries in 6502 emulation mode (E=1).
> 7. Subtract 1 cycle for 6502 emulation mode (E=1).
> 8. Add 1 cycle for REP, SEP.
> 9. Wait at cycle 2 for 2 cycles after NMIB or IRQB active input.
> 10. RWB remains high during Reset.
> 11. BRK bit 4 equals "0" in Emulation mode.
> 12. PHP and PLP.
> 13. Some OpCodes shown are compatible only with the W65C816S.
> 14. VDA and VPA are not valid outputs on the W65C02S but are valid on the W65C816S. The two signals, VDA
>     and VPA, are included to point out the upward compatibility to the W65C816S. When VDA and VPA are
>     both a one level, this is equivalent to SYNC being a one level.
> 15. The PBR is only applicable to the W65C816S.
> 16. COP Latches.
> 17. In the emulation mode, during a R-M-W instruction the RWB is low during both write and modify
>     cycles.

**Note 4 is the one to flag.** "Add 1 cycle for indexing across page boundaries, **or write, or X=0**"
means an indexed *read* with 16-bit index registers pays the extra cycle **unconditionally**, page cross
or not. A design that carried the 6502's "reads only pay on a page cross" rule forward would be wrong for
every `abs,X` read with `x=0`, and would be wrong in a way that only shows up in half the native-mode
vectors. This is precisely the class of error this document exists to prevent.

**Note 17** is the second. In emulation mode the 65816's read-modify-write reverts to the NMOS
double-*write* (`RWB` low for both the modify and the write cycle), rather than the CMOS dummy read this
project implements for the 65C02. So the 816's RMW middle cycle is mode-dependent at run time, which is
unlike every variant handled so far, where it was fixed at table-build time.

**Note 1** pins `REP`/`SEP` at three cycles always, with `VPA` low on the third and the address bus at
`PC+1` — a detail no cycle-count table alone would convey.

### 2.2 Primary — Bruce Clark, "65C816 Opcodes", 6502.org

**Confirmed.** The canonical prose reference for effective-address formation. Last updated
**September 28, 2015**.

**Fetch note, worth recording:** the documented URL `http://www.6502.org/tutorials/65c816opcodes.html`
returns **404 to `curl`** (the site appears to reject non-browser agents; a fetch through the WebFetch
tool did return content). The reliable machine-readable source is the site's own GitHub mirror:

```
https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html
```

137 KB, ~3,270 lines of text once de-marked-up. Anyone reproducing this research should use the mirror.

What it supplies that the datasheet does not: an explicit effective-address formula for every addressing
mode, with the width of each intermediate calculation (24-bit, truncated to 16, truncated to 8) drawn as
a box diagram, plus §5.1's rules of thumb. The rules, verbatim:

> **5.1.1 PAGE BOUNDARY WRAPPING**
> Page boundary wrapping only occurs in emulation mode, and only for "old" instructions and addressing
> modes, i.e. instructions and addressing modes that are available on the 65C02. Page boundary wrapping
> only occurs in the following situations:
> A. When the DL register is $00 (and in emulation mode -- both conditions must be met), the direct page
>    wraps at a page boundary
> B. In emulation mode, the stack wraps at the page 1 boundary
>
> **5.1.2 BANK BOUNDARY WRAPPING**
> Bank boundary wrapping occurs in both native and emulation mode (and does not depend on which mode the
> 65C816 is in). The following are confined to bank 0 ("confined to" means they address bank 0 and wrap at
> the bank 0 boundary):
> A. The direct page
> B. The stack
> C. [absolute] and (absolute) addressing modes (JMP is the only instruction available for either
>    addressing mode)
>
> The following are confined to bank K:
> A. (absolute,X) addressing mode (JMP and JSR are the only instructions available for this addressing
>    mode)
> B. The Program Counter (i.e. the PC register); again, this means branches wrap at the bank K boundary
>
> source,destination addressing (i.e. the MVN and MVP instructions) wraps at both the source and
> destination bank boundaries.
>
> Otherwise, wrapping does not occur at bank boundaries.

And the "old versus new" distinction that governs which of those apply, §5.1.1 again:

> Note that because stack,S addressing is a "new" addressing mode (i.e. this addressing mode was not
> available on the 65C02), it does not wrap at a bank boundary under any circumstances. Likewise, since
> PEI is a "new" instruction, PEI $FF does not wrap at a page boundary (either the direct page part, or
> the (pushing onto the) stack part).

The appendix restates it with worked examples, including the one that most cleanly separates emulation
mode from 8-bit native mode:

> The two biggest differences are (a) emulation mode has direct page wrap around (when DL is $00, which is
> typically the case), but native mode does not, and (b) after a TXS, in emulation mode, the stack is on
> page 1 (since SH is forced to $01), but in 8-bit native mode, TXS transfers all 16 bits of the X
> register (since the S register is 16 bits wide) thus the stack is then on page 0 (since XH is forced to
> $00 when the x flag is 1).

and

> Another interesting fact is that direct page wrapping occurs (in emulation mode) when the DL register is
> $00; the DH register need not be zero.

That last sentence rules out the obvious shortcut of testing `D == 0`. The condition is `E && DL == $00`,
and the wrapped address keeps `DH` — Clark's §5.1.3 example gives `0 : DH : $LL+X` with the low byte
truncated to 8 bits. (This is the direct-page *index add*'s condition — confirmed, 320 discriminating
vectors. The indirect pointer's own `+1` read is a separate rule, narrower than this one: `D == $0000`,
not `DL == $00`. See §12.7, measured in phase 7c after this section was written.)

Mode-invariant forcing, from §6.10.4 (`XCE`) and §6.4.2 (`REP`/`SEP`):

> XCE exchanges (i.e. swaps) the c and e flags. This is the only instruction that changes the value of the
> e flag. Note that when the e flag is 1, the m and x flags are forced to 1 (and consequently the XH and
> YH registers are forced to $00), and the SH register is forced to $01.

> Note that when the e flag is 1, the m and x flag are forced to 1, so after the REP or SEP, both flags
> will still be 1 no matter what the operand is.

### 2.3 Arbiter — SingleStepTests/65816 vectors

**Confirmed.** `https://github.com/SingleStepTests/65816`, a **different repository** from the `65x02` set
`HarteCache` fetches today. Layout confirmed live via the GitHub API: a single `v1/` directory holding
**512 files**, named `{opcode:x2}.e.json` and `{opcode:x2}.n.json` — one emulation-mode and one
native-mode file per opcode, 10,000 vectors each, 20,000 per opcode, **5,120,000 total**.

Measured size: the first six files average **5.6 MB**, putting the set at roughly **2.9 GB** — on top of
the ~3.8 GB the five existing cores already need.

The JSON shape differs from the `65x02` sets. From the repository README, verbatim:

```
{
	"name": "3d e 1",
	"initial": {
		"pc": 9900, "s": 2191, "p": 171, "a": 25345, "x": 100, "y": 124,
		"dbr": 26, "d": 50304, "pbr": 111, "e": 1,
		"ram": [ [1751932, 14], [7284398, 187], [7284397, 24], [7284396, 61] ]
	},
	"final": { ... same shape ... },
	"cycles": [
		[7284396, 61, "dp-remx-"],
		[7284397, 24, "-p-remx-"],
		[7284398, 187, "-p-remx-"],
		[1751932, 14, "d--remx-"]
	]
}
```

New state fields: `dbr`, `pbr`, `d` (the direct-page register), `e`. `a`, `x`, `y`, `s` and `d` are
16-bit. RAM addresses are 24-bit.

The third element of each cycle entry is a **pin string**, and the README defines it verbatim:

> `cycles` provides a cycle-by-cycle breakdown of bus activity in the form `[address, value, outputs]`
> where output is a sequence of characters, in the order:
>
> * `d` if VDA is active, otherwise `-`;
> * `p` if VPA is active, otherwise `-`;
> * `v` if VPB is active, otherwise `-`;
> * `r` if RWB signalled a read, otherwise `w`;
> * `e` if E is active, otherwise `-`;
> * `m` if MX indicated M was active, otherwise `-`;
> * `x` if MX indicated X was active, otherwise `-`; and
> * `l` if MLB is active, otherwise `-`.
>
> The environment used does not activate RAM unless one of VDA, VPA or VPB is active, therefore affected
> bus transactions with the read line set do not produce a `value`. `null` is recorded in its place.

Two consequences for the design, both **confirmed** by that text:

1. **Internal cycles perform no memory access at all.** They still drive an address, and the vector still
   records it, but the value is `null`. Every 8-bit core in this project reads the bus on every cycle,
   including its dummy cycles, because on those parts every cycle *is* a real access. That is not true
   here, and a core that reads anyway is wrong against any bus with read side effects.
2. **The pin string is exactly Table 5-7's VDA/VPA/VPB/MLB/RWB columns**, plus `E`/`M`/`X` which are just
   status bits. So the datasheet specifies, and the vectors assert, the same eight signals. They can be
   implemented from the former and gated by the latter.

### 2.4 Third opinion — Eyes & Lichty, *Programming the 65816*

**Confirmed.** ISBN 0-89303-789-3, Eyes & Lichty, 1986. Supplied by the project owner; read from a local
copy, 638 pages with a clean embedded text layer (not OCR guesswork). Not redistributable and not
committed — this document records what it says, not the file.

Its standing is unusual and worth stating: the acknowledgments record that the authors worked directly
with **Bill Mensch, the designer of the 65816**, across "the two years in which it passed from first logic
drawings to functional silicon", testing against consecutive beta parts. That makes it a primary source on
*intent*. It is also from 1986, which makes it the oldest of the three and the one most likely to have been
overtaken.

The parts that earn it a place:

- **Ch. 4, "Switching Between 6502 Emulation and Native Modes"** (pp. 71–72) — the only source of the three
  that spells out the register transitions in both directions, including what is destroyed:

  > If the index registers were in sixteen-bit mode, they keep their low bytes, but their high bytes are
  > permanently lost. If, on the other hand, the accumulator was in sixteen-bit mode, the low byte remains
  > in accumulator A while the high byte remains accessible as the hidden accumulator B.

  and

  > The stack is truncated from sixteen to eight bits, with its high byte forced to a one … Any value in
  > the high byte of the stack pointer register is permanently lost.

- **Ch. 4, "65802/65816 Bugs and Quirks"** (p. 72) — the designer's-book statement of the rule Clark gives
  as "old versus new":

  > The most anomolous feature of the 65816 is the behavior of new opcodes while in the 6502 emulation
  > mode. While strict 6502 compatability is enforced for all 6502 and 65C02 opcodes, this is not the case
  > with new opcodes. For example, although the high byte of the stack register is always set to one,
  > wrapping of the stack during the execution of a single non-6502 instruction is not supported.

- **Ch. 4** (p. 54) on the program counter, confirming Clark §5.1.2 from the other direction:

  > Relative branches stay in the same bank; that is, you can't branch across bank boundaries. And program
  > segments cannot cross bank boundaries; if the program counter increments past $FFFF, it rolls over to
  > $0000 without incrementing the program counter bank.

- **Ch. 18–19** — a per-opcode reference with cycle counts and per-instruction footnotes, and a full
  opcode list.

## 3. Where the sources disagree, and what wins

Six conflicts have surfaced. The first two were found while extracting §2, and in both the outlier is the
book, and a later reader holding only the book would otherwise "correct" the implementation into a bug.
§3.3–§3.5 were added on 2026-08-05 by phase 7d task 1, and §3.6 on 2026-08-06 by phase 7d task 7; in those
four the outlier is a *primary* source contradicting itself or contradicting the vectors, which is the more
dangerous shape, because §4's precedence rules do not help when a source is its own outlier.

### 3.1 The bit positions of the m and x flags — the book is wrong

**Eyes & Lichty p. 72**, on switching native → emulation:

> The m bit (bit five) returns to its emulation role as the break flag; the x bit (bit four) becomes once
> again an unused flag.

That sentence puts the **break flag at bit 5**. Against it:

- **Clark §4** is the only source here that speaks directly to *native-mode* placement, explicitly:
  *"P register bit 5: m flag (native mode) / P register bit 4: x flag (native mode), b flag (emulation
  mode)"*.
- **WDC datasheet §2.8** does not address native mode at all, but pins down bit 4 in emulation mode,
  explicitly: *"When an interrupt occurs during Emulation mode, the Break flag is written to stack memory
  as bit 4 of the Processor Status Register."* That corroborates Clark's bit 4 by implication — it is the
  same bit x is said to become in emulation mode — without itself naming bit 5's role.
- **The 6502 itself**, and therefore this repository already: `Flag.B = 0x10` (bit 4), `Flag.U = 0x20`
  (bit 5) in `CpuState.cs`, certified across 2.56 M vectors. Same implication as the datasheet: break is
  bit 4, consistent with Clark, but silent on where `m` sits in native mode.

Clark states the native-mode assignment outright; the datasheet and this repository's own `Flag.B` support
it by implication rather than stating `m` = bit 5 themselves — and five phases of green vectors against one
sentence in the book. **Resolution:** bit 4 is `x` in native mode and `b` in emulation mode; bit 5 is `m`.
The book is in error here. The book's
p. 54 phrasing — *"the m and x flags replace the 6502's break and unused flags"* — pairs them in the same
misleading order, so this is not a lone typo, and the whole passage should be distrusted on bit numbering.

### 3.2 Indexed-read timing when the index registers are 16-bit — the book is incomplete

**Eyes & Lichty**, footnote 3 under `LDA` (p. 462) and throughout Ch. 18:

> 3 Add 1 cycle if adding index crosses a page boundary

That is the 6502's rule, carried forward. It omits the 65816's own condition. Against it:

- **WDC datasheet Note 4:** *"Add 1 cycle for indexing across page boundaries, **or write, or X=0**. When
  X=1 or in the emulation mode, this cycle contains invalid addresses."*
- **Clark §6.5**, which gives `LDA abs,X` (`$BD`) as the formula **`6-m-x+x*p`**.

Clark's formula is decisive, because the page-cross term is *multiplied by x*:

| m | x | page cross | `6-m-x+x*p` | meaning |
| --- | --- | --- | --- | --- |
| 1 | 1 | no | 4 | the familiar 6502 count |
| 1 | 1 | yes | 5 | the familiar page-cross penalty |
| 1 | 0 | either | **5** | 16-bit index: the extra cycle is paid **unconditionally**; `p` drops out |
| 0 | 0 | either | 6 | both widths 16-bit |

So with `x=0` the penalty is not conditional and page-crossing is not consulted. That is exactly the
datasheet's "or X=0", derived independently, and the `x*p` construction is far too deliberate to be a
slip. **Resolution: the datasheet and Clark are right; the book's footnote 3 is an incomplete
simplification.** A core built to the book alone would be wrong on every native-mode indexed read with
16-bit index registers — roughly half the `.n` vectors for those opcodes.

The book and Clark **agree** on the write case, which is the useful corroboration: the book gives
`STA abs,X` (`$9D`) as 5 cycles + footnote 1 only, with no page-cross footnote at all, and Clark gives
`6-m` with no `p` term. Indexed writes always pay, page cross or not — the datasheet's "or write".

### 3.3 The b flag's bit position, again — Clark §6.3.1 contradicts Clark §4

Added 2026-08-05, phase 7d task 1. §3.1 recorded the book putting the break flag at bit 5. **Clark makes
the same error, twice, in §6.3.1**, and a reader who reaches the interrupt section without having read §4
would take it as authoritative. Verbatim:

> When BRK pushes the P register, the b flag (i.e. bit 5) will be set

and, five paragraphs later:

> the correct way for an emulation mode BRK/IRQ handler to distinguish a BRK from an IRQ is to use the
> stacked value of the b flag (i.e. bit 5 of the stacked value of the P register)

Three things in the same document contradict both parentheticals:

- **Clark §4**, quoted already in §3.1: *"P register bit 5: m flag (native mode) / P register bit 4: x flag
  (native mode), b flag (emulation mode)"*.
- **Clark's own worked assembly**, printed immediately after the second sentence above, which masks with
  `BIT #$10` — `$10` is **bit 4**. The prose and the code in the same paragraph disagree with each other.
- **WDC datasheet §2.8**: *"When an interrupt occurs during Emulation mode, the Break flag is written to
  stack memory as bit 4 of the Processor Status Register."*

**Resolution: bit 4, as §3.1 already concluded.** The finding here is not a new answer, it is that the
error is not confined to the book: it now has to be expected in any source at any point, and §6.3.1 is
otherwise the single most useful passage on the interrupt sequences, so it will be read.

### 3.4 `WDM`'s second byte — Clark says it is read; the vectors say it is not

Added 2026-08-05, phase 7d task 1. Clark §6.7, verbatim:

> On the 65C816, it is acts like a 2-byte, 2-cycle NOP (note that the actual NOP instruction is only 1
> byte). **The second byte is read, but ignored.**

The vectors say otherwise, and this is a **measurement, not a citation**: in all 10,000 vectors of `42 n`
and all 10,000 of `42 e`, cycle 2 has value `null` and pin string `---r…` — `VDA = 0`, `VPA = 0`, no memory
access — while `PC` still advances by 2. See §14.2. The datasheet does not adjudicate: `WDM` appears in no
row of Table 5-7 and §7.16 says only *"It performs no operation."*

**Resolution: the vectors win, per §4 rule 4.** Cycle 2 is an internal cycle at `PBR,PC+1`, not a read.
Clark's sentence is presumably describing the *bus-cycle count* loosely — "read" in the sense of "consumed
by the program counter" — but taken literally it produces a bus access the vectors do not have, which is
exactly the failure mode §9's implied-row note warns about.

### 3.5 `STP`'s opcode in the datasheet's Table 5-5 — printed as `D8`, which is `CLD`

Added 2026-08-05, phase 7d task 1. In the datasheet's instruction-summary table (p. 33), the `STP` row's
implied-addressing column reads **`D8`**. `$D8` is `CLD`. `STP` is `$DB`.

Verified against a 300 dpi rendering of p. 33, not only the extracted text, because a column-alignment
artifact was the likelier explanation and had to be ruled out; the rendering shows `D8` printed in `STP`'s
row. The datasheet contradicts itself two pages earlier: its own opcode matrix prints the `$Dx` row as
`… CLD CMP PHX STP JML …`, putting `STP` at `$DB` correctly, and Clark §6.9 gives `DB 1 3 imp`.

**Resolution: `$DB`.** Harmless if the opcode table is built from the matrix or from Clark; a silent
clobbering of `CLD` if it is built from Table 5-5. Recorded because Table 5-5 is the table one naturally
reaches for — it is the one that carries the flag-effect columns.

### 3.6 `(abs,X)`'s pointer-read pins in Table 5-7 rows 2a/2b — the datasheet is wrong

Added 2026-08-06, phase 7d task 7. Rows 2a (`JMP (abs,X)`, `$7C`) and 2b (`JSR (abs,X)`, `$FC`) print
`VDA=0 VPA=1` on both pointer-read cycles — row 2a's cycles 5 and 6, row 2b's cycles 7 and 8 — which pins
them as program-stream reads. All 40,000 `$7C`/`$FC` vectors read **`d--r`** instead: `VDA` asserted, `VPA`
clear, exactly like every other pointer read on the part. Rows 3a/3b, printing `VDA=1 VPA=0` on the bank-0
pointer reads, are correct and unaffected; the error is confined to the two indexed-indirect rows. Failing
evidence, from the run that caught it: `7c e 1: cycle 4 expected [$72CB6A, $11, "d--remx-"], got
[$72CB6A, $11, "-p-remx-"]`, and the same on `7c n 1`, `fc e 1` and `fc n 1`.

**Resolution: the vectors win, per §4 rule 4.** The implementation follows them, not the datasheet. Same
shape as §3.4 — a primary source's stated bus behaviour overruled by pin strings — and as §13.1's row 16b
and §14.1's rows 22a/22c. Full write-up, including the discriminating run and the 40,000-vector count, at
§14.6.

## 4. Source precedence

Stated once, so it does not have to be re-litigated per opcode:

1. **WDC W65C816S datasheet (2024), Table 5-7 and its notes** — the specification for cycle counts, bus
   addresses, and pin states. Newest, and the only source that is per-cycle.
2. **Clark, "65C816 Opcodes" (2015)** — the specification for effective-address formation, wrapping,
   mode-forcing invariants, and per-opcode cycle *formulas*. Nothing contradicting the datasheet was found
   while extracting the above; on §3.2 the two agree against the book.
3. **Eyes & Lichty (1986)** — corroboration, intent, and prose explanation. Outranked by both of the
   above where they conflict (§3.1, §3.2), because it is the oldest and has now been shown wrong twice.
   Its unique value is the mode-transition detail in §2.4, which neither other source states as fully.
4. **SingleStepTests/65816 vectors** — the arbiter. Where any source and the vectors disagree, the vectors
   win, and the divergence is recorded in a code comment naming the source, the claim, and the measurement
   — the form `Op.AdcCmos` and `MicroOpTable.IndexedRmwAlwaysPaysFixup` already use.

Rule 4 is not a licence to skip rules 1–3. Deriving behaviour from vectors alone is what makes a core that
passes its suite and cannot explain itself; the documentation is what turns a passing test into a
justified one. §3.2 is the case in point: the vectors would eventually have caught the wrong indexed-read
timing, but only as a wall of mismatches with no stated cause.

## 5. Cycle formulas for the phase 7b slice

Taken verbatim from Clark §6.5 and §6.4.2/§6.10.4, since these are the exact opcodes phase 7b is gated on.
`m` and `x` are the flag values (0 or 1), `w` is 1 when `DL != $00` and 0 otherwise, `p` is 1 on a page
cross. Cross-checked against the datasheet's Table 5-7 row shapes and its Notes 1, 2 and 4.

| Mode | LDA | cycles | STA | cycles |
| --- | --- | --- | --- | --- |
| immediate | `$A9` | `3-m` (length `3-m`) | — | — |
| direct | `$A5` | `4-m+w` | `$85` | `4-m+w` |
| direct,X | `$B5` | `5-m+w` | `$95` | `5-m+w` |
| absolute | `$AD` | `5-m` | `$8D` | `5-m` |
| absolute,X | `$BD` | `6-m-x+x*p` | `$9D` | `6-m` |
| absolute,Y | `$B9` | `6-m-x+x*p` | `$99` | `6-m` |
| (direct,X) | `$A1` | `7-m+w` | `$81` | `7-m+w` |
| (direct),Y | `$B1` | `7-m+w-x+x*p` | `$91` | `7-m+w` |
| (direct) | `$B2` | `6-m+w` | `$92` | `6-m+w` |
| \[direct\] | `$A7` | `7-m+w` | `$87` | `7-m+w` |
| \[direct\],Y | `$B7` | `7-m+w` | `$97` | `7-m+w` |
| long | `$AF` | `6-m` | `$8F` | `6-m` |
| long,X | `$BF` | `6-m` | `$9F` | `6-m` |
| stack,S | `$A3` | `5-m` | `$83` | `5-m` |
| (stack,S),Y | `$B3` | `8-m` | `$93` | `8-m` |

Four structural facts fall straight out of that table and are worth naming, because each is a rule the
8-bit cores do not have:

- **`w` appears only on direct-page modes.** `stack,S`, `long`, `long,X` and absolute modes never pay it.
- **`p` appears only where `x*p` appears** — indexed *reads*, and only when `x=1`. Never on a write, never
  on `long,X`, never on `[direct],Y`.
- **`[direct],Y` has no page-cross term at all**, unlike `(direct),Y`. The long pointer makes the add a
  24-bit calculation with nothing to fix up.
- **`(stack,S),Y` is flat at `8-m`** — no `w`, no `p`. Consistent with stack,S being a "new" mode.

`REP`/`SEP` (`$C2`/`$E2`) are 3 cycles unconditionally — Clark §6.4.2 and datasheet Note 1, which adds
that `VPA` is low on cycle 3 and the address bus holds `PC+1`. `XCE` (`$FB`) is 2 cycles, Clark §6.10.4.

## 6. Clean-room posture, unchanged

Datasheets and published references are specifications, not implementations. Nothing here involves reading
another emulator's source, and the project's existing position — *"No ROM images, PLA dumps, or emulator
source from other projects are included or distributed"* — is not affected by any source above. The
SingleStepTests vectors are MIT and already consumed under that licence.

## 7. What this settles for phase 7b

| Question | Answer | Source |
| --- | --- | --- |
| Do internal cycles access memory? | No. They drive an address; no RAM activation. | 2.3, README verbatim |
| Are per-cycle pin states specified, or only observed? | Specified. Table 5-7, all five output signals. | 2.1 |
| Indexed read timing with 16-bit index registers | Extra cycle is unconditional when `x=0` — not page-cross-conditional. | Note 4 + Clark `6-m-x+x*p`; **book is wrong**, §3.2 |
| Indexed write timing | Always pays, page cross or not. | Note 4 ("or write") + Clark `6-m`; book agrees |
| P register bit 4 | `x` flag in native mode, `b` (break) in emulation mode. | Clark §4 + datasheet §2.8; **book is wrong**, §3.1 |
| P register bit 5 | `m` flag. Never the break flag. | Clark §4; **book is wrong**, §3.1 |
| Direct-page penalty | +1 cycle when `DL != $00`. | Note 2 |
| Direct-page page-wrap condition | `E == 1 && DL == $00`, keeping `DH`. Not `D == 0`. This is the *index add*'s condition; the indirect pointer's own `+1` read is narrower — `D == $0000` — see §12.7. | 2.2, §5.1.1 + appendix |
| Which things wrap in bank 0 | Direct page, stack, `(abs)`/`[abs]` pointers. | 2.2, §5.1.2 |
| Which things wrap in bank K | `(abs,X)` pointers, and PC — so branches wrap at the bank boundary. | 2.2, §5.1.2 |
| Does `abs,X` cross into the next bank? | Yes — "Otherwise, wrapping does not occur at bank boundaries." | 2.2, §5.1.2 |
| Do "new" modes wrap in emulation mode? | No. `stack,S` never wraps at a bank boundary; `PEI` never page-wraps. | 2.2, §5.1.1 |
| `REP`/`SEP` timing | Always 3 cycles; `VPA` low on cycle 3; address bus `PC+1`. | Note 1 |
| Emulation-mode invariants | `m=1`, `x=1`, `XH=YH=$00`, `SH=$01`; only `XCE` changes `e`. | 2.2, §6.10.4; book p.71 agrees |
| Native → emulation transition | `XH`/`YH` permanently lost; `SH` forced to `$01` and the old `SH` lost; `A` low byte stays in A, high byte survives as B. | 2.4, book pp. 71–72 |
| Emulation → native transition | `m`/`x` forced to 1, so widths stay 8-bit; S keeps its page-one value; all else unchanged. | 2.4, book p. 71 |
| Emulation-mode RMW direction | `RWB` low on **both** modify and write — NMOS-style, not the 65C02 dummy read. | Note 17 |
| Per-opcode cycle counts for the 7b slice | Exact formulas in §5 above. | Clark §6.5 |
| Vector set location and size | Separate repo, 512 files, ~2.9 GB, 5.12 M vectors. | 2.3 |

## 8. Deferred to later phases, listed so they are not forgotten

- **ABORT (`ABORTB`)** and datasheet Note 3. No vectors exercise it; out of scope for phase 7 entirely.
- **Note 9** — the two-cycle wait at cycle 2 after `NMIB`/`IRQB`. Phase 7d, with interrupts.
- **Note 11** — `BRK` bit 4 is `0` in emulation mode. Phase 7d.
- **Note 16** — "COP Latches". Phase 7d.
- **Note 17** — mode-dependent RMW direction. Phase 7c′, with the read-modify-write opcodes; noted now
  because it is the first behaviour in this project that cannot be resolved at table-build time.
- **The book's remaining cycle-count footnotes.** §3.2 showed footnote 3 is an incomplete simplification.
  The rest of Ch. 18's footnotes have not been audited opcode by opcode, and must not be used as a primary
  source in 7c/7d. Use Clark's formulas and Table 5-7; the book is corroboration only.
- **Appendix E of the book** is a reprint of the *1986* W65C816 data sheet. Superseded by §2.1 (2024) and
  deliberately not consulted; recorded so nobody mistakes it for a fourth source.

---

## 9. Per-cycle bus sequences for the phase 7b slice

Added 2026-08-04, after phase 7a landed. Transcribed from **WDC datasheet Table 5-7** (pp. 36–42) —
the source described in §2.1 — for every addressing mode phase 7b implements. Column order in the
datasheet is `Cycle | VPB | MLB | VDA | VPA | Address Bus | Data Bus | RWB`; below, only the cycle
number, the VDA/VPA pair, the address bus expression and what is on the data bus are kept, since VPB
and MLB are `1` (inactive) throughout every one of these modes.

Cycle numbers suffixed with a letter are **conditional** and carry the note that gates them:

- `(1)` — the high half of a 16-bit access. Taken when the relevant width flag is 0.
- `(2)` — the direct-page penalty. Taken when `DL != $00`.
- `(4)` — the indexing cycle. Taken on a page cross, **or on a write, or when `x = 0`** (§3.2).

`DO` is the direct-page offset operand byte, `AA` the effective address, `AAB` the bank byte of a long
address, `SO` the stack-relative offset operand.

### The notation that matters most

**`IO` means an internal cycle: VDA and VPA are both 0 and no memory access occurs.** This is the
behaviour `IBus.Internal` exists to model, added in phase 7a. Every `IO` row below is a call to it, not
a read.

### Implied — row 19a, and the first surprise

```
XCE (and CLC, SEC, TAX, TXS, TCD, TCS, TDC, TSC, TXY, TYX, NOP, …)  25 opcodes, 1 byte, 2 cycles
  1   VDA=1 VPA=1   PBR,PC     OpCode
  2   VDA=0 VPA=0   PBR,PC+1   IO
```

*** Cycle 2 is an internal cycle, not a dummy read. *** On every 65xx core this project has built so
far, an implied instruction's second cycle is a genuine read at PC — `MicroOp.ImpliedExec` performs
one. The 65816 does not read there at all. Any implied 65816 instruction built by reusing the existing
implied micro-op will produce a bus access the vectors do not have, on the very first opcode
implemented. This is also the concrete justification for phase 7a's `IBus.Internal`.

Note the address driven: **`PBR,PC+1`** — one past the opcode, not the opcode's own address.

### Direct — row 10a

```
LDA dp / STA dp   16 opcodes, 2 bytes, 3 / 4 / 5 cycles
  1        VDA=1 VPA=1   PBR,PC       OpCode
  2        VDA=0 VPA=1   PBR,PC+1     DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1     IO        <- DL != $00
  3        VDA=1 VPA=0   0,D+DO       Data Low
  3a  (1)  VDA=1 VPA=0   0,D+DO+1     Data High
```

The data access is at **bank 0**, and the direct-page penalty's internal cycle drives `PBR,PC+1`.

### Direct,X — row 16a

```
LDA dp,X / STA dp,X   12 opcodes, 2 bytes, 4 / 5 / 6 cycles
  1        VDA=1 VPA=1   PBR,PC       OpCode
  2        VDA=0 VPA=1   PBR,PC+1     DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1     IO        <- DL != $00
  3        VDA=0 VPA=0   PBR,PC+1     IO        <- the indexing cycle, unconditional
  4        VDA=1 VPA=0   0,D+DO+X     Data Low
  4a  (1)  VDA=1 VPA=0   0,D+DO+X+1   Data High
```

Two internal cycles at the same address when `DL != $00`. Cycle 3 is unconditional — unlike the 6502's
`ZpIndexX`, which performs a real dummy read at the unindexed address.

### Absolute — row 1a

```
  1        VDA=1 VPA=1   PBR,PC       OpCode
  2        VDA=0 VPA=1   PBR,PC+1     AAL
  3        VDA=0 VPA=1   PBR,PC+2     AAH
  4        VDA=1 VPA=0   DBR,AA       Data Low
  4a  (1)  VDA=1 VPA=0   DBR,AA+1     Data High
```

### Absolute,X — row 6a, and Absolute,Y — row 7

```
  1        VDA=1 VPA=1   PBR,PC              OpCode
  2        VDA=0 VPA=1   PBR,PC+1            AAL
  3        VDA=0 VPA=1   PBR,PC+2            AAH
  3a  (4)  VDA=0 VPA=0   DBR,AAH,AAL+XL      IO        <- page cross, or write, or x=0
  4        VDA=1 VPA=0   DBR,AA+X            Data Low
  4a  (1)  VDA=1 VPA=0   DBR,AA+X+1          Data High
```

Cycle 3a's address is the **mis-indexed** one — high byte un-carried — which is the datasheet's "this
cycle contains invalid addresses" in Note 4, and the direct analogue of the NMOS dummy read. `,Y` is
identical with `YL` substituted.

### (Direct,X) — row 11

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1        IO        <- DL != $00
  3        VDA=0 VPA=0   PBR,PC+1        IO        <- the indexing cycle, unconditional
  4        VDA=1 VPA=0   0,D+DO+X        AAL
  5        VDA=1 VPA=0   0,D+DO+X+1      AAH
  6        VDA=1 VPA=0   DBR,AA          Data Low
  6a  (1)  VDA=1 VPA=0   DBR,AA+1        Data High
```

### (Direct) — row 12

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1        IO
  3        VDA=1 VPA=0   0,D+DO          AAL
  4        VDA=1 VPA=0   0,D+DO+1        AAH
  5        VDA=1 VPA=0   DBR,AA          Data Low
  5a  (1)  VDA=1 VPA=0   DBR,AA+1        Data High
```

Pointer fetched from **bank 0**; data addressed through **DBR**.

### (Direct),Y — row 13

```
  1        VDA=1 VPA=1   PBR,PC              OpCode
  2        VDA=0 VPA=1   PBR,PC+1            DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1            IO
  3        VDA=1 VPA=0   0,D+DO              AAL
  4        VDA=1 VPA=0   0,D+DO+1            AAH
  4a  (4)  VDA=0 VPA=0   DBR,AAH,AAL+YL      IO        <- page cross, or write, or x=0
  5        VDA=1 VPA=0   DBR,AA+Y            Data Low
  5a  (1)  VDA=1 VPA=0   DBR,AA+Y+1          Data High
```

### [Direct] — row 15

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1        IO
  3        VDA=1 VPA=0   0,D+DO          AAL
  4        VDA=1 VPA=0   0,D+DO+1        AAH
  5        VDA=1 VPA=0   0,D+DO+2        AAB
  6        VDA=1 VPA=0   AAB,AA          Data Low
  6a  (1)  VDA=1 VPA=0   AAB,AA+1        Data High
```

Three-byte pointer from bank 0; the data bank comes from the **pointer's own third byte**, not DBR.

### [Direct],Y — row 14

As `[Direct]`, with the final access at `AAB,AA+Y` / `AAB,AA+Y+1`. ***No indexing cycle at all*** — no
`(4)` row exists for this mode, which is why Clark's formula `7-m+w` for `$B7` carries no `p` term
(§5). The 24-bit add needs no fixup.

### Absolute Long — row 4a, and Absolute Long,X — row 5

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        AAL
  3        VDA=0 VPA=1   PBR,PC+2        AAH
  4        VDA=0 VPA=1   PBR,PC+3        AAB
  5        VDA=1 VPA=0   AAB,AA          Data Low       (AAB,AA+X for long,X)
  5a  (1)  VDA=1 VPA=0   AAB,AA+1        Data High      (AAB,AA+X+1 for long,X)
```

Also no indexing cycle for `long,X` — again matching Clark's flat `6-m`.

### Stack Relative — row 23

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        SO
  3        VDA=0 VPA=0   PBR,PC+1        IO        <- unconditional, and there is no (2) penalty
  4        VDA=1 VPA=0   0,S+SO          Data Low
  4a  (1)  VDA=1 VPA=0   0,S+SO+1        Data High
```

No direct-page penalty — confirming §5's observation that `w` appears only on direct-page modes.

### (Stack Relative),Y — row 24

```
  1        VDA=1 VPA=1   PBR,PC          OpCode
  2        VDA=0 VPA=1   PBR,PC+1        SO
  3        VDA=0 VPA=0   PBR,PC+1        IO
  4        VDA=1 VPA=0   0,S+SO          AAL
  5        VDA=1 VPA=0   0,S+SO+1        AAH
  6        VDA=0 VPA=0   0,S+SO+1        IO        <- second internal cycle, unconditional
  7        VDA=1 VPA=0   DBR,AA+Y        Data Low
  7a  (1)  VDA=1 VPA=0   DBR,AA+Y+1      Data High
```

Two internal cycles, the second driving `0,S+SO+1` rather than `PBR,PC+1`. Flat `8-m` in Clark — no
`w`, no `p`.

### Immediate, and REP/SEP

Immediate is the operand fetched at `PBR,PC+1`, plus `PBR,PC+2` when the width flag is 0 — Note 1's
"add 1 byte for immediate only". `REP`/`SEP` are three cycles always, and Note 1 states the third
explicitly: **`VPA` low, address bus `PC+1`**.

### What this section settles

Every cycle of every mode in the 7b slice now has a stated address, a stated VDA/VPA pair, and a
stated condition. The plan can be written against these rows rather than against a cycle count, and a
disagreement with the vectors becomes a specific row to re-read rather than a search.

## 10. Reset initialization, and the one thing it leaves ambiguous

Added 2026-08-04, during phase 7b's review. **Confirmed** — WDC datasheet §2.25, "Reset" (p. 15). This
is the source `Cpu.Reset()`'s 65816 block should have been checked against from the start; it was not,
which is how a missing `D` clear survived review once already (see the code fix this section
accompanies).

The datasheet gives reset initialization as two small tables. The register row, verbatim:

```
D=0000  SH=01, SL=—  DBR=00  XH=00, XL=—  PBR=00  YH=00, YL=—  A=—
```

And the P register row, verbatim:

```
                                        P Register
     N        V        M        X        D        I        Z       C/E
                       1        1        0        1                 1
              Shaded Area = Not Initialized
```

Read together, that pins down exactly six things: `D` (the direct/page-zero register) `= $0000`,
`SH = $01`, `DBR = $00`, `XH = $00`, `PBR = $00`, `YH = $00`, `M = 1`, `X = 1`, `D` (the decimal flag,
a different `D` from the register above — WDC's own naming collision, not this document's) `= 0`, and
`I = 1`. Everything else in both tables is explicitly the shaded "not initialized" case: `SL`, `XL`,
`YL`, `A`, and, in the P register, `N`, `V` and `Z`. **`N`, `V`, `Z` and `A` must stay untouched by
reset** — the same "reset does not clear the registers" position `Cpu.Reset()`'s doc comment already
takes for the 8-bit cores, extended here to the flags and register the 65816 table speaks to that the
8-bit cores' reset behaviour never had occasion to mention.

**The one column that cannot be resolved from this table alone is the last one, labelled `C/E`,
value `1`.** That is ambiguous on its face between "the carry flag is set" and "the emulation flag is
set" — and emulation mode is independently confirmed elsewhere (§2.2, Clark on `XCE`; Eyes & Lichty
ch. 4) to be forced on reset regardless, which is already covered by `M`/`X` both reading `1` above.
Nothing in either table, or anywhere else surveyed for this document, states outright which of the two
the shared column means, or whether it means both. **No SingleStepTests vector covers reset at all** —
the vector set exercises instruction execution, not the power-on sequence — so §4's usual arbiter has
nothing to arbitrate with here. Given that, `Cpu.Reset()` leaves `C` untouched rather than guessing:
setting it on the strength of an ambiguous column would be exactly the kind of unverified assumption
this document exists to keep out of the implementation. If a future source resolves the ambiguity, the
fix is one line in `Cpu.Reset()`'s 65816 block, guarded by this paragraph so it doesn't need
rediscovering.

---

## 11. `XCE` and the stack pointer's high byte — measured, then explained

Added 2026-08-04, during phase 7b Task 3.

Implementing `XCE` produced a rule the earlier sections do not state. §7 says only that emulation mode
forces `SH = $01`, without saying whether the condition is evaluated before or after `XCE` swaps `c`
and `e`. The implementer measured it exhaustively across all 20,000 `$FB` vectors and found:

> `SH` is forced to `$01` whenever **either** the old or the new `E` is 1 — unlike `m`, `x`, `XH` and
> `YH`, whose forcing follows the **new** `E` alone.

So a native → emulation switch forces `SH`, and so does an emulation → native switch. That second half
looks arbitrary from the vectors alone, and it was recorded as unexplained.

**It is not unexplained.** Eyes & Lichty (§2.4) states the mechanism directly, p. 71:

> While the emulation mode stack pointer register is only an eight-bit register, it can be thought of as
> a sixteen-bit register with its high byte hard-wired to one, so that the emulation stack is always in
> page one. When the 65802 is switched from emulation to native mode, the sixteen-bit native mode stack
> pointer assumes the same value the emulation mode stack pointer has been pointing to — a page one
> address.

That is the `oldE = 1` half exactly: leaving emulation mode, the newly-16-bit `S` takes the page-one
value the 8-bit emulation pointer was standing at, so `SH` reads `$01` on the way out. And p. 72 gives
the other direction, entering emulation:

> The stack is truncated from sixteen to eight bits, with its high byte forced to a one … Any value in
> the high byte of the stack pointer register is permanently lost.

The two halves are one mechanism seen from either side, not two rules. Measurement and primary source
agree, which is the strongest position a claim in this document can be in — and it is worth noting that
the book, wrong twice in §3, is right here and is the only source that explains it.

**Contrast with `m`, `x`, `XH`, `YH`**, whose forcing follows the new `E` alone: those live in the
processor status register and the index registers, and nothing about them survives a mode change the way
a stack address does. Do not generalise `SH`'s rule to them; the vectors disagree, and so does the book.

---

## 12. Phase 7c's four unsettled questions — the section the 7c plan calls "§10"

Added 2026-08-05, before any phase 7c code was written. Same practice as §9: transcribe first, implement
second.

**Numbering note, read this before hunting for a missing section.** The phase 7c plan
(`docs/superpowers/plans/2026-08-05-phase7c-bulk-alu.md`) names this section **§10** and its parts
**§10.1**–**§10.5**, because it was written when this document ended at §9. Phase 7b then added §10
(reset initialisation) and §11 (`XCE` and `SH`), and both are cited by section number from doc comments —
§10 from `src/SixtyFiveXX/Cpu.cs` (lines 617 and 642) and `tests/SixtyFiveXX.Tests/W65C816StateTests.cs`
(line 105), §11 from `src/SixtyFiveXX/Cpu.Exec.cs` (line 96). Renumbering them would silently falsify
those comments, so this material lands at §12 instead. The mapping is exact and one-to-one:

| The plan says | Read as | Subject |
| --- | --- | --- |
| §10.1 | §12.1 | 16-bit decimal `ADC`/`SBC` |
| §10.2 | §12.2 | the `Op` member decision |
| §10.3 | §12.3 | Table 5-7's `Direct,Y` row |
| §10.4 | §12.4 | the x-width immediates |
| §10.5 | §12.5 | a cycle formula for every opcode in phase 7c |

**Sources, fetched for this section rather than recalled.** Clark, "65C816 Opcodes", from the GitHub
mirror `https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
(137,171 bytes, footer "Last Updated September 28, 2015") — the mirror because 6502.org itself 404s
non-browser agents (§2.2). WDC W65C816S datasheet from
`https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf` (1,532,025 bytes, header
"March 13, 2024"), Table 5-7 on pp. 36–42 and Table 7-1 on p. 49. Quotations below are verbatim from
those two files. Notation is §5's: `m` and `x` are the flag values (0 or 1), `w` is 1 when `DL != $00`
and 0 otherwise, `p` is 1 on a page cross.

### 12.1 16-bit decimal `ADC` and `SBC` — mostly a recorded gap (the plan's §10.1)

**Summary in one line: Clark states the flag *meanings* in decimal mode, gives one worked 16-bit example,
and states that 8-bit decimal behaves like the 65C02 — but is silent on the correction algorithm at every
width. The datasheet is silent too.**

**What Clark does state.** §4, on the `d` flag:

> When the d flag is 0, the ADC and SBC instructions perform binary arithmetic. When the d flag is 1,
> the ADC and SBC instructions perform BCD arithmetic.

§6.1.1.1, the arithmetic itself, which is stated once and not per-mode:

> The formula for ADC is:
>
> accumulator = accumulator + data + carry
>
> The formula for SBC can be written several ways; one way is:
>
> accumulator = accumulator - data - 1 + carry

and, immediately after the binary-mode flag rules, the whole of what §6.1.1.1 says about decimal-mode
flags:

> When the d flag is 1, the n, z, and c flags have the same meaning (i.e. the n flag reflects the high
> bit of the result, the z flag indicates when the result is zero, and the carry indicates when the
> result is outside the range 0 to 9999). The v flag is overwritten, but BCD is really an unsigned
> representation, so the v flag can be considered invalid, since it does not represent a signed
> arithmetic overflow.

Note that this passage is width-neutral: it does not distinguish `m = 0` from `m = 1`, and "the range 0
to 9999" is the four-digit range, so the sentence is at least written with the 16-bit case in view. The
preceding binary-mode paragraphs *do* split by width explicitly ("bit 15 when the m flag is 0, bit 7 when
the m flag is 1"); the decimal paragraph does not.

**The one worked 16-bit decimal example Clark gives**, §6.1.1.1, verbatim:

> Example 2: If the accumulator is $0001, the m flag is 0, the d flag is 1, and the c flag is 1, then
> after SBC #$2003
>
> the accumulator will be $7998
> the n flag will be 0
> the z flag will be 0
> the c flag will be 0

This is the only place any source surveyed states a 16-bit decimal result, and it is an `SBC` example —
Clark gives no decimal `ADC` example at any width. It pins down exactly one thing beyond the accumulator
value, for `SBC`: **`N` comes from the corrected result, not from the binary intermediate.** Whether
`ADC`'s decimal `N` at 16 bits comes from the same place is a separate, unestablished question; see gap 5
below. The binary intermediate is not inferred here — Clark's **Example 1** immediately above is the same
operands with `d = 0` and states the accumulator will be `$DFFE`, whose bit 15 is 1. For `d = 1` Clark
states `n = 0`, which is bit 15 of the corrected `$7998`. `Z` and `C` are not discriminated by this
example (both agree between the binary intermediate and the corrected result), and `V` is not listed at
all.

**What Clark states about 8-bit decimal, in the §6 preamble rather than in §6.1.1.1.** This passage sits
under the heading `6  INSTRUCTIONS`, after the column key and before §6.1.1.1, which is why a search
confined to the `ADC SBC` section misses it. Verbatim:

> In general, in emulation mode (and for 8-bit results in native mode), the 65C816 has the same behavior
> as 65C02 but the same cycle counts as the NMOS 6502. For example, when the d flag is 1 and the m flag
> is 1, ADC #$00 will have valid n, z, and c flag results (like the 65C02, but unlike the NMOS 6502), but
> will take 2 cycles (like the NMOS 6502, but unlike the 65C02).

Three things follow, and the limits on each matter as much as the claim:

- **Emulation mode and 8-bit native mode are grouped together**, both described as behaving like the
  65C02. So the question "does decimal behaviour differ between emulation mode and 8-bit native mode?"
  is **not** a gap — Clark answers it, in the negative. The hedge "In general" is his, and is preserved
  here rather than dropped; it is a general statement, not a per-flag guarantee.
- **It is scoped to 8-bit results.** It says nothing whatever about `m = 0`, which is the case phase 7c
  must newly implement. The 16-bit correction algorithm remains what Example 2 alone speaks to.
- **It enumerates `n`, `z` and `c`, and omits `v`.** That omission is consistent with §6.1.1.1's "the v
  flag is overwritten … can be considered invalid", and with this codebase's measured finding that the
  65C02's decimal `V` is computed exactly as NMOS does. `V` is not covered by the identity claim.

It also corroborates §12.5's cycle answer a third time, from a worked example rather than a rule:
`ADC #$00` with `d = 1`, `m = 1` takes 2 cycles, which is `3-m` with no decimal term.

**What Clark does not state — recorded as silence, in those words.** The document was searched in full
for `decimal`, `BCD`, `nibble`, `digit`, `$06`, `$60`, `correct`, `adjust`, `d flag`, `additional` and
`cycle`. **The sources are silent on the decimal correction algorithm.**

*(Search note, because it cost this section a revision: the §6 preamble passage above contains neither
"decimal" nor "BCD" — it says "when the d flag is 1" — so the first pass over this section, searching only
the obvious decimal vocabulary, missed it and wrongly recorded emulation-versus-native decimal behaviour
as a gap. Clark names flags by letter far more often than by name. Search `d flag`, not `decimal`. A
second trap in the same document: it is paginated with form feeds, so `takes no additional cycles` spans
a blank line as `additional\n\ncycles` and does not match as a literal phrase.)* Clark nowhere describes a correction — not nibble-wise, not
`$60`/`$06`-style as this codebase's `SbcCmos` uses, not anything, and not at 8 bits either. The brief's
anticipated shape ("Clark describes 8-bit decimal mode but says nothing about 16-bit") does not apply:
Clark describes the algorithm at *no* width. He states what the flags mean, what one 16-bit `SBC`
produces, and that 8-bit decimal behaves like the 65C02 — and stops short of ever saying how the
correction is performed, on any part, at any width.

Specifically, the following are **not** established by any source and must be treated as open by task 5:

1. **The correction algorithm**, at 8 or 16 bits. No source surveyed gives one.
2. **How `V` is computed in decimal mode.** Clark says only that it "is overwritten" and "can be
   considered invalid". "Overwritten with what" is not stated.
3. **Whether `Z` and `C` are taken from the corrected result or the binary intermediate.** Clark's
   wording ("the z flag indicates when the result is zero", "the carry indicates when the result is
   outside the range 0 to 9999") reads as the corrected result, but Example 2 does not discriminate, and
   he never uses the words "binary result" or "intermediate" in a decimal-mode sentence. `N` alone is
   pinned for `SBC`, by Example 2.
4. **Behaviour on invalid BCD input digits** (nibbles `$A`–`$F`). Not mentioned anywhere.
5. **Whether `ADC`'s decimal `N` at 16 bits comes from the corrected result or the binary intermediate.**
   Example 2 pins this for `SBC` only; Clark gives no decimal `ADC` example at any width, so nothing
   licenses carrying the `SBC` answer over to `ADC`. This is not academic: this codebase's own NMOS pair
   already diverges on the general question of where decimal `N` comes from. In
   `src/SixtyFiveXX/Cpu.Exec.cs`, `Adc`'s decimal path takes `N` from the partially corrected high nibble
   (`_s.N = (hi & 0x08) != 0;`, line 380), while `Sbc`'s takes it from the binary difference computed
   before any decimal correction runs (`_s.N = (binary & 0x80) != 0;`, line 444). `ADC` and `SBC` are not
   guaranteed to agree here, and the 65816 must be measured rather than assumed.

Note what is **not** on that list any more: whether decimal behaviour differs between emulation mode and
8-bit native mode. The §6 preamble quoted above groups the two, so that question is answered by a source
and is not a gap. Note also that the preamble constrains gaps 1 and 3 **at 8 bits only** — it asserts
65C02-like behaviour there without giving the algorithm, so task 5 may use the 65C02 path as a *hypothesis*
for `m = 1` and must still measure it, and must not carry that hypothesis to `m = 0`, which no source
covers.

**What the datasheet adds, and why it is not a specification of the above.** Table 7-1 "Caveats" (p. 49),
`(Flag Reg)` row, `W65C816S` column, verbatim:

> N,V and Z flags valid in decimal mode. D=0 after reset/interrupt

Three reasons that sentence does not close gaps 1–5. It says nothing about the algorithm. It says nothing
about `C`. And it is **word-for-word the same claim the same table's row makes for the W65C02 and
W65C02S columns** ("N,V and Z flags valid in decimal mode. D=0 after reset/interrupt") — a claim this
project has already measured to overstate the real part. `Op.AdcCmos`'s doc comment, from a core
certified against every decimal vector of `$69`, `$72`, `$E9` and `$F2`, records: *"N and Z come from the
final decimal result, while C and V are computed exactly as NMOS does — so 'correct N/V/Z' overstates it,
V included."* A row already known to overstate `V` for the 65C02 cannot be read as a specification of
`V` for the 65816.

**What task 5 can use.** Clark's Example 2 is a citable, single-point check that any candidate 16-bit
decimal `SBC` must reproduce: `A = $0001`, `m = 0`, `d = 1`, `c = 1`, `SBC #$2003` → `A = $7998`,
`n = 0`, `z = 0`, `c = 0`. Everything else about the algorithm has to come from the vectors, and when it
does it belongs back in this section **labelled as measured, not cited** — the form §11 uses.

#### Measured — the five gaps closed, phase 7c task 5

Added 2026-08-05, after task 5. Everything in this block is **measured against the 600,000
SingleStepTests vectors of the thirty `ADC`/`SBC` opcodes**, not cited. Nothing below has a source.
Where a source says otherwise, that is called out — one of these findings contradicts Clark.

The algorithm, at both widths and for both instructions, is the **nibble-wise** correction: each BCD
digit is corrected in turn and its carry or borrow propagates into the next. Not the `$60`/`$06`
adjustment of the binary result that `SbcCmos` uses. Implemented in `Cpu.Exec.cs`, `Adc816`/`Sbc816`.

| Gap | Measured answer |
| --- | --- |
| 1. The correction algorithm | Nibble-wise, at 8 and 16 bits, for both instructions. Two digits at `m = 1`, four at `m = 0`, same rule repeated |
| 2. How `V` is computed | `ADC`: from the **partially corrected** top digit — the sum before that digit's own `+$06`. `SBC`: from the **binary** difference. Both are exactly what the 8-bit NMOS/CMOS helpers in this codebase already did; `V` needed no new rule at either width |
| 3. Where `Z` and `C` come from | `ADC`: both from the corrected result (`C` = carry out of the corrected top digit) — discriminated by vector `73 n 1007`. `SBC`: `C` from the binary difference, `Z` from the corrected result — discriminated by vector `fd n 3835`. Each is the sole discriminating vector in its own corpus — 37,519 16-bit decimal `ADC` vectors, 37,465 `SBC` |
| 4. Invalid BCD digits (`$A`-`$F`) | Fall out of the nibble-wise rule with no special case. This is the *only* thing that distinguishes nibble-wise from `$60`/`$06`, and it is what made gap 1 measurable at all |
| 5. `ADC`'s decimal `N` at 16 bits | The **corrected** result — the same source as `SBC`'s, though no source licensed assuming so. `SetZN16` on the corrected accumulator; passed first run |

**The one finding that contradicts a source.** Clark's §6 preamble, quoted above, groups emulation mode
and 8-bit native mode and says the 65816 "has the same behavior as 65C02" — hedged with "In general",
and enumerating `n`, `z` and `c`. Task 5 took that as a hypothesis and delegated both 8-bit decimal
paths to this codebase's certified `AdcCmos`/`SbcCmos`. **For `ADC` the hypothesis held; for `SBC` it
did not.**

> **Measured: the 65816's decimal `SBC` corrects nibble-wise (like NMOS), not by the 65C02's
> `$60`/`$06` adjustment — at 8 bits as well as 16.**

Established by all thirty `SBC` test cases failing at once (fifteen opcodes × two modes) and by this
hand-trace of the first of them, **`e9 e 15`**: `A = $60B0` so `A8 = $B0`, `p = $3A` so `d = 1` and
`c = 0`, operand `$4D`, expected final `A8 = $6C`.

- `SbcCmos`'s rule: `binary = $B0 - $4D - 1 = $62`; `$62 >= 0` so no `-$60`; the low-nibble borrow
  (`$0 - $D - 1 < 0`) takes `-$06` → **`$5C`**. Wrong.
- Nibble-wise: `lo = $0 - $D - 1 = -14`, negative, so `-$06` and borrow one from the high digit;
  `hi = $B - $4 - 1 = 6` → `$6C`. **Right.**

Every flag agreed in that vector, and in all thirty: only the accumulator diverged, and only because
the operand's `$D` digit is not valid BCD. Note what this means for gap 1's constraint — the §6
preamble constrains 8-bit behaviour *loosely enough to be wrong*, so its "In general" is doing real
work and it cannot be treated as a specification of anything.

**The second correction, from the same run: `SBC`'s decimal `N` at 16 bits.** Vector **`e9 n 49`** —
`A = $2A35`, `m = 0`, `d = 1`, `c = 0`, operand `$F5C0`. The accumulator was already right
(`$D414`); `N` alone diverged. The binary difference is `$3474`, bit 15 clear; the corrected result is
`$D414`, bit 15 set; the vector wants `n = 1`. So `N` comes from the corrected result — which is
exactly what Clark's Example 2 already said, and what a `SBC` written from `Sbc`'s all-flags-binary
shape gets wrong. `Z` moved with it, per gap 3.

**What was right first time, and is therefore also measured:** every one of the fifteen `ADC` opcodes
passed in both modes on the first run — the 8-bit delegation to `AdcCmos`, the four-digit nibble-wise
16-bit path, `V` from the partially corrected top digit, and `N`/`Z` from the corrected result. Gap 5
is closed in the affirmative, on 150,000 native-mode vectors, having been open on principle.

### 12.2 The `Op` member decision — new members (the plan's §10.2)

**Verdict: `Op.Adc816` and `Op.Sbc816`, new members. `Op.AdcCmos`/`Op.SbcCmos` are not reused.**

The rule is the spec's, pre-committed and not re-litigated here
(`docs/superpowers/specs/2026-08-03-65816-core-design.md`): *"reuse only if the sources state the
behaviour is identical; any divergence, or any silence, gets its own members."*

**The sentence the verdict rests on**, Clark §6.1.1.1, verbatim:

> Note that like the NMOS 6502, but unlike the 65C02, decimal mode (i.e. when the d flag is 1) takes no
> additional cycles.

That is an explicit statement of *difference* from the 65C02 in decimal mode, from the higher-precedence
of the two prose sources, and the datasheet states the same difference independently in Table 7-1's
Timing sub-row `D. Decimal Mode` (§12.5). So this verdict does not rest on silence alone — there is a
stated divergence.

**The one statement of identity that does exist, and why it does not reach far enough.** Clark's §6
preamble (quoted in full in §12.1) says the 65816 "has the same behavior as 65C02" — but only "in
general", only **in emulation mode and for 8-bit results in native mode**, and its worked example
enumerates `n`, `z` and `c` while omitting `v`. Three reasons that does not license reuse under the rule:

1. **It does not cover `m = 0`.** Sixteen-bit decimal arithmetic is exactly what phase 7c adds, and no
   source states it matches anything — the 65C02 has no 16-bit decimal mode to match.
2. **It does not cover `V`.** The one flag `Op.AdcCmos`'s doc comment records as the 65C02's surprise
   (*"C and V are computed exactly as NMOS does"*) is the one flag the sentence leaves out.
3. **An `Op` member is selected once, not per width.** A member that was right for `m = 1` and unstated
   for `m = 0` would still need a 16-bit path behind it, so the reuse it would buy is nil.

The rule admits reuse only on a positive statement that *the* decimal behaviour is identical. What exists
is a hedged, width-limited, `V`-omitting statement of partial identity sitting alongside a flat statement
of divergence on timing. Divergence, partial identity and silence all point the same way.

Two supporting facts, neither of them the reason but both worth recording:

- **The existing helpers could not be reused as written even if the behaviour were identical.** `Adc`,
  `AdcCmos`, `Sbc` and `SbcCmos` in `src/SixtyFiveXX/Cpu.Exec.cs` operate on `A8` and hard-code 8-bit
  constants throughout (`0x0F`, `0x06`, `0x60`, `0x80`, `> 0xFF`). A 16-bit path is a different function
  regardless of which `Op` member selects it.
- **The precedent is this codebase's own.** `Op.AdcCmos`/`Op.SbcCmos` already exist as members separate
  from `Op.Adc`/`Op.Sbc`, and their shared doc comment states why: *"Decimal mode differs from NMOS in
  the accumulator correction as well as the flags, so these are separate members rather than a variant
  test inside `Adc`."* Phase 4's ledger — the 65C02 family, where this separation was introduced — records
  evidence for it rather than assertion: a reviewer deliberately broke `AdcCmos`'s `V` formula in a
  worktree (dropped a `<<4`) and re-ran the Synertek Harte suite, and 9 of 256 opcodes failed, including
  `$69` — direct evidence the vectors discriminate on this exact formula
  (`.superpowers/sdd/progress.md`, "PHASE 4 FINAL REVIEW"). §1 of this document quotes the same doc
  comment as one of the three findings where the documentation was wrong or silent and the vectors were
  right. Adding a third pair for the 65816 is the same decision taken again for the same reason, not a
  new pattern.

### 12.3 Table 5-7's `Direct,Y` row — row 17 (the plan's §10.3)

**Row number: 17**, headed `17. Direct, Y d,y`, opcode list `LDX, STX`, footed
`2 OpCodes, 2 bytes, 4,5 and 6 cycles`. It is on datasheet p. 40, immediately below rows 16a and 16b.
Transcribed in §9's format, same conventions — `(1)` is the 16-bit high half, `(2)` the direct-page
penalty, `DO` the direct-page offset operand byte, `IO` an internal cycle with `VDA = VPA = 0` and no
memory access:

#### Direct,Y — row 17

```
LDX dp,Y / STX dp,Y   2 opcodes, 2 bytes, 4 / 5 / 6 cycles
  1        VDA=1 VPA=1   PBR,PC       OpCode
  2        VDA=0 VPA=1   PBR,PC+1     DO
  2a  (2)  VDA=0 VPA=0   PBR,PC+1     IO        <- DL != $00
  3        VDA=0 VPA=0   PBR,PC+1     IO        <- the indexing cycle, unconditional
  4        VDA=1 VPA=0   0,D+DO+Y     Data Low
  4a  (1)  VDA=1 VPA=0   0,D+DO+Y+1   Data High
```

**It does mirror §9's `Direct,X` block (row 16a) exactly with `Y` substituted — checked cell by cell
against the table rather than assumed.** Same six rows, same `VDA`/`VPA` pairs, same `(2)` on 2a and
`(1)` on 4a, same unconditional internal cycle 3 at `PBR,PC+1`, same bank-0 data access. The only
differences are the index register in the address expression and the header line: row 16a is
`12 OpCodes, 2 bytes, 4,5,and 6 cycles` for `ADC, AND, BIT, CMP, EOR, LDA LDY, ORA, SBC, STA, STY, STZ`,
row 17 is `2 OpCodes, 2 bytes, 4,5 and 6 cycles` for `LDX, STX`. The width flag gating the `(1)` row is
`x` here rather than `m`, since `LDX`/`STX` are x-width — which is Clark's `5-x+w` for `$B6`/`$96`
(§12.5), against `5-m+w` for the row-16a opcodes.

**Row 17 is the only Table 5-7 row phase 7c needs that §9 does not already have.** Cross-check of every
7c opcode against the table's own opcode lists, as printed on pp. 36–42 (the grouped rows are given once
where their lists are identical; punctuation slips in the datasheet's own lists are reproduced):

| Row | Header | Covers, in this phase |
| --- | --- | --- |
| 1a | `ADC, AND, BIT, CMP, CPX, CPY, EOR, LDA, LDX LDY ORA, SBC, STA, STX, STY, STZ` | all `abs` forms |
| 5 | `ADC, AND, CMP, EOR, LDA, ORA, SBC, STA` | `long,X` |
| 4a | same eight | `long` |
| 6a | `ADC, AND, BIT, CMP, EOR, LDA, LDY, ORA, SBC, STA, STZ` | `abs,X`, incl. `BIT`, `LDY`, `STZ` |
| 7 | `ADC, AND, CMP, EOR, LDA, LDX, ORA, SBC, STA` | `abs,Y`, incl. `LDX` |
| 10a | `ADC AND BIT, CMP, CPX, CPY ,EOR, LDA, LDX, LDY, ORA, SBC, STA, STX, STY, STZ` | all `dp` forms |
| 11 / 12 / 13 / 14 / 15 | `ADC, AND, CMP, EOR, LDA, ORA, SBC, STA` each | `(dp,X)`, `(dp)`, `(dp),Y`, `[dp],Y`, `[dp]` |
| 16a | `ADC, AND, BIT, CMP, EOR, LDA LDY, ORA, SBC, STA, STY, STZ` | `dp,X`, incl. `BIT`, `LDY`, `STY`, `STZ` |
| **17** | **`LDX, STX`** | **`dp,Y` — the row added above** |
| 18 | `ADC, AND, BIT, CMP, CPX, CPY, EOR, LDA, LDX, LDY, ORA, REP, SBC, SEP` | all immediates |
| 23 / 24 | `ADC, AND, CMP, EOR, LDA, ORA, SBC, STA` each | `sr,S`, `(sr,S),Y` |

Note what that table also settles by omission. `STZ` appears in rows 1a, 6a, 10a and 16a and nowhere
else; `STX` in rows 1a, 10a and 17 only; `STY` in rows 1a, 10a and 16a only. That is exactly the mode
set Clark gives each of them (§12.5) — four modes for `STZ`, three each for `STX` and `STY` — so neither
source has a mode the other lacks for these opcodes.

### 12.4 The x-width immediates (the plan's §10.4)

**Confirmed: `LDX #`, `LDY #`, `CPX #` and `CPY #` are `3-x` cycles and `3-x` bytes, mirroring
`LDA #`'s `3-m`.** Clark's rows, verbatim, columns `OP LEN CYCLES MODE nvmxdizc e SYNTAX`:

```
A2  3-x  3-x  imm  x.....x. .  LDX #$54       (Clark 6.5)
A0  3-x  3-x  imm  x.....x. .  LDY #$54       (Clark 6.5)
E0  3-x  3-x  imm  x.....xx .  CPX #$54       (Clark 6.1.1.2)
C0  3-x  3-x  imm  x.....xx .  CPY #$54       (Clark 6.1.1.2)
A9  3-m  3-m  imm  m.....m. .  LDA #$54       (Clark 6.5, already in §5)
```

Both the `LEN` and the `CYCLES` column read `3-x`, so the byte count and the cycle count move together,
exactly as `LDA #`'s do with `m`.

**One correction to the task brief:** only `LDX #` and `LDY #` are in Clark §6.5. `CPX #` and `CPY #`
are in **§6.1.1.2** (`CMP CPX CPY`), which is where the compares live. The formulas are as the brief
predicted; the section reference is not.

The datasheet corroborates from the other direction. Table 5-7 row 18 is
`18.Immediate #`, `14 OpCodes, 2 and 3 bytes, 2 and 3 cycles`, with the conditional cycle `2a` carrying
notes `(1)` and `(8)`, and Note 1 reads: *"Add 1 byte (for immediate only) for M=0 or X=0 (i.e. 16-bit
data), add 1 cycle for M=0 or X=0."* Note the "**or X=0**" — the datasheet's own note is explicit that
the immediate widening is driven by `x` as well as by `m`, and row 18's opcode list includes `CPX, CPY,
LDX, LDY` alongside the m-width ones.

### 12.5 A cycle formula for every opcode in phase 7c (the plan's §10.5)

**The answer task 5's shape depends on, first: the 65816's `ADC` and `SBC` formulas carry no
decimal-mode term at all — decimal mode costs no extra cycle on this part, so `ADC`/`SBC` use the
ordinary read tail and task 5 needs no new micro-op, no conditionally-skipped slot, and no 65816
analogue of `MicroOp.BcdExtra`.**

Two independent sources, in the two-of-three agreement §4 asks for. Clark §6.1.1.1, verbatim:

> Note that like the NMOS 6502, but unlike the 65C02, decimal mode (i.e. when the d flag is 1) takes no
> additional cycles.

WDC datasheet Table 7-1 "Caveats" (p. 49), Timing sub-row `D. Decimal Mode`, read across its four
columns — `NMOS 6502` / `W65C02` / `W65C02S` / `W65C816S`:

```
D. Decimal Mode    No add. cycles    Add 1 cycle    Add 1 Cycle    No add. cycles
```

(The Timing cell is one merged cell holding sub-rows A–D per column; the row alignment was verified
against a rendering of p. 49, not only against extracted text, because a mis-aligned read of that cell
would flip this answer.) Corroborating both: every one of the thirty `ADC`/`SBC` formulas below carries
`m`, `w`, `x` and `p` terms and no `d` term — Clark's cycle column has no way to express a decimal
penalty for this part because there is none.

**The six full-mode ALU operations.** `ORA`, `AND`, `EOR`, `ADC`, `CMP` and `SBC` have **identical cycle
and byte formulas in every one of the fifteen modes** — checked opcode by opcode across Clark §6.1.2.1
(`AND EOR ORA`), §6.1.1.1 (`ADC SBC`) and §6.1.1.2 (`CMP`), not assumed from one of them. The formulas
are also identical to `LDA`'s in §5, mode for mode.

| Mode | ORA | AND | EOR | ADC | CMP | SBC | cycles | bytes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| immediate | `$09` | `$29` | `$49` | `$69` | `$C9` | `$E9` | `3-m` | `3-m` |
| direct | `$05` | `$25` | `$45` | `$65` | `$C5` | `$E5` | `4-m+w` | 2 |
| direct,X | `$15` | `$35` | `$55` | `$75` | `$D5` | `$F5` | `5-m+w` | 2 |
| absolute | `$0D` | `$2D` | `$4D` | `$6D` | `$CD` | `$ED` | `5-m` | 3 |
| absolute,X | `$1D` | `$3D` | `$5D` | `$7D` | `$DD` | `$FD` | `6-m-x+x*p` | 3 |
| absolute,Y | `$19` | `$39` | `$59` | `$79` | `$D9` | `$F9` | `6-m-x+x*p` | 3 |
| (direct,X) | `$01` | `$21` | `$41` | `$61` | `$C1` | `$E1` | `7-m+w` | 2 |
| (direct),Y | `$11` | `$31` | `$51` | `$71` | `$D1` | `$F1` | `7-m+w-x+x*p` | 2 |
| (direct) | `$12` | `$32` | `$52` | `$72` | `$D2` | `$F2` | `6-m+w` | 2 |
| \[direct\] | `$07` | `$27` | `$47` | `$67` | `$C7` | `$E7` | `7-m+w` | 2 |
| \[direct\],Y | `$17` | `$37` | `$57` | `$77` | `$D7` | `$F7` | `7-m+w` | 2 |
| long | `$0F` | `$2F` | `$4F` | `$6F` | `$CF` | `$EF` | `6-m` | 4 |
| long,X | `$1F` | `$3F` | `$5F` | `$7F` | `$DF` | `$FF` | `6-m` | 4 |
| stack,S | `$03` | `$23` | `$43` | `$63` | `$C3` | `$E3` | `5-m` | 2 |
| (stack,S),Y | `$13` | `$33` | `$53` | `$73` | `$D3` | `$F3` | `8-m` | 2 |

**`BIT` and the remaining loads, stores and compares.** Clark §6.1.2.2 (`BIT`), §6.5 (`LDX LDY STX STY
STZ`) and §6.1.1.2 (`CPX CPY`):

| Op | Mode | Opcode | cycles | bytes | Clark § |
| --- | --- | --- | --- | --- | --- |
| `BIT` | immediate | `$89` | `3-m` | `3-m` | 6.1.2.2 |
| `BIT` | direct | `$24` | `4-m+w` | 2 | 6.1.2.2 |
| `BIT` | direct,X | `$34` | `5-m+w` | 2 | 6.1.2.2 |
| `BIT` | absolute | `$2C` | `5-m` | 3 | 6.1.2.2 |
| `BIT` | absolute,X | `$3C` | `6-m-x+x*p` | 3 | 6.1.2.2 |
| `LDX` | immediate | `$A2` | `3-x` | `3-x` | 6.5 |
| `LDX` | direct | `$A6` | `4-x+w` | 2 | 6.5 |
| `LDX` | direct,Y | `$B6` | `5-x+w` | 2 | 6.5 |
| `LDX` | absolute | `$AE` | `5-x` | 3 | 6.5 |
| `LDX` | absolute,Y | `$BE` | `6-2*x+x*p` | 3 | 6.5 |
| `LDY` | immediate | `$A0` | `3-x` | `3-x` | 6.5 |
| `LDY` | direct | `$A4` | `4-x+w` | 2 | 6.5 |
| `LDY` | direct,X | `$B4` | `5-x+w` | 2 | 6.5 |
| `LDY` | absolute | `$AC` | `5-x` | 3 | 6.5 |
| `LDY` | absolute,X | `$BC` | `6-2*x+x*p` | 3 | 6.5 |
| `STX` | direct | `$86` | `4-x+w` | 2 | 6.5 |
| `STX` | direct,Y | `$96` | `5-x+w` | 2 | 6.5 |
| `STX` | absolute | `$8E` | `5-x` | 3 | 6.5 |
| `STY` | direct | `$84` | `4-x+w` | 2 | 6.5 |
| `STY` | direct,X | `$94` | `5-x+w` | 2 | 6.5 |
| `STY` | absolute | `$8C` | `5-x` | 3 | 6.5 |
| `STZ` | direct | `$64` | `4-m+w` | 2 | 6.5 |
| `STZ` | direct,X | `$74` | `5-m+w` | 2 | 6.5 |
| `STZ` | absolute | `$9C` | `5-m` | 3 | 6.5 |
| `STZ` | absolute,X | `$9E` | `6-m` | 3 | 6.5 |
| `CPX` | immediate | `$E0` | `3-x` | `3-x` | 6.1.1.2 |
| `CPX` | direct | `$E4` | `4-x+w` | 2 | 6.1.1.2 |
| `CPX` | absolute | `$EC` | `5-x` | 3 | 6.1.1.2 |
| `CPY` | immediate | `$C0` | `3-x` | `3-x` | 6.1.1.2 |
| `CPY` | direct | `$C4` | `4-x+w` | 2 | 6.1.1.2 |
| `CPY` | absolute | `$CC` | `5-x` | 3 | 6.1.1.2 |

Ninety opcodes in the first table plus thirty-one in the second is **121**, the phase's full count.

Six things fall out of those tables that §5's `LDA`/`STA` slice could not show, each a place a
transcription from memory would plausibly go wrong:

- **`LDX abs,Y` and `LDY abs,X` are `6-2*x+x*p`, not `6-m-x+x*p`.** This is the one formula *shape* §5
  does not contain. The reason is structural rather than special-cased: in `6-m-x+x*p` the `-m` is the
  data width and the `-x` the index width, and for `LDX`/`LDY` both are `x`, so the two terms collapse
  into `-2*x`. It still shares Table 5-7 rows 7 and 6a with the m-width opcodes (§12.3) — the bus
  sequence is the same, only the flag gating the `(1)` row differs.
- **`STZ abs,X` is `6-m` with no `p` term** — an indexed write, so it always pays, page cross or not.
  Same rule §3.2 established for `STA abs,X`, and the only indexed write among the new store opcodes.
- **`BIT abs,X` is `6-m-x+x*p`**, an ordinary m-width indexed read; `BIT` gains a `dp,X` and an `abs,X`
  form on this part that the NMOS `BIT` does not have.
- **`BIT #` is `3-m` and affects only `z`.** Clark §6.1.2.2: *"Immediate addressing only affects the z
  flag (with the result of the bitwise And), but does not affect the n and v flags. All other addressing
  modes of BIT affect the n, v, and z flags. This is the only instruction in the 6502 family where the
  flags affected depends on the addressing mode."* Clark's flag column shows this directly: `$89` is
  `......m.` against `mm....m.` for the other four.
- **`BIT`'s `n` and `v` come from the data, at the 16-bit positions when `m = 0`.** Clark §6.1.2.2:
  *"The n flag reflects the high bit of the data (note: just the data, not the bitwise And of the
  accumulator and the data). The v flag reflects the second highest bit of the data (i.e. bit 14 of the
  data when the m flag is 0, and bit 6 of the data when the m flag is 1 …)."*
- **`CMP`/`CPX`/`CPY` are always binary and never touch `v`.** Clark §6.1.1.2, on `CMP` versus `SBC`:
  *"A. It is always a binary subtraction (i.e. SBC as though the d flag was 0) B. It does not include
  the carry in the formula (i.e. register - data; in other words, SBC as though the carry was set before
  the SBC) C. The v flag is not affected."* So §12.1's open questions do not reach the compares — they
  are settled, and they are settled by a source rather than by a vector.

### 12.6 The gaps this section records, listed in one place

Everything above either carries a named source or appears here. Task 5 owned the first block; nothing
else in phase 7c depended on the rest.

**All five are now closed, by measurement.** They are left standing below, with what the sources said
when they were opened, because the difference between "no source says this" and "the vectors say this"
is the thing this document exists to keep visible. The answers are in §12.1's **Measured** block, which
names the vectors; the one-line versions are in the right-hand column.

| # | Gap | Status when opened | Resolved |
| --- | --- | --- | --- |
| 1 | The decimal correction algorithm for `ADC`/`SBC`, at 8 **or** 16 bits | **The sources are silent.** Clark gives none at any width; the datasheet gives none | **Measured**, task 5: nibble-wise at both widths, both instructions. *Not* the 65C02's `$60`/`$06` form — see the contradiction below |
| 2 | How `V` is computed in decimal mode | **The sources are silent.** Clark: "overwritten … can be considered invalid". Table 7-1's "N,V and Z flags valid" is the same wording already measured to overstate for the 65C02 | **Measured**, task 5: `ADC` from the partially corrected top digit, `SBC` from the binary difference — the 8-bit helpers' existing rules, unchanged at 16 bits. No discriminating-vector figure is available for this gap: it closed by matching the existing 8-bit rule rather than by a single vector that distinguishes one candidate answer from another |
| 3 | Whether `Z` and `C` come from the corrected result or the binary intermediate | **The sources are silent.** Clark's wording implies the corrected result but Example 2 does not discriminate | **Measured**, task 5: `ADC` both from the corrected result; `SBC` `C` from the binary difference and `Z` from the corrected result |
| 4 | Decimal behaviour on invalid BCD digits (`$A`–`$F` nibbles) | **The sources are silent.** Not mentioned anywhere surveyed | **Measured**, task 5: falls out of the nibble-wise rule, no special case — and is the only thing that discriminated gap 1 |
| 5 | Whether `ADC`'s decimal `N` at 16 bits comes from the corrected result or the binary intermediate | **The sources are silent for `ADC`.** Example 2 pins `N` for `SBC` only; Clark gives no decimal `ADC` example at any width. This codebase's own NMOS `Adc`/`Sbc` already diverge on the general question (`src/SixtyFiveXX/Cpu.Exec.cs:380` vs `:444`), so the `SBC` answer must not be assumed for `ADC` | **Measured**, task 5: the corrected result, the same as `SBC`'s. Passed first run on 150,000 native-mode vectors — 25,426 of the 37,519 16-bit decimal `ADC` vectors discriminate `N` |

**One source turned out to be wrong, and it is the one that constrained gaps 1 and 3 at `m = 1`.**
Clark's §6 preamble ("the same behavior as 65C02", 8-bit results) was treated as a hypothesis to
measure rather than a citation to implement against — correctly, as it happens. It holds for `ADC` and
fails for `SBC`: the 65816's decimal `SBC` corrects nibble-wise at 8 bits too, where the 65C02 adjusts
by `$60`/`$06`. §12.1's Measured block has the hand-trace. The precedence rule in §4 did its job, and
"In general" was load-bearing.

What is **not** a gap, so nobody re-opens it: `SBC`'s decimal `N` source at 16 bits (Clark's Example 2,
§12.1, and now the vectors agreeing); whether emulation mode and 8-bit native mode differ in decimal
behaviour (Clark's §6 preamble groups them — §12.1); the decimal cycle count (§12.5, two sources
agreeing, plus the §6 preamble's worked example a third time — and confirmed by 600,000 vectors passing
with no micro-op change); the `Op` member verdict (§12.2, decided by a pre-committed rule on a stated
divergence, and vindicated: `SBC`'s divergence is real); row 17 (§12.3, transcribed); the x-width
immediates (§12.4, transcribed); and all 121 cycle formulas (§12.5, transcribed).

### 12.7 The direct-page indirect pointer's high byte wraps on `D == $0000`, not on `DL == $00` — measured

Added 2026-08-05, during phase 7c task 5.

Task 5's vectors surfaced one non-arithmetic failure, and it is worth its own subsection because it
falsifies a "zero vector coverage" note that a phase 7b code review had left in the source.

`Cpu.DirectPagePointerHighAddress` decides where an "old" direct-page indirect mode — `(dp)`, `(dp,X)`,
`(dp),Y` — reads the *high byte of its pointer* when the pointer's own low byte sits at `$xxFF`. Clark's
appendix is the source, verbatim (already quoted in that method's remarks):

> if the D register is $0000 (and the e flag is 1), then LDA ($FF) uses a pointer whose low byte is at
> $0000FF and whose high byte is at $000000 (like the 65C02)

Phase 7b implemented that as `E && (D & $FF) == $00` — generalising Clark's `D == $0000` to "`DL` is
zero", by analogy with `MicroOp.DirectPageIndexX`, which really does use the `DL` condition. At the time
no vector could tell the two apart, and the method's remarks said so.

**Measured, from vector `e1 e 8669`** (`SBC (dp,X)`, emulation mode): `D = $F400` — so `DL == $00` but
`D != $0000` — direct-page offset `$B0`, `X = $4F`, giving a pointer base of `$F4FF`. The hardware reads
the pointer's high byte at **`$F500`**. It does not wrap. Reading it at `$F400` yields a pointer of
`$002F` instead of `$3E2F`, an operand of `$00` instead of `$B3`, and a final `A` of `$3108` against the
vector's `$3155`.

So the condition is Clark's literal one, `D == $0000`, and the generalisation was wrong. Two things keep
this narrow:

- **The index add's `DL == $00` condition is not affected and is separately confirmed.** A sweep of the
  "old" `.e` files wherever `MicroOp.DirectPageIndexX`'s wrap applies found 320 emulation vectors
  discriminated with `D != $0000`, and every one of them wraps. Sixteen files actually contributed a
  discriminating vector; eight of those are plain `dp,X`, not indirect — the 24 purely indirect `.e`
  files alone account for only 162 of the 320. The two conditions genuinely differ; only the pointer's
  `+1` read follows `D == $0000`.
- **`e1 e 8669` is the only vector in the corpus that discriminates the pointer read at all** — 1 hit
  across those same 24 files, and the `.n` files cannot contribute because the whole rule is gated on
  `E`. With `D == $0000` specifically there are zero vectors, so the wrapping half of the rule still
  rests on Clark alone, exactly as before. What changed is that the non-wrapping half is now measured.

The same shape as §11: a rule the earlier sections do not state, measured from the vectors, and agreeing
with the one source that speaks to it once that source is read literally rather than generalised.

---

## 13. Phase 7c′'s five unsettled questions — read-modify-write, the implied forms, and Note 17

Added 2026-08-05, before any phase 7c′ code was written. Same practice as §9 and §12: transcribe first,
implement second. The phase 7c′ plan
(`docs/superpowers/plans/2026-08-05-phase7cprime-rmw-implied.md`) cites this material as **§13.1**–**§13.5**,
which is the numbering used here — unlike §12, there is no offset to map.

**Sources, fetched for this section rather than recalled.** Clark, "65C816 Opcodes", from the GitHub mirror
`https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
(**137,171 bytes**, footer "Last Updated September 28, 2015") — the mirror because 6502.org itself 404s
non-browser agents (§2.2). WDC W65C816S datasheet from
`https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf` (**1,532,025 bytes**, header
"March 13, 2024"), Table 5-7 on pp. 36–42 and its Notes on p. 43. Both byte counts are identical to the
ones §12 recorded a day earlier, so this is demonstrably the same pair of files, not a re-issue.

Notation is §5's and §9's: `m` and `x` are the flag values (0 or 1), `w` is 1 when `DL != $00` and 0
otherwise, `p` is 1 on a page cross; `IO` is an internal cycle with `VDA = VPA = 0` and no memory access;
`(1)` is the high half of a 16-bit access, `(2)` the direct-page penalty. Two conditional-cycle notes are
new to this section, and both are transcribed verbatim in §13.1: **`(3)`** and **`(17)`**.

**One extension to §9's column set, and it is load-bearing.** §9 dropped the `VPB` and `MLB` columns because
"VPB and MLB are `1` (inactive) throughout every one of these modes". **That is not true of the
read-modify-write rows.** Table 5-7 prints `MLB = 0` — asserted, the pin is active-low — on every cycle of an
RMW instruction that touches the target byte. `MLB` is the **eighth character of the pin string the vectors
assert** (`BuildPinString` in `tests/SixtyFiveXX.Conformance/Harte816Tests.cs`, slot 7, the `l`), so it is not
cosmetic. Every block below therefore carries an `MLB` column, and an `RWB` column as well, since the
direction of one cycle is the whole subject of §13.1. `VPB` is still `1` throughout and is still dropped.

### 13.1 The RMW per-cycle sequences, and what the middle cycle actually is

> **The native-mode middle cycle is an internal cycle, not a read.** Table 5-7 prints `VDA = 0`, `VPA = 0`,
> data bus `IO` and `RWB = 1` on that cycle in all four read-modify-write rows. It is the same `IO` §9
> defines — no memory access, value recorded `null` — and it is **not** the 65C02's dummy read, which would
> be a genuine access with `VDA` asserted. `RmwModifyRead816` must call `InternalCycle`, not `ReadBus`.

The phase 7c′ brief anticipated the opposite, reasoning that if emulation mode "reverts to the NMOS
double-write" then the native case ought to be the CMOS-style dummy read. That phrase is the brief's, not
the datasheet's — Note 17 says nothing of the kind, as its verbatim text below shows — and the table
contradicts the inference. The 65816's native middle cycle is neither of the two 8-bit shapes this codebase
already implements: it accesses no memory at all.

**But the cycle is not pin-identical to any internal cycle this codebase has emitted before**, and that is
the finding that goes with it: `MLB = 0` on that cycle as well, so its pin string is `MLB` asserted with
neither `VDA` nor `VPA`. No micro-op in the project has ever driven that combination — `MicroOps.PinsFor`
currently emits `BusPins.Vda | BusPins.Mlb` for the four 8-bit RMW micro-ops and `BusPins.None` for every
internal cycle. `RmwModifyRead816`'s entry must be `BusPins.Mlb` alone, and it does **not** belong on
`PinsFor`'s documented list of legitimately-`None` micro-ops.

**Note 17, verbatim** (p. 43):

> 17. In the emulation mode, during a R-M-W instruction the RWB is low during both write and modify cycles.

**Note 3, verbatim** (p. 43), which gates the same cycle and is transcribed because `(3)` appears alongside
`(17)` on every one of these rows — the run-together "DBRregisters" is the datasheet's own:

> 3. Special case for aborting instruction. This is the last cycle which may be aborted or the Status, PBR or
>    DBRregisters will be updated.

Note 3 is about `ABORTB`, which §8 already defers out of phase 7 entirely. It is recorded here only so that
a later reader who sees `(3)(17)` on the row knows both halves have been read and only one of them bites.

Note what Note 17 does and does not say. It speaks of **`RWB` alone**. It does not say `VDA` rises in
emulation mode, it does not give an address, and it does not say the write is of the unmodified value —
that last is a property of the NMOS sequence being emulated, not something this datasheet states. See the
gaps in §13.6.

Transcribed in §9's format, extended with `MLB` and `RWB`. All four rows were read from the datasheet's
extracted text **and then cross-checked cell by cell against page renderings of pp. 36, 37, 39 and 40** —
the same precaution §12.5 took with Table 7-1's merged cell, and it earned its place here too (see the note
on row 16b below).

#### Direct (R-M-W) — row 10b, p. 39

```
ASL dp / DEC dp / INC dp / LSR dp / ROL dp / ROR dp / TRB dp / TSB dp
                                                  8 opcodes, 2 bytes, 5 / 6 / 7 / 8 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=1 MLB=1   PBR,PC+1     DO          RWB=1
  2a  (2)        VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1   <- DL != $00
  3              VDA=1 VPA=0 MLB=0   0,D+DO       Data Low    RWB=1
  3a  (1)        VDA=1 VPA=0 MLB=0   0,D+DO+1     Data High   RWB=1
  4   (3),(17)   VDA=0 VPA=0 MLB=0   0,D+DO+1     IO          RWB=1   <- the middle cycle; Note 17 in emulation
  5a  (1)        VDA=1 VPA=0 MLB=0   0,D+DO+1     Data High   RWB=0
  5              VDA=1 VPA=0 MLB=0   0,D+DO       Data Low    RWB=0
```

#### Direct,X (R-M-W) — row 16b, p. 40

```
ASL dp,X / DEC dp,X / INC dp,X / LSR dp,X / ROL dp,X / ROR dp,X
                                                  6 opcodes, 2 bytes, 6 / 7 / 8 / 9 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=1 MLB=1   PBR,PC+1     DO          RWB=1
  2a  (2)        VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1   <- DL != $00
  3              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1   <- the indexing cycle, unconditional
  4              VDA=1 VPA=0 MLB=0   0,D+DO+X     Data Low    RWB=1
  4a  (1)        VDA=1 VPA=0 MLB=0   0,D+DO+X+1   Data High   RWB=1
  5   (3),(17)   VDA=0 VPA=0 MLB=0   0,D+DO+X+1   IO          RWB=1   <- the middle cycle
  6a  (1)        VDA=1 VPA=0 MLB=0   0,D+DO+X+1   Data High   RWB=0
  6              VDA=1 VPA=0 MLB=0   0,D+DO+X     Data Low    RWB=0
```

**One typographic caveat on this row, resolved rather than guessed.** In the printed table the note cell
`(3),(17)` wraps onto two lines, which pushes the following `(1)` marker down one line so that it appears
beside cycle `6` rather than beside `6a`. Rows 1d, 10b and 6b carry the same note cell without wrapping and
put that `(1)` on the `a` row every time; and the alternative reading — a conditional `Data Low` write and an
unconditional `Data High` write — is incoherent, since `Data High` exists only when `m = 0`. The marker
belongs on `6a`. Recorded because the extracted text and the rendered page show the same offset, so a reader
checking either one alone would see it.

#### Absolute (R-M-W) — row 1d, p. 36

```
ASL abs / DEC abs / INC abs / LSR abs / ROL abs / ROR abs / TRB abs / TSB abs
                                                  8 opcodes, 3 bytes, 6 / 8 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=1 MLB=1   PBR,PC+1     AAL         RWB=1
  3              VDA=0 VPA=1 MLB=1   PBR,PC+2     AAH         RWB=1
  4              VDA=1 VPA=0 MLB=0   DBR,AA       Data Low    RWB=1
  4a  (1)        VDA=1 VPA=0 MLB=0   DBR,AA+1     Data High   RWB=1
  5   (3)(17)    VDA=0 VPA=0 MLB=0   DBR,AA+1     IO          RWB=1   <- the middle cycle
  6a  (1)        VDA=1 VPA=0 MLB=0   DBR,AA+1     Data High   RWB=0
  6              VDA=1 VPA=0 MLB=0   DBR,AA       Data Low    RWB=0
```

The row's own header reads `6 OpCodes, 3 bytes, 6 & 8 cycles` while listing **eight** mnemonics
(`ASL, DEC, INC, LSR, ROL, ROR, TRB, TSB`). Row 10b lists the same eight and says `8 OpCodes`; Clark gives
`TRB $9876` as `$1C` and `TSB $9876` as `$0C` (§6.1.2.3), so `abs` RMW really is eight opcodes and **`6
OpCodes` is a miscount in the datasheet**, of the same kind as the punctuation slips §12.3 reproduced from
these lists. The cycle figures are unaffected.

#### Absolute,X (R-M-W) — row 6b, p. 37

```
ASL abs,X / DEC abs,X / INC abs,X / LSR abs,X / ROL abs,X / ROR abs,X
                                                  6 opcodes, 3 bytes, 7 / 9 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC             OpCode      RWB=1
  2              VDA=0 VPA=1 MLB=1   PBR,PC+1           AAL         RWB=1
  3              VDA=0 VPA=1 MLB=1   PBR,PC+2           AAH         RWB=1
  4              VDA=0 VPA=0 MLB=1   DBR,AAH,AAL+XL     IO          RWB=1   <- unconditional, NOT note (4)
  5              VDA=1 VPA=0 MLB=0   DBR,AA+X           Data Low    RWB=1
  5a  (1)        VDA=1 VPA=0 MLB=0   DBR,AA+X+1         Data High   RWB=1
  6   (3)(17)    VDA=0 VPA=0 MLB=0   DBR,AA+X+1         IO          RWB=1   <- the middle cycle
  7a  (1)        VDA=1 VPA=0 MLB=0   DBR,AA+X+1         Data High   RWB=0
  7              VDA=1 VPA=0 MLB=0   DBR,AA+X           Data Low    RWB=0
```

**Cycle 4 carries no note at all.** Compare row 6a in §9, whose analogous cycle `3a` carries `(4)` and is
skipped when `x = 1` and there is no page cross. The RMW form's indexing cycle is **unconditional**, which
is why Clark's `9-2*m` (§13.3) has no `p` term and no `x` term — this is the same shape as an indexed
*write*, and it is stated by the table and by Clark independently. Its address is still the mis-indexed
`DBR,AAH,AAL+XL`, high byte un-carried, exactly as §9's row 6a.

#### What is common to all four

- The addressing-mode cycles (opcode fetch, operand fetches, direct-page penalty, indexing cycle) keep
  `MLB = 1`. `MLB` goes to `0` on the first cycle that touches the target byte and stays there to the end.
  `BusPins.Mlb`'s existing doc comment in `src/SixtyFiveXX/MicroOp.cs` — *"set on the cycles of a
  read-modify-write instruction that actually touch the target byte … Not set on the addressing-mode cycles
  that merely compute the target address, even when they occur inside an RMW instruction"* — was written for
  the 8-bit cores and states the rule the 65816's table follows. Row 6b's cycle 4, the indexing `IO` at the
  mis-indexed address, is `MLB = 1`, which is that comment's second sentence confirmed on this part. The
  comment's enumeration (*"the read, the modify, and the final write"*) is the 8-bit shape and needs
  widening to five cycles for `m = 0`; the rule behind it does not change.
- The middle cycle drives the **`+1` address** — `AA+1`, `D+DO+1`, `AA+X+1`, `D+DO+X+1` — in every row.
  This is *not* what the plan's draft `RmwModifyRead816` does (`InternalCycle(_addr)`, the low address).
  See §13.6, gap 1, for the part the table leaves open.
- `RWB` is `1` on the two reads and the middle cycle, `0` on the two writes. There is no `1/0` cell anywhere
  in an RMW row, unlike the load/store rows, because the direction of every cycle is fixed by the row.

#### Measured, not cited: the middle cycle's pins are the same in emulation mode

Added 2026-08-05 by phase 7c′ task 2, from the vectors. Recorded in §12's form for a measured result: this
is **not** in Clark and **not** in the datasheet, and the two open questions it closes were recorded as
gaps *before* the measurement was taken, not rationalised afterwards.

> **The emulation-mode middle cycle asserts `MLB` with neither `VDA` nor `VPA` — the same pins as the
> native one, differing only in `RWB`.** Table 5-7's unqualified `VDA = 0, VPA = 0` on that row is
> **literal, and holds in emulation mode too**, where the cycle is a genuine write to memory. Note 17
> governs `RWB` and only `RWB`, exactly as its wording says; the datasheet's silence about `VDA` was not an
> omission to be filled in by inference.

**The vector that established it.** `06 e`, vector `1` (`ASL dp`, emulation), **cycle 4** — the middle
cycle. Expected pin string `---wemxl`, against `d--wemxl` from a core classifying that cycle
`VDA | MLB`. Only the first character differs: the address (`$0021CB`), the value (`$74`), the direction
(`w`, slot 3) and `MLB` (`l`, slot 7) all matched. The eight-character string is
`BuildPinString`'s, in `tests/SixtyFiveXX.Conformance/Harte816Tests.cs`.

**All four rows agree, and so do all sixteen opcodes.** `ASL`, `LSR`, `ROL` and `ROR` in `dp` (row 10b),
`dp,X` (16b), `abs` (1d) and `abs,X` (6b) each failed identically and only on that character, at cycle 4
for the unindexed modes and cycle 5 for the indexed ones — sixteen of sixteen, 160,000 emulation vectors.
The 160,000 native vectors passed unchanged, which is the same statement for the native form. So the
generalisation, and the useful one for phase 7d's remaining read-modify-writes (`DEC`, `INC`, `TRB`,
`TSB`):

> **All three middle-cycle forms carry `MLB` alone.** Native (internal, `RWB = 1`) and emulation (write,
> `RWB = 0`) are pin-identical. A read-modify-write's middle cycle never asserts an address-valid pin, in
> either mode, at either width.

**The second gap the same run closed.** The middle cycle drives the **low** address (`AA`, `D+DO`,
`AA+X`, `D+DO+X`) when `m = 1` — *not* the `+1` printed unconditionally in all four rows. Established
twice over and independently: every emulation vector matched on the address field above, and all 160,000
native vectors passed with the low address driven at `m = 1`. The `+1` in the printed rows belongs to the
16-bit form only, where it is confirmed. See §13.6, gaps 1 and 2, both now closed.

**And one inference corroborated.** §13.6 gap 3 — that the emulation middle cycle writes the *unmodified*
value — was an inference from the certified NMOS part rather than a 65816 source. The value field matched
on all sixteen opcodes, so it is right; it remains an inference that the vectors confirmed, not a cited
fact.

### 13.2 The 16-bit write order — the writes reverse

**The reads go low-then-high and the writes go high-then-low.** This is stated by the table rows directly,
not inferred: in row 1d the read pair is cycle `4` at `DBR,AA` (`Data Low`) then `4a` at `DBR,AA+1`
(`Data High`), and the write pair is cycle `6a` at `DBR,AA+1` (`Data High`, `RWB = 0`) then `6` at `DBR,AA`
(`Data Low`, `RWB = 0`) — in that printed order, high before low. All four rows agree:

| Row | Read low | Read high | Write high | Write low |
| --- | --- | --- | --- | --- |
| 10b `dp` | 3 @ `0,D+DO` | 3a @ `0,D+DO+1` | 5a @ `0,D+DO+1` | 5 @ `0,D+DO` |
| 16b `dp,X` | 4 @ `0,D+DO+X` | 4a @ `0,D+DO+X+1` | 6a @ `0,D+DO+X+1` | 6 @ `0,D+DO+X` |
| 1d `abs` | 4 @ `DBR,AA` | 4a @ `DBR,AA+1` | 6a @ `DBR,AA+1` | 6 @ `DBR,AA` |
| 6b `abs,X` | 5 @ `DBR,AA+X` | 5a @ `DBR,AA+X+1` | 7a @ `DBR,AA+X+1` | 7 @ `DBR,AA+X` |

Two things make this a *stated* fact rather than a reading of row order. First, the cycle counts force it:
`abs` RMW is `6 & 8 cycles`, so at `m = 0` the sequence `4, 4a, 5, 6a, 6` is eight cycles with `6a` seventh
and `6` eighth. There is no ordering left to choose.

Second, the inversion — an `a`-suffixed conditional cycle printed *above* its base cycle, which is the
reverse of the `2a`/`3a`/`4a` convention everywhere else in Table 5-7 — is the datasheet's deliberate way of
saying the conditional half happens first, and **one other row proves it means that**. Row 22c, the 16-bit
pushes, p. 41:

```
22c.  PHA, PHB PHP, PHD, PHK, PHX, PHY            7 opcodes, 1 byte, 3 and 4 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1
  3a             VDA=1 VPA=0 MLB=1   0,S          REG High    RWB=0
  3              VDA=1 VPA=0 MLB=1   0,S-1        REG Low     RWB=0

22b.  PLA, PLB, PLD, PLP, PLX, PLY                6 opcodes, 1 byte, 4 and 5 cycles
  4              VDA=1 VPA=0 MLB=1   0,S+1        REG Low     RWB=1
  4a  (1)        VDA=1 VPA=0 MLB=1   0,S+2        REG High    RWB=1
```

The push prints `3a` above `3`, high byte before low; the pull prints `4a` below `4`, low byte before high.
Neither order is a matter of interpretation, because the stack descends: a push must write `0,S` before
`0,S-1`, and a pull must read `0,S+1` before `0,S+2`. So on the one pair of rows where the true order is
independently fixed, the convention "printed above means happens first" holds in **both** directions. That
is what licenses reading rows 1d/6b/10b/16b the same way.

(Rows 19c and 19d, `STP` and `WAI`, also print `1c`/`1b`/`1a` above cycle `1`, but those are successive
states of a stop-and-restart sequence rather than width-conditional halves. Named here so the paragraph
above is not mistaken for a claim about every suffixed label in the table.)

**Table 5-7 does show the 16-bit RMW explicitly**, so the brief's fallback ("if Table 5-7 does not show a
16-bit RMW row explicitly, say so") does not apply. The `(1)`-gated rows `4a`/`6a` *are* the 16-bit RMW, and
the header's second cycle figure (`8` for `abs`, `9` for `abs,X`, `7`/`8` for `dp`, `8`/`9` for `dp,X`) is
the `m = 0` case.

The six-slot shape in the phase 7c′ plan emits `RmwWriteHigh816` before `RmwWrite816`, which is this order.

### 13.3 A cycle formula for every one of the 59 opcodes

> **`ASL abs` is `8-2*m`. Yes, it is `8-2m`, and the six-slot shape survives unchanged.** Clark §6.1.3, row
> `0E 3 8-2*m abs`. Sixteen-bit costs exactly `+2` cycles over eight-bit — one extra read and one extra
> write — in every one of the four memory RMW modes, with no third conditional cycle anywhere. Task 2 does
> not need redesigning.

Two independent sources agree on all four, in the two-of-three form §4 asks for. Clark's `CYCLES` column
against Table 5-7's own per-row cycle-count headers:

| Mode | Clark | Table 5-7 row header | `m=1,w=0` | `m=1,w=1` | `m=0,w=0` | `m=0,w=1` |
| --- | --- | --- | --- | --- | --- | --- |
| `dp` | `7-2*m+w` | 10b: `5,6,7 and 8 cycles` | 5 | 6 | 7 | 8 |
| `dp,X` | `8-2*m+w` | 16b: `6,7,8 and 9 cycles` | 6 | 7 | 8 | 9 |
| `abs` | `8-2*m` | 1d: `6 & 8 cycles` | 6 | — | 8 | — |
| `abs,X` | `9-2*m` | 6b: `7 and 9 cycles` | 7 | — | 9 | — |

Every enumerated header value is reproduced by the formula and no header value is left over, in all four
rows. **Across all 59 opcodes the only symbols that appear in a cycle formula are `m` and `w`.** No `p`
term anywhere — `abs,X` RMW is flat `9-2*m`, per §13.1's unconditional cycle 4 — and no `x` term anywhere
either, including the four implied register forms, whose cycle count is a flat `2` even though their *flag*
column is `x`-tagged. `w` appears only on the two direct-page modes, as §5 established for the phase 7b
slice.

**The 28 memory read-modify-writes.** Clark §6.1.3 (`ASL LSR ROL ROR`), §6.1.1.3 (`DEC DEX DEY INC INX INY`)
and §6.1.2.3 (`TRB TSB`), transcribed opcode by opcode rather than assumed from one of them:

| Op | `dp` | `abs` | `dp,X` | `abs,X` | Clark § |
| --- | --- | --- | --- | --- | --- |
| `ASL` | `$06` | `$0E` | `$16` | `$1E` | 6.1.3 |
| `LSR` | `$46` | `$4E` | `$56` | `$5E` | 6.1.3 |
| `ROL` | `$26` | `$2E` | `$36` | `$3E` | 6.1.3 |
| `ROR` | `$66` | `$6E` | `$76` | `$7E` | 6.1.3 |
| `DEC` | `$C6` | `$CE` | `$D6` | `$DE` | 6.1.1.3 |
| `INC` | `$E6` | `$EE` | `$F6` | `$FE` | 6.1.1.3 |
| `TRB` | `$14` | `$1C` | — | — | 6.1.2.3 |
| `TSB` | `$04` | `$0C` | — | — | 6.1.2.3 |
| **cycles** | `7-2*m+w` | `8-2*m` | `8-2*m+w` | `9-2*m` | |
| **bytes** | 2 | 3 | 2 | 3 | |

`TRB` and `TSB` exist in `dp` and `abs` only — 24 + 4 = **28**. Clark gives them no indexed form and Table
5-7 lists them in rows 1d and 10b and nowhere else, so neither source has a mode the other lacks.

**The six accumulator forms.** Clark §6.1.3 and §6.1.1.3; Table 5-7 row 8, `8. Accumulator A`,
`ASL, DEC, INC, LSR, ROL, ROR`, `6 OpCodes, 1 byte, 2 cycles`:

| Op | Opcode | cycles | bytes | Clark flag column | Clark § |
| --- | --- | --- | --- | --- | --- |
| `ASL A` | `$0A` | 2 | 1 | `m.....mm` | 6.1.3 |
| `LSR A` | `$4A` | 2 | 1 | `0.....m*` | 6.1.3 |
| `ROL A` | `$2A` | 2 | 1 | `m.....mm` | 6.1.3 |
| `ROR A` | `$6A` | 2 | 1 | `m.....m*` | 6.1.3 |
| `INC A` | `$1A` | 2 | 1 | `m.....m.` | 6.1.1.3 |
| `DEC A` | `$3A` | 2 | 1 | `m.....m.` | 6.1.1.3 |

**Flat 2 cycles at both widths** — the cycle column carries no `m` term and row 8 gives a single figure. Its
one non-fetch cycle is `VDA=0 VPA=0 MLB=1`, `PBR,PC+1`, `IO`, `RWB=1` — pin-identical to §9's implied block,
row 19a, and carrying no `MLB` assertion despite being a read-modify-write instruction, because no memory
cycle is involved. Read Clark's flag columns with §6's key: `0` = cleared, `*` = affected, `m` =
affected by the `(16-8*m)`-bit result. So `LSR`'s `n` is **cleared** at both widths, and `ASL`/`ROL` tag `c`
with `m` because which bit is shifted out depends on the width, while `LSR`/`ROR` tag it `*` because bit 0
is bit 0 either way.

> **A source error to implement around, found in Clark and not previously recorded.** §6.1.3's prose reads:
> *"ASL shifts left; a zero is shifted into the low bit (bit 0); the high bit (bit 15 when the m flag is one,
> bit 7 when the m flag is 0) is shifted into the c flag."* **The m-flag polarity is inverted.** `m = 1` is
> the 8-bit case, so the high bit is bit 7, not bit 15. Clark has it the right way round everywhere else —
> §6.1.1.1 on `ADC`/`SBC` says *"(bit 15 when the m flag is 0, bit 7 when the m flag is 1)"* and §6.1.2.2 on
> `BIT` says *"bit 14 of the data when the m flag is 0, and bit 6 of the data when the m flag is 1"* — so
> this is an isolated typo in one sentence, not his convention. Taking that sentence literally puts the wrong
> bit into `C` on every `ASL` and `ROL`. Clark's own `CYCLES`/flag table for the same instructions is
> unaffected, and `8-2*m` reads correctly.

**The four implied register read-modify-writes.** Clark §6.1.1.3; Table 5-7 row 19a:

| Op | Opcode | cycles | bytes | Width | Flags | Clark § |
| --- | --- | --- | --- | --- | --- | --- |
| `DEX` | `$CA` | 2 | 1 | `x` | `x.....x.` (`N`,`Z`) | 6.1.1.3 |
| `DEY` | `$88` | 2 | 1 | `x` | `x.....x.` | 6.1.1.3 |
| `INX` | `$E8` | 2 | 1 | `x` | `x.....x.` | 6.1.1.3 |
| `INY` | `$C8` | 2 | 1 | `x` | `x.....x.` | 6.1.1.3 |

Clark states the width rule outright rather than leaving it to the flag column: *"DEX, DEY, INX, and INY are
16-bit operations when the x flag is 0 and 8-bit operations when the x flag is 1."* And for the memory and
accumulator forms: *"DEC and INC are 16-bit operations when the m flag is 0 and 8-bit operations when the m
flag is 1."*

**The flag instructions and `NOP`.** Clark §6.4.1 and §6.7; Table 5-7 row 19a. All 1 byte, 2 cycles:

| Op | Opcode | Flag column | Effect |
| --- | --- | --- | --- |
| `CLC` | `$18` | `.......0` | `c := 0` |
| `SEC` | `$38` | `.......1` | `c := 1` |
| `CLI` | `$58` | `.....0..` | `i := 0` |
| `SEI` | `$78` | `.....1..` | `i := 1` |
| `CLD` | `$D8` | `....0...` | `d := 0` |
| `SED` | `$F8` | `....1...` | `d := 1` |
| `CLV` | `$B8` | `.0......` | `v := 0` |
| `NOP` | `$EA` | `........` | none — *"performs no operation (affecting no flags or registers)"* |

`WDM` (`$42`, `2` bytes, `2` cycles, Clark §6.7) is **not** in this phase's 59 and is recorded only so nobody
adds it to the count.

**`XBA`** is `$EB`, 1 byte, **3** cycles — §13.5.

**The count, and a structural cross-check that it is right.** 28 memory RMW + 6 accumulator + 4 implied
register RMW + 12 transfers (§13.4) + 1 `XBA` + 7 flag instructions + 1 `NOP` = **59**, the phase's full
count. Independently: Table 5-7 row 19a's opcode list is `CLC, CLD, CLI, CLV, DEX, DEY, INX, INY, NOP, SEC,
SED, SEI, TAX, TAY, TCD, TCS, TDC, TSC, TSX, TXA, TXS, TXY, TYA, TYX, XCE`, headed `25 OpCodes, 1 byte, 2
cycles`. That is exactly this phase's 7 flag instructions + 4 implied register RMWs + `NOP` + 12 transfers =
24, plus `XCE`, which phase 7b already implemented (§11). The datasheet's own row enumerates the phase's
implied slice with nothing missing and nothing spare.

**Cycle-count behaviour in emulation mode.** Emulation forces `m = 1` (§7), so every formula above collapses
to its 8-bit value; **no note subtracts or adds a cycle for `E = 1` on any RMW or implied row.** Note 7
("Subtract 1 cycle for 6502 emulation mode (E=1)") appears on exactly two rows of Table 5-7 — 22a
(`ABORT, IRQ, NMI, RES`) and 22g (`RTI`) — and on none of rows 1d, 6b, 8, 10b, 16b, 19a or 19b.
Note 17 changes the *direction* of the middle cycle in emulation mode, not the cycle count: the row is the
same length either way.

### 13.4 The transfer rules, per instruction

Clark §6.10.1 (`TAX TAY TSX TXA TXS TXY TYA TYX`) and §6.10.2 (`TCD TCS TDC TSC`). All twelve are 1 byte and
**2 cycles**, and all twelve are in Table 5-7 row 19a, whose sequence is §9's implied block: cycle 1 opcode
fetch, cycle 2 `VDA=0 VPA=0`, `PBR,PC+1`, `IO`. Read the flag column with §6's key — `.` not affected,
`*` affected, `m` affected by the `(16-8*m)`-bit result, `x` affected by the `(16-8*x)`-bit result.

| Instr | Op | Width sized by | Flag column | `N`/`Z` | Cycles | Clark § |
| --- | --- | --- | --- | --- | --- | --- |
| `TAX` | `$AA` | destination `X` → **`x`** | `x.....x.` | yes, on the `(16-8*x)`-bit result | 2 | 6.10.1 |
| `TAY` | `$A8` | destination `Y` → **`x`** | `x.....x.` | yes | 2 | 6.10.1 |
| `TSX` | `$BA` | destination `X` → **`x`** | `x.....x.` | yes | 2 | 6.10.1 |
| `TXY` | `$9B` | destination `Y` → **`x`** | `x.....x.` | yes | 2 | 6.10.1 |
| `TYX` | `$BB` | destination `X` → **`x`** | `x.....x.` | yes | 2 | 6.10.1 |
| `TXA` | `$8A` | destination `A` → **`m`** | `m.....m.` | yes, on the `(16-8*m)`-bit result | 2 | 6.10.1 |
| `TYA` | `$98` | destination `A` → **`m`** | `m.....m.` | yes | 2 | 6.10.1 |
| `TXS` | `$9A` | destination `S` → **always 16-bit**, see below | `........` | **no flags** | 2 | 6.10.1 |
| `TCD` | `$5B` | **always 16-bit** | `*.....*.` | yes, on the 16-bit result | 2 | 6.10.2 |
| `TDC` | `$7B` | **always 16-bit** | `*.....*.` | yes | 2 | 6.10.2 |
| `TSC` | `$3B` | **always 16-bit** | `*.....*.` | yes | 2 | 6.10.2 |
| `TCS` | `$1B` | **always 16-bit** | `........` | **no flags** | 2 | 6.10.2 |

**The rule the table is an instance of**, Clark §6.10.1, verbatim:

> The size of the destination register (i.e. the register transferred to) determines whether these
> instructions are 8-bit operations or 16-bit operations. When the destination register is 8 bits wide,
> 8 bits are transferred, and when the destination register is 16 bits wide, 16 bits are transferred.
>
> The width of the accumulator is based on the m flag, and the width of the X and Y registers is based on
> the x flag, but the S register is always considered 16 bits wide. However, when the e flag is 1, SH is
> forced to $01, so in effect, TXS is an 8-bit transfer in this case since XL is transferred to SL and SH
> remains $01. Note that when the e flag is 0 and the x flag is 1 (i.e. 8-bit native mode), that XH is
> forced to zero, so after a TXS, SH will be $00, rather than $01. This is an important difference that
> must be accounted for if you want to run emulation mode code in (8-bit) native mode.

and §6.10.2, verbatim:

> TCD, TCS, TDC, and TSC transfer the C accumulator (the full 16-bit accumulator) to and from the D and S
> registers. These instructions always transfer 16 bits, no matter what the value of the m flag is.
> However, when the e flag is 1, SH is forced to $01, so in that case, TCS acts like an 8-bit transfer, by
> transferring the A accumulator (i.e. the low byte of the accumulator) to the SL register.

and, on the two flags, §6.10.1 and §6.10.2 in identical words:

> The n flag is 1 when the high bit of the result (i.e. the value transferred from one register to the
> other) is 1, and the n flag is 0 when the high bit of the result is 0. The z flag is 1 when the result is
> zero, and the z flag is 0 when the result is nonzero.

Note that `N`/`Z` are taken from **the value transferred**, i.e. the destination-width result, not from the
source register. Clark's worked example makes the distinction observable: *"If the accumulator is $1234, the
X register is $ABCD, and the m flag is 1, then after a TXA the accumulator will be $12CD, the n flag will be
1 (since only $CD was actually transferred), the z flag will be 0."*

**The brief's five claims, each confirmed or corrected:**

1. **`TAX`, `TAY`, `TSX`, `TXY`, `TYX` are sized by `x`** — **confirmed.** All five have destination `X` or
   `Y`, and all five carry Clark's `x.....x.` column.
2. **`TXA`, `TYA` are sized by `m`** — **confirmed.** Destination `A`, column `m.....m.`.
3. **`TCD`, `TDC`, `TCS`, `TSC` are 16-bit regardless of `m` and `x`** — **confirmed**, and stated in those
   words by §6.10.2 above.
4. **`TXS` and `TCS` set no flags; the other ten set `N` and `Z`** — **confirmed.** `$9A` and `$1B` are the
   only two of the twelve whose flag column is `........`.
5. **In emulation mode `TXS` and `TCS` force `SH = $01`** — **confirmed**, by both quotations above, and
   consistent with §7 and §11's independent finding about `SH`.

**One correction the brief's list does not contain, and it matters.** **`TXS` is *not* sized by `x`.** Its
destination is `S`, and *"the S register is always considered 16 bits wide"* — so in native mode `TXS`
always writes all 16 bits of `S` from `X`. The reason `TXS` *looks* 8-bit when `x = 1` is that `XH` is
forced to `$00`, so `SH` receives `$00`; and the reason it looks 8-bit when `e = 1` is that `SH` is forced
to `$01` afterwards. Neither is the transfer narrowing. Clark spells out the observable consequence: with
`e = 0, x = 1` a `TXS` leaves `SH = $00`, whereas with `e = 1` it leaves `SH = $01`. Implementing `TXS` as an
x-width transfer would leave the old `SH` intact in 8-bit native mode and be wrong on exactly the vectors
that set `SH != $00` before the instruction.

### 13.5 `XBA`

**`XBA` is `$EB`, 1 byte, 3 cycles, and its `N`/`Z` come from the new low byte as an 8-bit result regardless
of `m`.** Both halves are stated outright.

Clark §6.10.3, the table row and then the prose, verbatim:

```
OP LEN CYCLES      MODE      nvmxdizc e SYNTAX
-- --- ----------- --------- ---------- ------
EB 1   3           imp       *.....*. . XBA
```

> XBA exchanges the B accumulator and the A accumulator, i.e. it swaps the high byte and the low byte of the
> accumulator. Note that this is a swap rather than a copy (as is the case for the transfer instructions).
>
> The n and z flags are always based on an 8-bit result, no matter what the value of the m flag is.
> Specifically, they are based on the A accumulator (i.e. the low byte of the accumulator) result; in other
> words, the final value of the A accumulator, which is the same as the initial value of the B accumulator.

The flag column is `*.....*.` — `*` rather than `m`, which is Clark's own key (§6) distinguishing "affected"
from "affected by the `(16-8*m)`-bit result". The `*` is therefore consistent with, and a second statement
of, the prose: the width is not `m`-dependent.

**The cycle count is corroborated independently**, and `XBA` gets a Table 5-7 row of its own rather than
sharing 19a — `19b. Implied i`, `XBA`, `1 OpCode, 1 byte, 3 cycles` (p. 40):

```
XBA                                               1 opcode, 1 byte, 3 cycles
  1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1
  3              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1
```

Two internal cycles at the same address, both `PBR,PC+1` — row 19a's single `IO` cycle repeated, not a new
shape. This is why `XBA` is the one implied opcode in the phase that cannot reuse the two-cycle implied
sequence unchanged.

Clark's worked example, for a single-point check: *"If the accumulator is $6789, then after an XBA the
accumulator will be $8967, the n flag will be 0, the z flag will be 0."* The result `$8967` has bit 15 set
while `n` is `0`, so `n` comes from the new `A` of `$67`. **The example corroborates the rule but does not
establish it**, because Clark does not state the `m` flag's value for it: at `m = 1` an m-width
implementation would produce the same `n`. The rule rests on the prose above, which states it outright; the
example is a usable regression check and nothing more.

### 13.6 The gaps this section records, listed in one place

Everything above either carries a named source or appears here. Same practice as §12.6.

| # | Gap | Status |
| --- | --- | --- |
| 1 | **The address the middle cycle drives when `m = 1`.** Table 5-7 prints the `+1` (high) address on that cycle in all four rows — `DBR,AA+1`, `0,D+DO+1`, `DBR,AA+X+1`, `0,D+DO+X+1` — and gives **no separate 8-bit form**: the `(1)`-gated high-half rows are skipped when `m = 1`, but the middle cycle's own address expression is printed unconditionally and still carries the `+1`. **The sources are silent** on whether the 8-bit middle cycle drives `AA` or `AA+1`. Not resolvable from the 6502 or 65C02 either, since neither has a 16-bit form for the `+1` to come from | **CLOSED 2026-08-05, measured.** It drives the **low** address, `AA` — the plan's `_addr` was right. All 160,000 native vectors pass with it, and all sixteen emulation vectors matched on the address field. See §13.1's "Measured, not cited" note. The `+1` in the printed rows is the 16-bit form's alone |
| 2 | **Whether the emulation-mode middle cycle asserts `VDA`.** Note 17 says only *"the RWB is low during both write and modify cycles"* — it speaks of `RWB` and nothing else. Table 5-7 prints `VDA = 0, VPA = 0` on that cycle without qualification, but a cycle that genuinely writes memory asserting neither address-valid pin would be unusual. **The sources are silent** on which of the two the emulation case is | **CLOSED 2026-08-05, measured.** It does **not**. Table 5-7's `VDA = 0, VPA = 0` is literal and holds in emulation mode too, so the native and emulation middle cycles are pin-identical and differ only in `RWB`. The "unusual" reading was the correct one. Established by `06 e` vector 1 cycle 4, `---wemxl` against `d--wemxl`, and by all sixteen opcodes agreeing. See §13.1's "Measured, not cited" note |
| 3 | **What value the emulation-mode middle cycle writes.** Note 17 states the *direction* only. That the NMOS double-write puts back the **unmodified** value is a property of the NMOS 6502 — which this codebase has certified separately — and **the 65816 sources do not state it**. Recorded explicitly as an inference from a different part, not from a 65816 source, per this document's rule | **Corroborated 2026-08-05, still an inference.** The unmodified value is right: the vectors' value field matched on all sixteen opcodes. Remains an inference from a different part that the vectors confirmed, not a cited 65816 fact — the distinction is kept deliberately, per this document's rule |
| 4 | **`TRB`/`TSB`'s `Z` at 16 bits.** Clark §6.1.2.3 states *"The z flag reflects whether the result (of the bitwise And) is zero"* and *"These are 16-bit operations when the m flag is 0"*, but his only worked example is 8-bit. So the width is stated and the `Z` rule is stated; what no source gives is a 16-bit worked value to check an implementation against, the way Example 2 does for decimal `SBC` in §12.1 | **Recorded, not open.** Both rules are cited. Noted only so a later reader knows there is no 16-bit reference value in any source and the vectors are the first check |
| 5 | **Whether any of the 59 behaves differently in emulation mode beyond the forced `m = 1`/`x = 1`.** Clark's §6 preamble's *"In general, in emulation mode … the 65C816 has the same behavior as 65C02"* is the only statement in view, and §12.6 already records that "In general" was load-bearing and that this exact sentence turned out to be **wrong** for decimal `SBC`. It is not treated as a citation here | **Open by policy.** Treat as hypothesis; the vectors arbitrate |

**Not gaps, so nobody re-opens them:** the native middle cycle's nature (§13.1, Table 5-7, all four rows
agreeing); the 16-bit write order (§13.2, stated by the rows and forced by the cycle counts); every one of
the 59 cycle formulas (§13.3, Clark and Table 5-7 agreeing independently); `abs,X` RMW having no `p` term
(§13.1's unmarked cycle 4 and Clark's flat `9-2*m`); all twelve transfer width and flag rules (§13.4,
Clark §6.10.1–2, quoted); `XBA`'s 3 cycles and 8-bit `N`/`Z` (§13.5, both stated and both corroborated).

**And two things a reader of Clark alone would get wrong, repeated here because they are the section's most
expensive findings:** `MLB` is asserted on the RMW data cycles including the internal one, so the pin
string's eighth character is not `-` there (§13.1); and Clark's §6.1.3 prose has the `m`-flag polarity
inverted for `ASL`'s carry bit (§13.3).

**Gaps 1, 2 and 3 were closed on 2026-08-05 by phase 7c′ task 2, from the vectors** — see §13.1's
"Measured, not cited" note for the measurement and the vector that established it. Gaps 4 and 5 remain as
recorded. Worth noting how the three fell: gap 1 confirmed the plan's guess, gap 2 refuted it, and gap 3
confirmed an inference borrowed from a different part. All three had been written down as open *before* the
code was run, which is the only reason the second one reads as a measurement rather than as a constant
tuned to fit a failing vector.

## 14. Phase 7d's unsettled questions — the stack, the interrupts, the block moves and the halts

Added 2026-08-05, before any phase 7d code was written. Same practice as §9, §12 and §13: transcribe first,
implement second. The phase 7d plan
(`docs/superpowers/plans/2026-08-05-phase7d-control-flow-stack-interrupts.md`) cites this material as
**§14.1**–**§14.8**, which is the numbering used here — like §13 and unlike §12, there is no offset to map.

**Sources, fetched for this section rather than recalled.** Clark, "65C816 Opcodes", from the GitHub mirror
`https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
(**137,171 bytes**, footer "Last Updated September 28, 2015") — the mirror because 6502.org itself 404s
non-browser agents (§2.2). WDC W65C816S datasheet from
`https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf` (**1,532,025 bytes**, header
"March 13, 2024"). Both byte counts are identical to the ones §12 and §13 recorded, so this is demonstrably
the same pair of files a third time, not a re-issue. Datasheet material used: Table 5-7 rows 1b, 1c, 2a, 2b,
3a, 3b, 4b, 4c, 9a, 9b, 19c, 19d, 20, 21, 22a–22j (pp. 36–42) and its Notes (p. 43); Tables 5-2 and 5-3,
the vector locations (p. 30); §2.8 (p. 7); §7.11, §7.13, §7.14, §7.16, §7.18 and §7.22 (pp. 51–53).

**Vector files read directly**, from the local cache at
`tests/SixtyFiveXX.Conformance/.harte-cache/65816/v1/`, downloaded by the same URL shape
`Harte816Cache.Download` uses. `54.n` (37,853,698 bytes) and `44.n` (37,836,551) for §14.3, `cb.n`, `cb.e`,
`db.n`, `db.e` (~4.4 MB each) for §14.4, and `42.n`, `42.e`, `08.n`, `08.e`, `28.n`, `28.e`, `00.n`, `00.e`,
`02.n`, `02.e`, `54.e`, `44.e` for the measured blocks below. Every measured claim names the file and the
vector. **No file that was needed proved unobtainable.**

**Notation** is §5's, §9's and §13's: `m` and `x` are the flag values (0 or 1), `e` is the emulation flag,
`w` is 1 when `DL != $00`, `p` is 1 on a page cross; `IO` is an internal cycle with `VDA = VPA = 0` and no
memory access, recorded `null` in a vector. `(1)` is the high half of a 16-bit access and `(2)` the
direct-page penalty. One symbol is new and it is Clark's own: **`t` is 1 when a branch is taken**, 0
otherwise.

**One extension to the column set, and like §13's it is load-bearing.** §9 dropped `VPB` and `MLB`; §13
restored `MLB` because the read-modify-write rows assert it. **`MLB` is `1` throughout every row in §14 and
is dropped again. `VPB` is not.** Table 5-7 prints `VPB = 0` — asserted, active-low — on the two cycles that
fetch an interrupt vector, in rows 22a and 22j. `VPB` is the **third character of the pin string the vectors
assert** (`BuildPinString` in `tests/SixtyFiveXX.Conformance/Harte816Tests.cs`, slot 2, the `v`), so §14.2's
blocks carry a `VPB` column. Every other block below omits it, because it is `1` throughout.

**And the discipline §13.1 earned, restated because this section is where it bites.** Table 5-7's pin
columns must be *read*, not inferred from a cycle's apparent purpose. §13 established that by sixteen vector
failures isolating one character of an eight-character string. Applied here, it produces one result the
plan would probably have got wrong by inference: the two vector-pull cycles assert `VPB` **and `VDA` at the
same time** (§14.2). A cycle whose whole job is "fetch the vector" is nonetheless a `VDA` data cycle.

### 14.1 The stack — the seven pushes, the six pulls, and where `S` lives

Transcribed from Table 5-7 rows 22b and 22c (p. 41), in §9's format. `MLB` is `1` and `VPB` is `1` on every
cycle of both rows and both are dropped; `RWB` is kept, since the pushes write and the pulls read.

```
22c. Stack s
PHA, PHB, PHP, PHD, PHK, PHX, PHY                 7 opcodes, 1 byte, 3 and 4 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
  3a  (1)        VDA=1 VPA=0   0,S          REG High    RWB=0
  3              VDA=1 VPA=0   0,S-1        REG Low     RWB=0

22b. Stack s     ("Different than N6502" — the datasheet's own row label)
PLA, PLB, PLD, PLP, PLX, PLY                      6 opcodes, 1 byte, 4 and 5 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
  3              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
  4              VDA=1 VPA=0   0,S+1        REG Low     RWB=1
  4a  (1)        VDA=1 VPA=0   0,S+2        REG High    RWB=1
```

**A push has one internal cycle and a pull has two**, both at `PBR,PC+1`. That asymmetry is the whole of the
one-cycle difference between the two rows, and the datasheet flags it in the row label rather than in a
note. §13.2 already quoted these two rows for a different purpose — the print-order convention — and this is
the same transcription, extended with the notes.

**One note-column caveat, resolved rather than guessed, in the form §13.1's row 16b uses.** Row 22c prints
`(1)` on the line of cycle `2` and `(12)` on the line of `3a`. Verified against a 400 dpi rendering of p. 41,
not only the extracted text: the baselines really are aligned that way. Note `(1)` is *"Add 1 byte (for
immediate only) for M=0 or X=0 …, add 1 cycle for M=0 or X=0"*, which cannot gate cycle 2 — cycle 2 is
present in the 3-cycle case, or the row's low figure would be 2. It gates `3a`, and row 22b puts the same
note on `4a` unambiguously, on the row where the printed alignment is clean. **The marker belongs on `3a`.**
Note `(12)` is *"PHP and PLP."* — that is its entire text, it gates nothing, and **it explains nothing**;
recorded verbatim so a later reader does not go looking for a rule inside it.

Now the six questions the phase 7d brief asks outright.

**1. Does native-mode `S` wrap within bank 0, or can a push at `S == $0000` reach bank 1? — Bank 0,
confirmed.** Clark §5.1.2, verbatim, the same passage §7 already cites:

> The following are confined to bank 0 ("confined to" means they address bank 0 and wrap at the bank 0
> boundary):
>
> A. The direct page
> B. The stack

And Clark §5.22, which gives the address form rather than the rule — note the literal `0` in the bank
position, for pushes and pulls alike:

> Otherwise, the address of the data for an 8-bit push is:
>
>     +-----------+-----------+-----------+
>     !     0     !           S           ! data lo
>     +-----------+-----------+-----------+

Table 5-7 agrees cell by cell: every stack cycle in rows 22a–22j prints its address as `0,S`, `0,S-1`,
`0,S+1` … — bank `0` written out, never `PBR,` and never `DBR,`. **`HighByteAddressBank0`'s assumption is
confirmed, and §14.6 extends it: `JSL`'s and `RTL`'s three-byte stack accesses are `0,S`/`0,S-1`/`0,S-2`
too.** Clark §5.22 also states the pointer arithmetic per mode: *"In emulation mode, SL will be decremented
N times / In native mode, S will be decremented N times"* — so the native wrap is a 16-bit wrap inside
bank 0 and the emulation wrap is, for *some* opcodes, an 8-bit wrap inside page one. Which opcodes is not a
mode question at all — it is the "Otherwise" quoted above eliding its own antecedent, corrected next.

**The emulation-mode page-one wrap is not universal, and the "Otherwise" quoted above was only ever half
of Clark §5.22's rule.** In full, verbatim, the sentence that actually decides which stack accesses wrap:

> For all interrupts and "old" instructions, when the e flag is 1, the address of the data for an 8-bit
> push is:
>
>     +-----------+-----------+-----------+
>     !     0     !     1     !    SL     ! data lo
>     +-----------+-----------+-----------+
>
> Otherwise, the address of the data for an 8-bit push is:
>
>     +-----------+-----------+-----------+
>     !     0     !           S           ! data lo
>     +-----------+-----------+-----------+

This is §5.1.1's old/new split (already quoted in §7 and in `Cpu.StackWrapsInPageOne`'s doc comment)
restated for the stack specifically, and it names **interrupts** in the very same clause as "old"
instructions — not as a separate case, as the same case. §14.7 already carries the specific instance of
this for `PEA`/`PEI`, quoting Clark §5.1.1 directly: *"since PEI is a 'new' instruction, PEI $FF does not
wrap at a page boundary"*; this is that same predicate's general form, and the two sections agree.

*Not measured, and recorded as such.* The `08.n` and `28.n` vector sets contain **no** vector with
`S <= $0001` or `S >= $FFFE`, so the bank-0 wrap at `S == $0000` is not exercised by `PHP` or `PLP` and this
answer rests on the two citations above, not on a measurement. The emulation-mode page-one wrap *is*
exercised, by an unrelated opcode: `02 e 1` (`COP`) starts with `S = $3000` and writes at `$0100`, `$01FF`,
`$01FE` — `SH` forced to `$01`, then `SL` wrapping `$00 → $FF` within page one. `COP` is new to the 65816,
but §5.22's predicate never turned on age alone — `COP` is also an interrupt, and interrupts wrap. There is
no tension between "`COP` is new" and "`COP` wraps"; there would only be tension if `COP` wrapped while
some *other* new, non-interrupt instruction did not, and §14.7's `PEI` citation and the measurement below
show that it does not.

#### Measured — Clark §5.22's predicate, against every stack opcode measured so far, phase 7d task 2 review fix

Added 2026-08-05, after a review of phase 7d task 2 caught this section's `COP` citation reading as a
counter-example to the old/new split rather than as an instance of it. Everything below is measured
against the SingleStepTests vectors, in addition to the §5.22 citation above, not instead of it.

**The thirteen push/pull opcodes' own old/new split, from phase 7d task 2's implementation work** (see
`Cpu.StackWrapsInPageOne`'s doc comment for the full accounting): `ab e 75` starts at `S = $27FF` (forced
to `$01FF`) and `PLB` — new — reads at `$000200`, not `$000100`; `28 e 311` starts at `$01FF` and `PLP` —
old — reads at `$000100`; `0b e 435` is the discriminating push, `PHD` — new — from `S = $0100` writing
`$000100` then `$0000FF`, ending at `S = $01FE`, below page one entirely.

**The same split holds across every other stack-touching opcode measured, including the interrupts and the
opcodes JSL/RTL/PER/PEA add in later tasks:**

| Opcode | §5.22 category | Out-of-page-one emulation-mode stack accesses |
| --- | --- | --- |
| `JSR` | old | 0 |
| `BRK` | interrupt | 0 |
| `COP` | interrupt | 0 |
| `JSL` | new | 103 |
| `RTL` | new | 196 |
| `PER` | new | 34 |
| `PEA` | new | 51 |

> **Zero exceptions across all seven: old and interrupt opcodes never leave page one in emulation mode,
> and new ones that touch the stack always can.** Discriminating example, the same shape as `0b e 435`'s
> `PHD` above: `22 e 61` (`JSL`) writes `$0000FF` from `S = $0100` — below page one, exactly what a
> non-wrapping push produces and exactly what a wrapping one cannot.

**2. Are the pushes high byte first and the pulls low byte first? — Yes, both.** Row 22c writes `REG High`
at `0,S` and then `REG Low` at `0,S-1`; row 22b reads `REG Low` at `0,S+1` and then `REG High` at `0,S+2`.
The stack descends, so the printed order is forced and is not a matter of interpretation — the argument
§13.2 makes at length. Clark corroborates instruction by instruction: `PEA` §6.8.1 (*"after PEA #$1234,
$0001FF will contain $12, $0001FE will contain $34"*), `JSR`/`JSL` §6.2.2.1 (*"The high byte is pushed
first, then the low byte"*), `BRK`/`COP` §6.3.1 (*"push the 16-bit address (again high byte first, then low
byte)"*), and `PLA` §6.8.2 (*"$0001FE contains $AB, $0001FF contains $CD … the accumulator will be $CDAB"*,
i.e. low byte from the lower address, pulled first). Rows 22d, 22e and 22f put `PEA`, `PEI` and `PER` in the
same high-then-low order — see §14.7.

**3. What does `PHP` push for bits 4 and 5 in each mode, and what does `PLP` load into them? — `PHP` pushes
`P` verbatim; `PLP` loads `P` verbatim in native mode and forces bits 4 and 5 to `1` in emulation mode.**
Clark §6.8.3 states only *"For PLP, (all of) the flags are pulled from the stack"* and *"when the e flag is
1, the m and x flag are forced to 1, so after the PLP, both flags will still be 1 no matter what value is
pulled from the stack"*, and says nothing whatever about what `PHP` pushes into bits 4 and 5. **On `PHP`
the sources are silent.** Measured — see the block below.

**4. Does `PLP` setting `x = 1` force `XH = YH = $00` immediately, as `SEP` does? — Yes.** No source names
`PLP` in this connection. What Clark §4 states is a property of the *flag*, not of the instruction that set
it: *"One important difference from the m flag is that when the x flag is 1 (8-bit index registers), the XH
register and the YH register are both forced to $00"*, and *"Attempting to change the value of the XH
register or the YH register when the x flag is 1 will have no effect on XH or YH."* His worked example uses
`SEP`. Reading that general rule as covering `PLP` is an inference, so it was **measured before being
relied on** — see the block below.

**5. Cycle counts for all thirteen.** Clark §6.8.2 and §6.8.3, transcribed opcode by opcode:

| Push | Op | Cycles | Pull | Op | Cycles |
| --- | --- | --- | --- | --- | --- |
| `PHA` | `$48` | `4-m` | `PLA` | `$68` | `5-m` |
| `PHX` | `$DA` | `4-x` | `PLX` | `$FA` | `5-x` |
| `PHY` | `$5A` | `4-x` | `PLY` | `$7A` | `5-x` |
| `PHB` | `$8B` | `3` | `PLB` | `$AB` | `4` |
| `PHD` | `$0B` | `4` | `PLD` | `$2B` | `5` |
| `PHK` | `$4B` | `3` | — | — | — |
| `PHP` | `$08` | `3` | `PLP` | `$28` | `4` |

Corroborated by row 22c's header (`3 and 4 cycles`) and row 22b's (`4 and 5 cycles`): the flat-3 pushes are
the 8-bit registers `DBR`, `K` and `P`; the flat-4 push is `PHD`, whose `D` register is 16 bits wide
regardless of `m`; the flat-4 pulls are `PLB` and `PLP` and the flat-5 pull is `PLD`, for the same reason.
Every enumerated header value is reproduced and none is left over. **No `w`, no `p` and no `e` term appears
on any of the thirteen** — the stack instructions cost the same in both modes.

**6. Which cycles are internal, and what address each drives.** Push: cycle 2 only, at `PBR,PC+1`. Pull:
cycles 2 **and** 3, both at `PBR,PC+1`. Neither row has any other `IO`. This is the same address §9's row
19a records for the implied form and §13.5 for `XBA` — one past the opcode, not the opcode's own address.

#### Measured, not cited: `PHP`'s pushed byte, `PLP`'s loaded byte, and `PLP`'s effect on `XH`/`YH`

Added 2026-08-05 by phase 7d task 1, from the vectors, in §12's and §13.1's form for a measured result.
None of the three is in Clark or the datasheet, and all three were written down as open above **before** the
measurement was taken.

> **`PHP` pushes the `P` register verbatim, all eight bits, in both modes.** Across all 10,000 vectors of
> `08 n` and all 10,000 of `08 e`, the byte written on the single write cycle XORed against the initial `P`
> is `$00` — 20,000 of 20,000. No bit is forced, set or cleared on the way out.
>
> **`PLP` loads the pulled byte verbatim in native mode, and forces bits 4 and 5 to `1` in emulation mode.**
> All 10,000 vectors of `28 n`: final `P` XOR pulled byte is `$00`. All 10,000 of `28 e`: that XOR is one of
> `$00`, `$10`, `$20`, `$30` and nothing else — the two forced bits and no others, which is Clark's *"the m
> and x flag are forced to 1"* confirmed and bounded.
>
> **`PLP` setting `x = 1` forces `XH = YH = $00` immediately, exactly as `SEP` does.** 2,494 of the 10,000
> `28 n` vectors end with the `x` flag set *and* began with a nonzero `XH` or `YH`; in every one of them the
> final `XH` and `YH` are `$00`. Example, `28 n 11`: `X = $A0B7`, `Y = $5953`, `P = $AF` → pulled `$39`
> (bit 4 set), final `X = $00B7`, `Y = $0053`. The measurement is not vacuous and the inference from Clark
> §4's flag-scoped rule was right.

**One thing this measurement cannot discriminate, stated so it is not over-read.** In `08 e` the initial `P`
has bits 4 and 5 both set in **all 10,000** vectors — the emulation invariant is baked into the vector set's
initial states, since `m` and `x` are forced to 1 and there is no legal emulation `P` with either bit clear.
So "push `P` verbatim" and "push `P | $30` in emulation mode" are **observationally identical** on this
vector set. The verbatim form is the one to implement, because it is also what the native measurement shows
and it needs no mode test; but a core that ORs in `$30` would pass too, and no vector distinguishes them.

### 14.2 The interrupts — `BRK`, `COP`, `IRQ`, `NMI`, and `WDM`

Transcribed from Table 5-7 rows 22a and 22j (pp. 41–42). These are the only two blocks in §14 that carry a
`VPB` column, and it is the reason the column exists.

```
22a. Stack s
ABORT, IRQ, NMI, RES                              4 hardware interrupts, 0 bytes, 7 and 8 cycles
  1   (3)        VPB=1 VDA=1 VPA=1   PBR,PC       IO             RWB=1
  2   (7)?       VPB=1 VDA=0 VPA=0   PBR,PC       IO             RWB=1
  3   (10)       VPB=1 VDA=1 VPA=0   0,S          PBR            RWB=0    <- omitted when e = 1; RWB stays 1 for RES (Note 10)
  4   (10)       VPB=1 VDA=1 VPA=0   0,S-1        PCH            RWB=0    <- RWB stays 1 for RES (Note 10)
  5   (10)       VPB=1 VDA=1 VPA=0   0,S-2        PCL            RWB=0    <- RWB stays 1 for RES (Note 10)
  6   (11)       VPB=1 VDA=1 VPA=0   0,S-3        P              RWB=0    <- RWB stays 1 for RES (Note 10)
  7              VPB=0 VDA=1 VPA=0   0,VA         AAVL           RWB=1
  8              VPB=0 VDA=1 VPA=0   0,VA+1       AAVH           RWB=1
  1              VPB=1 VDA=1 VPA=1   0,AAV        Next OpCode    RWB=1

22j. Stack s
BRK, COP                                          2 opcodes, 2 bytes, 7 and 8 cycles
  1              VPB=1 VDA=1 VPA=1   PBR,PC       OpCode         RWB=1
  2   (3)        VPB=1 VDA=0 VPA=1   PBR,PC+1     Signature      RWB=1
  3   (7)        VPB=1 VDA=1 VPA=0   0,S          PBR            RWB=0    <- omitted when e = 1
  4   (10)       VPB=1 VDA=1 VPA=0   0,S-1        PCH            RWB=0
  5   (10)       VPB=1 VDA=1 VPA=0   0,S-2        PCL            RWB=0
  6   (10)(16)   VPB=1 VDA=1 VPA=0   0,S-3        P              RWB=0
  7              VPB=0 VDA=1 VPA=0   0,VA         AAVL           RWB=1
  8              VPB=0 VDA=1 VPA=0   0,VA+1       AAVH           RWB=1
  1              VPB=1 VDA=1 VPA=1   0,AAV        Next OpCode    RWB=1
```

**The `(7)?` on row 22a cycle 2 is not a transcription slip; it is the datasheet's placement, and it is
wrong.** Verified against a 400 dpi rendering of p. 41: row 22a prints `(3)`, `(7)`, `(10)`, `(10)`, `(10)`,
`(11)` on the baselines of cycles 1, 2, 3, 4, 5, 6 respectively. Note 7 is *"Subtract 1 cycle for 6502
emulation mode (E=1)"*, and the cycle emulation mode omits is the **`PBR` push**, not an internal cycle:
row 22j puts the same note on **cycle 3**, its `PBR` push, on a row where nothing is ambiguous; datasheet
§7.11.2 states *"In Emulation Mode … previous contents of the PBR are not automatically saved"*; and Clark
§6.3.1.1 gives the emulation sequence with no `K` push. **Cycle 3 is the omitted cycle in both rows.** Row
22a's note column is off by one for this marker — the same class of typographic slip §13.1 records for row
16b, and recorded here rather than silently corrected. (Note also that only three `(10)`s are printed
against four write cycles, so the column plainly cannot hold everything that applies.)

**The notes on these rows, verbatim** (p. 43):

> 7. Subtract 1 cycle for 6502 emulation mode (E=1).
>
> 9. Wait at cycle 2 for 2 cycles after NMIB or IRQB active input.
>
> 10. RWB remains high during Reset.
>
> 11. BRK bit 4 equals "0" in Emulation mode.
>
> 16. COP Latches.

Note 16 is the whole of what the datasheet says about "COP Latches" and it is attached to row 22j's *address
bus* cell (`0,S-3 (16)`), not to a behaviour. **It explains nothing**; §8 flagged it for this phase and it
turns out to carry no content. Note 9 is about the interrupt-recognition handshake, not about any cycle a
vector records; it is transcribed here so §8's deferral is discharged, and it is not implementable against
the vector set (see the gaps below). Note 11's wording says *"BRK bit 4"* but it is printed on row **22a**,
the hardware-interrupt row, and *not* on row 22j — so it means "the break bit, bit 4, is 0" for `IRQ`, `NMI`
and `ABORT` in emulation mode, which is the 6502's rule and the opposite of what a literal reading ("`BRK`
pushes 0") would give. Corroborated by the datasheet's Table 8-1, `BRK Vector` row, `W65C816S` column,
verbatim: *"00FFFE,F(E=1) BRK bit=0 on stack if IRQ-NMIB, ABORTB / 000FFE6,7 (E=0), X=X on stack always"*.

**1. The vector address for each, in each mode, as a number.** Datasheet Tables 5-2 and 5-3 (p. 30) and
Clark §6.3.1.1 agree exactly, cell for cell:

| Interrupt | Native (`e = 0`) | Emulation (`e = 1`) |
| --- | --- | --- |
| `COP` | `$00FFE4` | `$00FFF4` |
| `BRK` | `$00FFE6` | `$00FFFE` |
| `ABORT` | `$00FFE8` | `$00FFF8` |
| `NMI` | `$00FFEA` | `$00FFFA` |
| `RESET` | — (none) | `$00FFFC` |
| `IRQ` | `$00FFEE` | `$00FFFE` |

There is no native `RESET` vector — Clark: *"A RESET interrupt puts the 65C816 into emulation mode, thus
there is no native mode RESET vector"*, and Table 5-3 simply has no row for it. `BRK` and `IRQ` share
`$00FFFE` in emulation mode and have separate vectors in native mode. The vector is fetched from **bank 0**:
both rows print `0,VA` and `0,VA+1`, and the *next opcode* is fetched at `0,AAV` — bank 0 again.

**2. Is `PBR` pushed, and is it cleared before the handler runs? — Pushed in native mode only; cleared in
both.** Datasheet §7.11, verbatim:

> 7.11.1 When in the Native mode, the Program Bank register (PBR) is cleared to 00 when a hardware
> interrupt, BRK or COP is executed. In the Native mode, previous PBR contents are automatically saved on
> Stack.
>
> 7.11.2 In Emulation Mode the PBR register is cleared to 00 when a hardware interrupt, BRK or COP is
> executed. In this case, previous contents of the PBR are not automatically saved.

Table 5-7's own `0,AAV` on the next-opcode row of both 22a and 22j says the same thing from the bus side.
Clark §6.3.1.1 states the push half only (*"In native mode, the K register … is pushed"* / in emulation mode
it is absent from the list) and is **silent on the clearing**; the datasheet is the source for that half.

**3. What the pushed `P` holds in bit 4, in each mode.**

- **Native mode: bit 4 is the `x` flag, always, for every one of the six.** Table 8-1, quoted above:
  *"X=X on stack always"*. Clark §6.3.1's worked example is the same fact from the other side: `P = $08`,
  `e = 0`, `BRK` at `$123456` stores `$08` at `$0001FC` — the pushed byte is `P` unchanged, and `$08` has
  bit 4 clear because `x` is 0.
- **Emulation mode, hardware interrupts (`IRQ`, `NMI`, `ABORT`): bit 4 is `0`.** Note 11 and Table 8-1.
- **Emulation mode, `BRK`: bit 4 is `1`.** Clark §6.3.1: *"When BRK pushes the P register, the b flag …
  will be set; because, in emulation mode … BRK and IRQ share an interrupt vector, this allows the BRK/IRQ
  handler to distinguish a BRK from an IRQ."* (Clark's parenthetical "(i.e. bit 5)" in that sentence is
  wrong; see §3.3.) The datasheet's register diagram (p. 10) labels the same bit *"B — BRK Bit 1=BRK 0=IRQ"*.
- **Emulation mode, `COP`: the sources are silent.** Note 11 and Table 8-1 enumerate `IRQ`, `NMI` and
  `ABORT`; Clark's sentence is scoped to `BRK`. Neither says what `COP` pushes. Measured below.

**4. Is `D` cleared, is `I` set, and when relative to the push? — Both, and after the push.** Clark §6.3.1:
*"The i flag is set after pushing the P register; furthermore, like the 65C02 (but unlike the NMOS 6502),
the d flag is cleared after pushing the P register"*, repeated verbatim for hardware interrupts in
§6.3.1.1. Datasheet p. 30, immediately under Table 5-3: *"When an interrupt is executed, D=0 and I=1 in
Status Register P."* Two sources, and the "after the push" ordering is stated only by Clark — the datasheet
gives the end state and not the ordering. Measured below, and the measurement agrees.

**5. Which cycles assert `VPB`, and do any assert `VDA` at the same time? — Cycles 7 and 8 (the two vector
fetches), and yes, both assert `VDA`.** The table prints `VPB=0, VDA=1, VPA=0` on both, in both rows, and
the datasheet says it a second time in prose on p. 30: *"The VP output is low during the two cycles used for
vector location access."* This is the §13.1 discipline paying off: a cycle labelled "fetch the vector" is
nonetheless a `VDA` data cycle, and inferring `VPA` from "it is fetching an address the PC will use" would
have produced the wrong pin string on two cycles of every interrupt. Measured below, and the measurement
agrees.

**6. Total cycle count in each mode.** `8-e` for `BRK` and `COP` (Clark §6.3.1, `00 1 8-e` and
`02 2 8-e`), and 7-or-8 for the hardware interrupts (row 22a's header, plus note 7). Native 8, emulation 7.

**7. Does the NMOS `NMI`-hijack anomaly exist on this part? — Unknown; the sources are silent, and it is
not measurable.** Neither Clark nor the datasheet mentions the case at all: an `NMI` asserted while a `BRK`
sequence is already running, such that the `BRK` fetches the `NMI` vector instead of its own. The nearest
statement is Clark §6.3.1.1, which is about a *different* case — an interrupt arriving mid-instruction:

> If an IRQ or NMI occurs in the middle of an instruction (e.g. after the first cycle of a BCC instruction),
> then the instruction is completed before pushing anything and jumping to the interrupt vector.

That says the instruction finishes first; it does not say what happens when the instruction that finishes is
itself `BRK`. **The sources are silent on the hijack.** It cannot be settled from the vectors either — see
the gaps below.

**8. `WDM` (`$42`).** Clark §6.7 gives `42 2 2 imm` — 2 bytes, 2 cycles — and *"On the 65C816, it is acts
like a 2-byte, 2-cycle NOP … The second byte is read, but ignored."* The datasheet has **no Table 5-7 row
for `WDM` at all** (row 18, Immediate, lists 14 opcodes and `WDM` is not among them) and §7.16 says only
*"The WDM opcode may be used on future microprocessors. It performs no operation."* So the datasheet is
silent on `WDM`'s cycle shape, and Clark's sentence is the only claim about the second byte — and it is
wrong. The brief's framing ("what its second byte does — fetched or not — decides whether it is two cycles
or three") has a third answer: **the second byte is not fetched, and it is still two cycles**, because the
cycle that would have fetched it is an internal cycle. Measured below; see also §3.4.

#### Measured, not cited: `BRK` and `COP` end to end, and `WDM`'s second cycle

Added 2026-08-05 by phase 7d task 1. All figures below are from the four `BRK`/`COP` files and the two
`WDM` files, 60,000 vectors in total. Everything here either confirms a citation above — in which case it is
labelled as confirmation, not as the source — or closes a stated silence.

> **Confirmations.** `00 n` and `02 n` are **8 cycles** with **4 writes**; `00 e` and `02 e` are **7 cycles**
> with **3 writes**, 10,000 of 10,000 each — note 7 and `8-e`. The vector addresses are `$00FFE6`, `$00FFE4`,
> `$00FFFE`, `$00FFF4`, 10,000 of 10,000 each — Tables 5-2/5-3. Final `PBR` is `$00` in all 40,000 — §7.11.
> Final `P & $0C` is `$04` in all 40,000, i.e. `d = 0` and `i = 1` — p. 30. Exactly two cycles per vector
> carry `v` in slot 2, they are the last two, and their pin strings are `d-vr…` — `VDA` set, `VPA` clear,
> read — which is Table 5-7's `VPB=0, VDA=1, VPA=0` confirmed character by character.
>
> **The pushed `P` is the pre-instruction `P`, byte for byte, in both modes.** Pushed byte XOR initial `P`
> is `$00` in all 40,000 vectors. So `d` and `i` are modified strictly *after* the push, which is Clark's
> ordering; and in native mode bit 4 of the pushed byte is simply the `x` flag, which is Table 8-1's
> "X=X on stack always".
>
> **The gap on emulation `COP`'s bit 4 is closed: it is `1`.** In `02 e` the pushed byte equals `P`, and in
> all 10,000 `02 e` vectors the initial `P` has bits 4 and 5 set (`P & $30 == $30`), as it must in emulation
> mode. So emulation `COP` pushes bit 4 = 1, the same as emulation `BRK`. **Caveat, the same one §14.1
> records for `PHP`:** because no legal emulation `P` has bit 4 clear, "push `P` verbatim" and "push
> `P | $10`" are observationally identical here. The verbatim rule is the one both measurements support.
>
> **The pushed address is the instruction's address plus 2, for both `BRK` and `COP`, in both modes.**
> Under 16-bit wrap, pushed 16-bit address minus initial `PC` is exactly 2 in 40,000 of 40,000 vectors. The
> wrap is exercised by exactly two of them: `00 e 2917` (`PC = $FFFE`, pushed address wraps to `$0000`) and
> `02 e 3935` (`PC = $FFFF`, pushed address wraps to `$0001`). That is Clark §6.3.1 (*"push the 16-bit
> address … of the BRK or COP instruction plus 2"*) plus the bank wrap of §4, measured. **`BRK` is a
> two-byte instruction** — datasheet §7.22, *"The BRK instruction for the NMOS 6502, 65C02 and 65C816 is
> actually a 2 byte instruction"* — even though Clark's own table prints `LEN` 1 for it and 2 for `COP`.
> Clark explains the discrepancy himself: *"most 65C816 assemblers will also assemble a BRK instruction as a
> one byte instruction … despite this, on all members of the 6502 family, the BRK instruction is really a
> two byte instruction"*. The signature byte **is** fetched — row 22j cycle 2 prints `VPA=1` and a real
> `Signature` value, and every vector's cycle 2 has a non-null value with pin string `-p-r…`.
>
> **`WDM`'s second cycle is an internal cycle, and Clark is wrong about it.** In all 10,000 vectors of
> `42 n` and all 10,000 of `42 e`, cycle 2 has value `null` and pin string `---r…` (`VDA = 0`, `VPA = 0`),
> the file is exactly 2 cycles long, and `PC` advances by exactly 2. Sample, `42 n 1`: `PC $50DE → $50E0`,
> cycles `[$FA50DE, $42, "dp-r-mx-"]` and `[$FA50DF, null, "---r-mx-"]`. So `WDM` is `$42`, **2 bytes, 2
> cycles**, one opcode fetch and one internal cycle at `PBR,PC+1` — the same shape as §9's row 19a implied
> form except that `PC` advances by two instead of one. See §3.4.

#### Implemented, and the two choices this section forced — phase 7d task 3

Added 2026-08-05. Everything transcribed above went in unchanged and all 60,000 `$00`/`$02`/`$42` vectors
passed on the first run, so nothing here is a correction to the table. Two things are recorded because they
are **decisions**, not findings, and §14.9's gap 2 required whichever was taken to be written down.

**1. There is no NMI hijack on the 65816, and this is a choice.** Gap 2 stands open: no source mentions an
`NMI` asserted mid-`BRK`, and no vector can settle it. The NMOS cores in this repository implement one —
`MicroOp.PushPBrk` lets a latched `NMI` steal `BRK`'s vector on the P-push cycle, and `HijackTests.cs`
covers it — and it was **not** carried over. The reasoning, stated so a later reader can overturn it with a
source rather than by preference: the general 6502-family rule is that an interrupt is recognised at an
instruction boundary, and that rule alone produces no hijack; the NMOS anomaly is an *exception* to it,
attributed in this repository's own `PushPBrk` comment to that die's `~VEC`/`pipe~VEC`/1578/1368 transistor
chain, and no source places that chain on this part. The 65C02 removed the anomaly outright (see
`PushPBrkCmos`), and the 65816's interrupt logic descends from the 65C02's. Adding the exception needs a
source; declining to needs none. Consequence, unit-tested in `W65C816InterruptTests`: `BRK` reads its own
vector and the pending `NMI` is taken afterwards.

**2. The recognition blackout is not extended to the 65816's vector-high cycle either, same reasoning.**
`Cpu.Tick` forces `_intPoll` false on `MicroOp.VectorHi`, which is what guarantees the five 8-bit cores
execute at least one handler instruction before another interrupt is serviced. `MicroOp.VectorHi816` is
deliberately absent from that test. This is the same class of unsourced NMOS die behaviour as the hijack —
the same node 1368 — and it is left out for the same reason. **Consequence, and it is visible:** an `NMI`
latched during a `BRK`, `COP`, `IRQ` or `NMI` sequence is serviced at the very next instruction boundary,
so the first handler instruction does not run. Bounded and non-looping: `IRQ` cannot re-enter because `I`
is set two cycles before the boundary, and the `NMI` latch is consumed on dispatch. If a source is ever
found, the fix is one clause in `Cpu.Tick`.

**This is not a claim that the part has no blackout anywhere.** Reset shares the 8-bit
`MicroOp.VectorLo`/`VectorHi` pair on every variant including the 65816 (`MicroOpTable`'s
`ResetEntry`, both spellings agreeing cycle for cycle) — deliberately, per that table's own
comment, because it is correct on every variant. `Cpu.Tick`'s `if (micro == MicroOp.VectorHi)
_intPoll = false;` therefore still fires for this part after a reset, exactly as it does for the
5 eight-bit cores. So: no blackout after `BRK`/`COP`/`IRQ`/`NMI` (this section's finding above),
but a blackout after reset (an artifact of the shared reset micro-ops, not a separate decision).
Observed, not re-derived from a source.

**3. One gap closed as a side effect.** `Cpu.Tick` carried a "KNOWN GAP, 65816 only" comment since phase
7b: the pins of the interrupt-entry cycle that `FetchOpcode` performs in place of an opcode fetch were
unknown, because §9 has no interrupt rows. Row 22a's cycle 1 supplies them — `VDA=1 VPA=1` at `PBR,PC`,
which is exactly the `OpcodeFetchPins` that were already there. The datasheet's `IO` in that row's data-bus
column means the byte is discarded, not that the address is invalid. Not vector-covered (gap 1); it rests
on the row.

### 14.3 `MVN` and `MVP` — from the datasheet, and from `$54.n.json` read directly

Transcribed from Table 5-7 rows 9a and 9b (p. 38). `DBA` is the datasheet's destination bank address and
`SBA` its source bank address. `MLB` and `VPB` are `1` throughout and are dropped.

```
9a. Block Move Negative (backward) xyc            MVN   1 opcode, 3 bytes, 7 cycles
9b. Block Move Positive (forward) xyc             MVP   1 opcode, 3 bytes, 7 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode        RWB=1
  2              VDA=0 VPA=1   PBR,PC+1     DBA           RWB=1
  3              VDA=0 VPA=1   PBR,PC+2     SBA           RWB=1
  4              VDA=1 VPA=0   SBA,X        SRC Data      RWB=1      (SBA,X-1, X-2 … for MVP)
  5              VDA=1 VPA=0   DBA,Y        Dest Data     RWB=0      (DBA,Y-1, Y-2 … for MVP)
  6              VDA=0 VPA=0   DBA,Y        IO            RWB=1
  7              VDA=0 VPA=0   DBA,Y        IO            RWB=1
  ... then either cycle 1 again at PBR,PC (another iteration)
      or        VDA=1 VPA=1   PBR,PC+3     Next OpCode    RWB=1
```

The row's own annotations state the register roles: `x=Source Address`, `y=Destination`,
`c=# of bytes to move-1`, `x,y Increment` for `MVN` and `x,y Decrement` for `MVP`.

**The per-byte cycle count is 7, and the two internal cycles are at the destination address.** Both `IO`
cycles drive `DBA,Y` — the address just written, not `PBR,PC+1`. That is unlike every internal cycle §9,
§13 and §14.1 record, all of which drive a program-counter address, and it is the one place in this phase
where an internal cycle's address is a data address.

**The count is in the accumulator, is bytes-minus-one, and is sixteen bits wide regardless of `m`.** Clark
§6.6: *"The (16-bit) accumulator contains the number of bytes to move minus 1, the X register contains the
16-bit source address, and the Y register contains the 16-bit destination address."* And: *"MVN and MVP
decrement the (16-bit) accumulator and increment (for MVN) or decrement (for MVP) both X and Y each time a
byte is moved; this means that the accumulator will be $FFFF after an MVN or MVP."* Clark writes "(16-bit)"
three times in two sentences and never qualifies it by `m`; measured confirmation below, including in
emulation mode where `m` is forced to 1.

**`DBR` is written from the operand, and it is the *first* operand byte.** Datasheet §7.18, verbatim and
entire:

> The MVN and MVP instructions change the Data Bank Register to the value of the second byte of the
> instruction (destination bank address).

"second byte of the instruction" is the byte at `PC+1` — the opcode is the first — which is Table 5-7's
`DBA` and Clark §5.19's `$TT` in `$OP $TT $SS`. Clark §6.6 states the same from the register side: *"the
DBR is overwritten by MVN and MVP; after an MVN or MVP, the destination bank is stored in the DBR."*
**The destination bank byte comes first in the instruction stream and the source bank byte second**, which
is the reverse of the operand order in the usual assembler syntax `MVN #source,#dest` — Clark's §5.19
example is `MVN #$12,#$34` moving *from* bank `$12` *to* bank `$34`, assembled as `$54 $34 $12`. The plan's
opcode map asserts this; it is hereby confirmed against two sources and a vector.

**`PC` is rewound by 3 — or rather, it is not advanced.** Clark §6.6: *"the program counter will be the
address of the next instruction (i.e. the instruction after the MVN or MVP) if the accumulator is $FFFF,
and the program counter will be the address of the the MVN or MVP if the accumulator is not $FFFF (i.e. the
instruction jumps to itself if the accumulator is not $FFFF)."* Table 5-7 shows the same thing as two
alternative next rows: `PBR,PC` for another iteration, `PBR,PC+3` for the exit.

**Both addresses wrap at the bank boundary.** Clark §5.1.2: *"source,destination addressing (i.e. the MVN
and MVP instructions) wraps at both the source and destination bank boundaries"*, with the §5.19 worked
example `X = $FFFE`, `Y = $FFFF`, `MVN #$12,#$34` moving `$12FFFE → $34FFFF`, `$12FFFF → $340000`,
`$120000 → $340001`.

**`MVN` and `MVP` are the only instructions that can be interrupted mid-instruction**, and only on a
seven-cycle boundary — Clark §6.6: *"MVN and MVP can be interrupted by IRQ and NMI before the move is
complete (unlike every other instruction, which must finish before an IRQ or NMI is serviced); however,
they can only be interrupted every seventh cycle."* Not exercised by any vector; recorded because it is the
reason the opcode is re-fetched every iteration rather than looped internally.

#### Measured, not cited: what one `MVN` vector file actually contains

Added 2026-08-05 by phase 7d task 1, by reading `54.n.json` (37,853,698 bytes, 10,000 vectors) and
`44.n.json` (37,836,551 bytes, 10,000 vectors) directly.

> **A vector holds the whole move, not one iteration — and is truncated at 100 cycles when the move is
> longer than that.** `54 n` cycle-array lengths: **9,999 vectors of exactly 100 cycles, and one of 98**.
> `44 n`: 9,997 of 100, and one each of 63, 28 and 14. Every length that is not 100 is an exact multiple of
> 7. The 100-cycle vectors are cut off mid-instruction: `54 n 1` starts with `A = $EF9B` (61,340 bytes to
> move) and its final state has `A = $EF8D`, `PC = $1A9F` and 14 bytes written — 14 complete iterations
> (98 cycles) plus the first two cycles of the fifteenth. **A 65816 core cannot be certified against these
> two opcodes with the `AtInstructionBoundary` assertion `Harte816Tests` makes**, because the instruction
> genuinely has not finished; see the gaps below.
>
> **The seven-cycle iteration, verbatim from `54 n 1`, cycles 1–8.** Initial `PBR = $06`, `PC = $1A9D`,
> `X = $0018`, `Y = $0021`, `A = $EF9B`, `DBR = $E4`, `P = $FF` (so `m = 1` *and* `x = 1`), `e = 0`:
>
> ```
>  1  [$061A9D, $54, "dp-r-mx-"]   opcode fetch
>  2  [$061A9E, $3D, "-p-r-mx-"]   DBA — destination bank
>  3  [$061A9F, $6D, "-p-r-mx-"]   SBA — source bank
>  4  [$6D0018, $56, "d--r-mx-"]   read  SBA,X
>  5  [$3D0021, $56, "d--w-mx-"]   write DBA,Y
>  6  [$3D0021, null, "---r-mx-"]  IO at the destination address
>  7  [$3D0021, null, "---r-mx-"]  IO at the destination address
>  8  [$061A9D, $54, "dp-r-mx-"]   opcode fetch again — PC was rewound
> ```
>
> Every cell of Table 5-7's rows 9a/9b is reproduced: the operand order (`$3D` at `PC+1` becomes the final
> `DBR`, `$6D` at `PC+2` is the bank of the read), the addresses `SBA,X` and `DBA,Y`, and both `IO` cycles
> at the destination. Cycle 8 re-fetching the opcode at `$061A9D` is the rewind, observed rather than
> inferred.
>
> **The count is a full 16-bit decrement even when `m = 1`.** `54 n 1` has `m = 1` and `A` goes
> `$EF9B → $EF8D` — a 16-bit value, `-14`, for 14 bytes moved. The `B` accumulator is clobbered. Same in
> emulation mode, where `m` is forced to 1: `54 e 9990` has `A = $0000 → $FFFF` for one byte moved.
>
> **A complete instruction ends at `PC + 3` with `A = $FFFF`, and costs exactly `7 × (C+1)` cycles.**
> `44 n 3752`: `A = $0001`, 14 cycles, `PC $AE0F → $AE12`, `A → $FFFF`, `X $0068 → $0066`, `Y $0054 →
> $0052`, `DBR → $18` (the byte at `PC+1`). `44 n 2075`: `A = $0008`, 63 cycles = 9 × 7, `X` and `Y` each
> `-9`. `44 n 5490`: `A = $0003`, 28 cycles, `x = 0` so `X = $0EFE → $0EFA` and `Y = $4637 → $4633` as full
> 16-bit registers. **There is no trailing cycle beyond the last iteration's seventh** — 14, 28 and 63 are
> exact multiples of 7 with nothing left over.
>
> **`MVP` decrements and `MVN` increments, confirmed by direction of the addresses**, not only by the
> register deltas: `44 n 3752` reads `$AC0068` then `$AC0067`; `54 n 1` reads `$6D0018` then `$6D0019`.
>
> **The smallest complete vector, `54 e 9990`, in full** — useful as a one-instruction regression case:
> `A = $0000`, `X = $00A6`, `Y = $0005`, `PBR = $BC`, `PC = $9514`, `DBR = $33`, `e = 1`. Seven cycles:
> `[$BC9514,$54]`, `[$BC9515,$B7]`, `[$BC9516,$8F]`, `[$8F00A6,$8B]`, `[$B70005,$8B]`, two `IO` at
> `$B70005`. Final: `PC = $9517`, `A = $FFFF`, `X = $00A7`, `Y = $0006`, `DBR = $B7`.

#### Measured, not cited: the index registers move at the operative width

Added 2026-08-05 by phase 7d task 4. **No source states this.** Clark §6.6 calls `X` and `Y` "the 16-bit
source address" and "the 16-bit destination address" and never mentions `x`; Table 5-7's annotations say
only "x,y Increment" and "x,y Decrement". The files settle it:

> **With `x = 1` both index registers are eight bits and wrap inside the low byte.** `54 n 63`
> (`P = $78`, so `x = 1`) starts at `X = $F3` and its source reads run `…$B300FE`, `$B300FF`,
> `$B30000` — wrapping to `$00`, not carrying to `$0100` — and its final `X` is `$01` after fourteen
> increments. `54 n 81` and `54 n 123` repeat it from `$F5` and `$F9`. `44 n 23` is the same in the other
> direction: `X = $0D` down through `$F80000` and out to a final `$FF`.
>
> **With `x = 0` both are sixteen bits and wrap at the bank boundary**, which is Clark §5.1.2's rule
> observed rather than cited: `54 n 4275` (`P = $E2`, `x = 0`) writes `$C9FFFD`, `$C9FFFE`, `$C9FFFF`,
> `$C90000` and ends at `Y = $000B`; `44 n 4731` reads `$000000` then `$00FFFF` and ends at
> `X = $FFF6`. **Neither address ever leaves its bank.**
>
> **What is NOT settled: whether a nonzero index high byte survives with `x = 1`.** No vector in either
> file has `x = 1` with `XH` or `YH` nonzero — 0 of 40,000 — so nothing arbitrates between zeroing the
> high byte and preserving it. The implementation zeroes it, through the same `Cpu.X8`/`Cpu.Y8` setters
> `INX`/`DEX` already use, on the same reasoning recorded there: hardware holds `XH`/`YH` at `$00` for as
> long as `x` is set (§7), so there is nothing to preserve on the part.
>
> **The status register is never written.** `P` is identical in the initial and final state of all 20,000
> native vectors, so no block move sets `N`, `Z` or anything else.

#### What the exemption actually costs the harness — two changes, not one

Added 2026-08-05 by phase 7d task 4, correcting this section's own earlier implication (and gap 12's) that
skipping `AtInstructionBoundary` was the whole of it.

> Skipping the assertion is necessary and not sufficient. `Harte816Tests` builds **one core per file** and
> reloads only `cpu.State` between vectors — which assigns the architectural registers and **not the
> micro-op sequence position**. Every other opcode ends its vector at an instruction boundary, so that has
> always been safe. A truncated block-move vector does not: it leaves the core part-way through its
> sequence, and the next vector then *resumes the previous vector's half-finished move* instead of
> fetching its own opcode.
>
> Measured: with only the assertion skipped, `54 n 1` passes and `54 n 2` fails — five cycles of vector
> 1's fifteenth iteration run against vector 2's registers, `PC` is rewound by 3 from the wrong place, and
> the core lands on `$00` in bank 0 and executes twelve `BRK`s inside the remaining budget. The reported
> divergence (`PC:0002 … S:FE7D … PBR:00`) names none of that; it looks like an interrupt storm.
>
> The fix is to return the core to a boundary before each vector — `if (!cpu.AtInstructionBoundary)`,
> rebuild it — which is a no-op for the other 457 files. **Any future opcode whose vectors are truncated
> mid-instruction needs both changes**, and the second is the one that is invisible until a second vector
> in the same file runs.

### 14.4 `WAI` and `STP` — from the datasheet, and from `$CB.n.json` and `$DB.n.json` read directly

Transcribed from Table 5-7 rows 19c and 19d (p. 40). Both rows print **3 cycles**, and both print, below
the three, a restart sequence labelled with pin conditions (`RESB=1`/`RESB=0` for `STP`, `RDY` and
`IRQB, NMIB` for `WAI`) rather than with cycle numbers in sequence — §13.2 already noted that rows 19c and
19d print `1c`/`1b`/`1a` above cycle `1` and that these are *successive states of a stop-and-restart
sequence*, not width-conditional halves.

```
19c. Stop the Clock
STP                                               1 opcode, 1 byte, 3 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
  3              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
    then, gated on RESB:  1c / 1b / 1a at PBR,PC+1 with "RES (BRK)", then 1 at PBR,PC+1 with "BEGIN"
    (See 22a. Stack Hardware Interrupt)

19d. Wait for Interrupt
WAI                                               1 opcode, 1 byte, 3 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2   (9)        VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
  3              VDA=0 VPA=0   PBR,PC+1     IO          RWB=1
    then, gated on IRQB/NMIB:  1 at PBR,PC+1 with "IRQ(BRK)"
```

Clark §6.9 agrees on both: `DB 1 3 imp STP` and `CB 1 3 imp WAI`. (Clark's `DB` is right; the datasheet's
Table 5-5 prints `D8` for `STP`, which is `CLD` — see §3.5.) Clark's prose on what happens *after* the three
cycles is the fullest statement in any source:

> STP stops the clock input of the 65C816, effectively shutting down the 65C816 until a hardware reset
> (interrupt) occurs.
>
> WAI puts the 65C816 into a low power sleep state until a hardware interrupt occurs. … When WAI is used,
> once its third cycle is complete, the 65C816 will wait for the interrupt and can respond to it without any
> additional delay whenever it occurs.
>
> … WAI when the i flag is 1 is a special case; specifically, when an IRQ occurs (after the WAI
> instruction), the 65C816 will continue with the next instruction rather than jumping to the interrupt
> vector.

and datasheet §7.13/§7.14 add the pin behaviour (*"The WAI instruction pulls RDY low"*, *"The STP
instruction disables the PHI2 clock to all internal circuitry"*).

#### Measured, not cited: what the `WAI` and `STP` vector files contain, and what they do not model

Added 2026-08-05 by phase 7d task 1, by reading `cb.n.json`, `cb.e.json`, `db.n.json` and `db.e.json`
directly — 4,413,574 to 4,415,743 bytes each, 10,000 vectors each, 40,000 in total. **This is the answer
that decides how task 5's two opcodes can be certified, and the brief is right that it had to be known
before that task was dispatched.**

> **Every one of the 40,000 vectors is exactly 4 cycles long**, and the fourth cycle is
> **`[null, null, "--------"]`** — no address, no value, and all eight pin characters `-`, including the
> `e`, `m` and `x` slots, which are `-` even in the emulation-mode files where `e` is 1. The first three
> cycles are the datasheet's three: an opcode fetch at `PBR,PC` and two internal cycles at `PBR,PC+1`.
>
> `cb n 1` in full — initial `PBR = $B0`, `PC = $4621`, `e = 0`, `P = $D7`:
>
> ```
>  1  [$B04621, $CB, "dp-r--x-"]
>  2  [$B04622, null, "---r--x-"]
>  3  [$B04622, null, "---r--x-"]
>  4  [null,    null, "--------"]
> ```
>
> `db n 1`, `cb e 1` and `db e 1` are the same shape, differing only in the opcode byte and in the `e`/`m`/`x`
> characters of cycles 1–3. **`PC` advances by exactly 1** in all 40,000: `cb n 1` ends at `$4622`.
> No register other than `PC` changes, except that the emulation-mode files show `SH` forced to `$01`
> (`cb e 1`: `S = $91C8 → $01C8`), which is the §11 invariant and not something `WAI` or `STP` did.
>
> **The vector set does not model the hold at all.** There is no waiting, no wake-up, no reset and no
> interrupt in any of the 40,000 vectors; the fourth entry is a sentinel marking "and then the processor
> stopped", carrying no address, no data and no pin state. Nothing in these files distinguishes `WAI` from
> `STP` beyond the opcode byte — the two files are byte-for-byte the same shape.
>
> **What this means for task 5.** The three *executed* cycles of both opcodes are fully specified by the
> vectors and can be certified against them, provided the harness is taught what a `[null, null,
> "--------"]` cycle is; it currently would compare an address against `null` and would also fail
> `Harte816Tests`' `AtInstructionBoundary` assertion, since a halted core is not at an instruction boundary.
> **Everything that makes `WAI` and `STP` different from a three-cycle `NOP` — the hold, the wake on
> `IRQB`/`NMIB`, the `i`-flag special case, `STP`'s reset-only exit — is not in the vectors and can only be
> covered by unit tests.**

#### How task 5 resolved it, 2026-08-05

The sentinel needed **no cycle kind of its own**. `[null, null, "--------"]` says the processor drove no
address and performed no access, so the core models the hold as a micro-op that performs no bus access at
all — `MicroOp.WaiHold816` and `MicroOp.StpHold816` — and `AssertCycles` compares the vector's *access*
entries, of which there are three, against the log, of which there are three. The sentinel becomes an
assertion rather than an exemption: a hold that drove any address at all logs a fourth access and fails.
Probed by building exactly that halt — `cb n 1: expected 3 bus cycles of the vector's 4, got 4`.

All 40,000 pass unchanged otherwise: cycle 1 is the opcode fetch, cycles 2 and 3 are `ImpliedInternal816`
— row 19b's `IO` at `PBR,PC+1`, the cycle `XBA` already uses — and `PC` ends one past the opcode.

The four unmodelled rules were covered by seventeen `WaiStpTests` cases across both modes. That coverage
is not decorative: a halt that ends the instruction and merely sets a flag **passes all 40,000 vectors**
and fails fourteen of those seventeen. The vectors cannot see a hold at all, in either direction.

### 14.5 The branches

Transcribed from Table 5-7 rows 20 and 21 (p. 41).

```
20. Relative r
BCC, BCS, BEQ, BMI, BNE, BPL, BRA, BVC, BVS       9 opcodes, 2 bytes, 2, 3 and 4 cycles
  1              VDA=1 VPA=1   PBR,PC          OpCode      RWB=1
  2              VDA=0 VPA=1   PBR,PC+1        Offset      RWB=1
  2a  (5)        VDA=0 VPA=0   PBR,PC+1        IO          RWB=1   <- branch taken
  2b  (6)        VDA=0 VPA=0   PBR,PC+1        IO          RWB=1   <- taken across a page, e = 1 only
  1              VDA=1 VPA=1   PBR,PC+Offset   OpCode      RWB=1

21. Relative Long rl
BRL                                               1 opcode, 3 bytes, 4 cycles
  1              VDA=1 VPA=1   PBR,PC          OpCode        RWB=1
  2              VDA=0 VPA=1   PBR,PC+1        Offset Low    RWB=1
  3              VDA=0 VPA=1   PBR,PC+2        Offset High   RWB=1
  4              VDA=0 VPA=0   PBR,PC+2        IO            RWB=1
  1              VDA=1 VPA=1   PBR,PC+Offset   OpCode        RWB=1
```

**1. Is the taken-branch page-cross cycle emulation-mode-only? — Yes.** Note 6, verbatim (p. 43):

> 6. Add 1 cycle if branch is taken across page boundaries in 6502 emulation mode (E=1).

with Note 5 alongside it, which gates the other conditional cycle:

> 5. Add 1 cycle if branch is taken.

Clark §6.2.1.1 states the same thing as a formula, and the shape of the formula is the corroboration —
`2+t+t*e*p` for the eight conditional branches and `3+e*p` for `BRA`. The page-cross term is multiplied by
`e`, exactly as §3.2's `x*p` is multiplied by `x`. **In native mode a taken branch is flat 3 cycles no
matter where it lands.** This is a real behavioural difference from all five 8-bit cores in this
repository, and it is stated independently by both primary sources.

Clark also pins what "page cross" is measured against, which the datasheet does not:

> a page boundary is crossed when the branch destination is on a different page than the next instruction
> (again, the instruction after the branch instruction). This means that `LABEL BRA LABEL+2 ; 3 cycles`
> always takes 3 cycles, no matter where the BRA instruction is located in memory, since the branch
> destination is the next instruction, i.e. they are the same address, and thus on the same page.

**2. `BRL`'s length, cycle count and conditional cycles.** `$82`, **3 bytes, flat 4 cycles, no conditional
cycle of any kind.** Clark §6.2.1.2: `82 3 4 rel16`. Row 21's header says `1 OpCode, 3 bytes, 4 cycles` —
one figure, not a list — and no note marker appears anywhere on the row. Both sources independently.
`BRL` is *always* four cycles: there is no not-taken case (*"BRA unconditionally branches"* applies to
`BRL` too, being its 16-bit form) and no page-cross penalty in either mode.

**3. Does a taken branch's displacement wrap within the bank, leaving `PBR` unchanged? — Yes, for both
`rel8` and `rel16`.** Clark §5.1.2 lists *"The Program Counter (i.e. the PC register); again, this means
branches wrap at the bank K boundary"* among the things confined to bank K, and §4 gives worked values for
each width:

> It's also worth noting that branches (both forward and backward) wrap at bank boundaries as well. A BCC
> $FFE0 instruction at $130020 will branch to $13FFC0 rather than $12FFC0. Likewise, a BRL $2000 at $13E000
> will branch to $132000 rather than $142000.

The destination formulas are §5.18's: `K : PC+2+$LL` for `rel8` and `K : PC+3+$HHLL` for `rel16` — the base
in both cases is the address of the *next* instruction, and `K` is carried through unchanged. Table 5-7
agrees by writing the destination as `PBR,PC+Offset` on both rows — the same `PBR`, never a `PBR+1`.

**Measured (phase 7d task 6), across all 200,000 vectors for the ten opcodes.** All three answers hold and
nothing in rows 20 and 21 needed correcting. Three things the transcription alone does not settle:

1. **The page-cross cycle is emulation-mode-only, confirmed by the cycle-length histograms.** All nine
   `*.n` files contain two- and three-cycle vectors and **no four-cycle vector at all** (`80.n` is 10,000
   of exactly three); all nine `*.e` files contain four-cycle ones — 1,180 to 1,314 per conditional file
   and 2,500 for `80.e`. `82.e` and `82.n` are 10,000 of exactly four each, so `BRL` has no conditional
   cycle in either mode, as answer 2 says.
2. **Row 20's address column means the offset byte's own address, not the byte after the branch.** Cycles
   2a and 2b both drive `PBR,PC+1` literally — the same address cycle 2 fetched the displacement from —
   and the second drives it again *after* `PC` has moved. `10 n 2` reads `$BF3750` on cycles 2 and 3;
   `10 e 7` reads `$E2195E` on cycles 2, 3 and 4. This is a real difference from the five 8-bit cores in
   this repository, whose taken-branch cycle rereads the byte *after* the branch (`PC+2`) and whose
   page-cross cycle drives the *un-fixed* new `PC`. Neither of those addresses appears in any 65816 branch
   vector, so the 65816 needs branch micro-ops of its own — and, because the un-fixed `PC` is never driven
   on this part, it can do the whole displacement add in one cycle rather than half of it.
   Row 21's cycle 4 is the same rule one byte along: `PBR,PC+2`, the high displacement byte's own address.
3. **The bank boundary is covered by the vectors, not a gap.** 5,078 of the 200,000 — 101 `rel8` and 4,977
   `rel16` — have `base + displacement` outside `$0000`-`$FFFF`, and every one records the wrapped `PC`
   with `PBR` unchanged. `rel16` wraps in about a quarter of its vectors, since a 16-bit displacement from
   an arbitrary `PC` usually can. The natural assumption — that a ±128 displacement from a random `PC`
   would never reach `$xxFFFF` in ten thousand tries — is wrong for `rel8` too, at roughly 1 in 1,800.
   **The first count of this came out 5,076 (99 `rel8`), two short — an easy way to undercount by
   exactly the cases the measurement exists to find.** The counting script used an unmasked `PC + 2` as
   `base`, so the two `rel8` vectors whose opcode sits at `$xxFFFE`/`$xxFFFF` — where the next-instruction
   address has *already* wrapped the bank before the displacement is even added — scored as in-range and
   were silently dropped: `pc=$FFFF disp=-106 → final.pc=$FF97` and `pc=$FFFE disp=-71 → final.pc=$FFB9`.
   Mask the base to 16 bits before adding the displacement, not after.

### 14.6 The jumps, the calls and the returns

Transcribed from Table 5-7 rows 1b, 1c, 2a, 2b, 3a, 3b, 4b, 4c (pp. 36–37) and 22g, 22h, 22i (p. 42).
`MLB` and `VPB` are `1` throughout all eleven rows.

```
1b.  JMP abs ($4C)                                3 bytes, 3 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2   VDA=0 VPA=1   PBR,PC+1   New PCL
  3   VDA=0 VPA=1   PBR,PC+2     New PCH    |  then OpCode at PBR,New PC

4b.  JMP long ($5C)                               4 bytes, 4 cycles
  1..3 as 1b, then
  4   VDA=0 VPA=1   PBR,PC+3     New BR     |  then OpCode at New PBR,PC

3b.  JMP (abs) ($6C)                              3 bytes, 5 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2   VDA=0 VPA=1   PBR,PC+1   AAL
  3   VDA=0 VPA=1   PBR,PC+2     AAH        |  4   VDA=1 VPA=0   0,AA       New PCL
  5   VDA=1 VPA=0   0,AA+1       New PCH    |  then OpCode at PBR,New PC

3a.  JML [abs] ($DC)                              3 bytes, 6 cycles
  1..3 as 3b, then
  4   VDA=1 VPA=0   0,AA         New PCL    |  5   VDA=1 VPA=0   0,AA+1     New PCH
  6   VDA=1 VPA=0   0,AA+2       New PBR    |  then OpCode at NEW PBR,PC

2a.  JMP (abs,X) ($7C)                            3 bytes, 6 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2   VDA=0 VPA=1   PBR,PC+1   AAL
  3   VDA=0 VPA=1   PBR,PC+2     AAH        |  4   VDA=0 VPA=0   PBR,PC+2   IO
  5   VDA=0 VPA=1   PBR,AA+X     New PCL    |  6   VDA=0 VPA=1   PBR,AA+X+1 New PCH
  then OpCode at PBR,NEW PC

1c.  JSR abs ($20)      "(different order from N6502)"      3 bytes, 6 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     RWB=1
  2   VDA=0 VPA=1   PBR,PC+1     New PCL    RWB=1
  3   VDA=0 VPA=1   PBR,PC+2     New PCH    RWB=1
  4   VDA=0 VPA=0   PBR,PC+2     IO         RWB=1
  5   VDA=1 VPA=0   0,S          PCH        RWB=0
  6   VDA=1 VPA=0   0,S-1        PCL        RWB=0
  then Next OpCode at PBR,NEWPC

2b.  JSR (abs,X) ($FC)                            3 bytes, 8 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     RWB=1
  2   VDA=0 VPA=1   PBR,PC+1     AAL        RWB=1
  3   VDA=1 VPA=0   0,S          PCH        RWB=0   <- pushes before AAH is fetched
  4   VDA=1 VPA=0   0,S-1        PCL        RWB=0
  5   VDA=0 VPA=1   PBR,PC+2     AAH        RWB=1
  6   VDA=0 VPA=0   PBR,PC+2     IO         RWB=1
  7   VDA=0 VPA=1   PBR,AA+X     New PCL    RWB=1
  8   VDA=0 VPA=1   PBR,AA+X+1   New PCH    RWB=1
  then Next OpCode at PBR,NEW PC

4c.  JSL long ($22)                               4 bytes, 8 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     RWB=1
  2   VDA=0 VPA=1   PBR,PC+1     New PCL    RWB=1
  3   VDA=0 VPA=1   PBR,PC+2     New PCH    RWB=1
  4   VDA=1 VPA=0   0,S          PBR        RWB=0   <- the old PBR, pushed before it is replaced
  5   VDA=0 VPA=0   0,S          IO         RWB=1   <- internal cycle at a STACK address
  6   VDA=0 VPA=1   PBR,PC+3     New PBR    RWB=1
  7   VDA=1 VPA=0   0,S-1        PCH        RWB=0
  8   VDA=1 VPA=0   0,S-2        PCL        RWB=0
  then Next OpCode at New PBR,PC

22g. RTI ($40)   "(different order from N6502)"   1 byte, 6 and 7 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2        VDA=0 VPA=0   PBR,PC+1   IO
  3   (3) VDA=0 VPA=0   PBR,PC+1  IO        |  4        VDA=1 VPA=0   0,S+1      P
  5   VDA=1 VPA=0   0,S+2        New PCL    |  6        VDA=1 VPA=0   0,S+3      New PCH
  7   (7) VDA=1 VPA=0   0,S+4     PBR       <- omitted when e = 1
  then Next OpCode at PBR,New PC

22h. RTS ($60)                                    1 byte, 6 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2   VDA=0 VPA=0   PBR,PC+1   IO
  3   VDA=0 VPA=0   PBR,PC+1     IO         |  4   VDA=1 VPA=0   0,S+1      PCL
  5   VDA=1 VPA=0   0,S+2        PCH        |  6   VDA=0 VPA=0   0,S+2      IO
  then OpCode at PBR,PC

22i. RTL ($6B)                                    1 byte, 6 cycles
  1   VDA=1 VPA=1   PBR,PC       OpCode     |  2   VDA=0 VPA=0   PBR,PC+1   IO
  3   VDA=0 VPA=0   PBR,PC+1     IO         |  4   VDA=1 VPA=0   0,S+1      New PCL
  5   VDA=1 VPA=0   0,S+2        New PCH    |  6   VDA=1 VPA=0   0,S+3      New PBR
  then Next OpCode at NEW PBR,PC
```

**1. The bank each jump form reads its pointer from.** Clark §5.1.2 and §5.4/§5.5, corroborated cell by cell
by the address expressions above:

| Form | Pointer bank | Table 5-7 | Clark |
| --- | --- | --- | --- |
| `JMP (abs)` `$6C` | **bank 0** | `0,AA` / `0,AA+1` | §5.4, `0 \| $HHLL` |
| `JML [abs]` `$DC` | **bank 0**, three bytes | `0,AA` / `0,AA+1` / `0,AA+2` | §5.4, `0 \| $HHLL(+1,+2)` |
| `JMP (abs,X)` `$7C` | **bank K** (`PBR`) | `PBR,AA+X` / `PBR,AA+X+1` | §5.5, `K \| $HHLL+X` |
| `JSR (abs,X)` `$FC` | **bank K** (`PBR`) | `PBR,AA+X` / `PBR,AA+X+1` | §5.5, same mode |

Clark §5.1.2 states the rule behind the table: bank 0 is confined to *"[absolute] and (absolute) addressing
modes (JMP is the only instruction available for either)"*, bank K to *"(absolute,X) addressing mode (JMP
and JSR are the only instructions available for this addressing mode)"*. Note that `$6C` and `$DC` take
their pointer from **bank 0 regardless of `PBR`**, and that `JML [abs]`'s destination bank comes from the
pointer's own third byte, not from `PBR` — Table 5-7 row 3a's next-opcode row reads `NEW PBR,PC`.

**2. The 65816 does not reproduce the NMOS `JMP ($xxFF)` page-wrap bug.** Clark §5.4, verbatim:

> Note that on the 65C816, as on the 65C02, (absolute) addressing does not wrap at a page boundary, i.e. for
> a JMP ($12FF) the low byte of the destination address is taken from $12FF and the high byte of the
> destination address is taken from $1300. On the NMOS 6502, (absolute) addressing did wrap on a page
> boundary, which was unintentional (i.e. a bug); there, a JMP ($12FF) took the low byte of the destination
> address from $12FF but took the high byte of the destination address from $1200 (rather than $1300).

Corroborated by Table 5-7 row 3b writing the second pointer address as `0,AA+1` — a 16-bit increment inside
bank 0, with no page qualification — and by Clark's own §5.4 worked example, which shows what *does* wrap:
*"If the K register is $12 and $000000 contains $34, $00FFFF contains $56, then JMP ($FFFF) jumps to
$123456"*. The pointer wraps at the **bank 0** boundary, `$00FFFF → $000000`, never at a page boundary.
`JMP (abs,X)` wraps at the bank K boundary in the same way — §5.5's example, `X = $000A`, `JMP ($FFFE,X)`
reads `$120008`, i.e. `$FFFE + $000A` truncated to 16 bits.

**3. `JSR (abs,X)`'s cycle order.** Row 2b above, and it is the datasheet that states it: the two pushes are
cycles **3 and 4**, before cycle 5 fetches `AAH`. **`JSR (abs,X)` pushes the return address after reading
only the low byte of its operand.** No other instruction in this phase interleaves a push into the middle
of operand fetching. Clark gives the cycle count (`FC 3 8`) but says nothing about the order — **Clark is
silent on the ordering**, and the datasheet row is the only source for it.

**4. What `JSR` and `JSL` push, and whether it is the last byte of the instruction or the next one.**
Clark §6.2.2.1, verbatim, for both:

> JSL pushes the K register (i.e. program bank register), then pushes the 16-bit address (high byte first,
> then low byte) of the JSL instruction plus 3 (one less than the address of the next instruction), then
> jumps to the address specified by the operand. Thus, if the JSL instruction (i.e. the $22 opcode) is at
> $12FFFD, then the bytes pushed are (in order): $12, $00, and $00, rather than $13, $00, and $00.
>
> JSR pushes the 16-bit address (i.e. the program counter) of the JSR instruction plus 2 onto the stack, and
> jumps to an address within the current program bank. In other words, the address pushed is one less than
> the address of the next instruction. The high byte is pushed first, then the low byte is pushed.

**Both push the address of the *last byte* of the instruction, not the next one** — `+2` for the three-byte
`JSR`, `+3` for the four-byte `JSL` — which is why `RTS` and `RTL` increment what they pull. `JSL`'s pushed
`K` is the **old** `PBR`, pushed at cycle 4 before cycle 6 reads the new one; and Clark's `$12FFFD` example
pins the bank wrap: `$FFFD + 3 = $0000` within bank `$12`, and the pushed bank stays `$12`. Clark's `JSR`
example gives a second single-point check: `S = $01FF`, `JSR $ABCD` at `$123456` stores `$34` at `$0001FF`
and `$58` at `$0001FE`, then jumps to `$12ABCD`, leaving `S = $01FD`.

**5. How many bytes `RTS`, `RTL` and `RTI` pull, and which add one.** Clark §6.2.2.2 and §6.3.2:

| | Op | Native | Emulation | Adds 1? |
| --- | --- | --- | --- | --- |
| `RTS` | `$60` | 2 bytes (`PCL`, `PCH`) | 2 bytes | **Yes** |
| `RTL` | `$6B` | 3 bytes (`PCL`, `PCH`, `PBR`) | 3 bytes | **Yes**, to `PC` only |
| `RTI` | `$40` | 4 bytes (`P`, `PCL`, `PCH`, `PBR`) | 3 bytes (`P`, `PCL`, `PCH`) | **No** |

Verbatim: *"RTL … pulls the low byte, then the high byte of the program counter from the stack, then
increments the program counter, then pulls the K register"*; *"RTS … pulls the low byte, then the high byte
of the program counter from the stack, then increments the program counter"*; and *"Note that unlike RTS
(and RTL), the program counter is not incremented after it is pulled from the stack"* for `RTI`.

**`RTL` is three bytes in both modes** — Table 5-7 row 22i carries no note 7, its header says `6 cycles`
with one figure, and Clark gives a flat `6`. `RTL` is the one return whose byte count does not vary with
`e`. **`RTL`'s increment does not carry into `PBR`**: Clark, *"if $FF, $FF, and $12 are pulled from the
stack, the instruction at $120000 (rather than $130000) will be executed next"* — the `+1` wraps inside the
16-bit `PC`, and the pulled bank is used as-is. `RTS`'s sixth cycle is the increment, printed as an `IO` at
`0,S+2`; `RTL` has no such cycle because its sixth cycle is the `PBR` pull, which is why both are 6.

**6. Does `RTI` pull `PBR` in native mode, and does it pull `P` before or after the return address? —
Yes, and before.** Row 22g: cycle 4 is `P`, cycles 5 and 6 are `New PCL` and `New PCH`, cycle 7 is `PBR`
with note 7 on it. Clark §6.3.2: *"In native mode, the P register is pulled, then the 16-bit program
counter is pulled (low byte first, then high byte), then the K register … is pulled. In emulation mode, the
P register is pulled, then the 16-bit program counter is pulled."* The row's label *"(different order from
N6502)"* is the datasheet's own and refers to this row; note that the *pull order* `P`-then-`PC` is in fact
the same as the NMOS 6502's, so what is different is the trailing `PBR`, not the ordering of the first
three. Clark's example is a single-point check: `S = $01FB`, `e = 0`, `$0001FC..FF` = `$08 $12 $34 $56`
→ jumps to `$563412`, `S = $01FF`, `P = $08`.

**Amended 2026-08-06, phase 7d task 7 — three things this section got wrong or left open, all measured.**

**(a) Rows 2a and 2b's pointer-read pins are wrong — recorded as a source conflict at §3.6.** Both rows print `VDA=0 VPA=1` on `(abs,X)`'s two
pointer cycles — row 2a's 5 and 6, row 2b's 7 and 8 — which would make them program-stream reads. All
40,000 `$7C`/`$FC` vectors read **`d--r`**: `VDA` asserted, `VPA` clear, exactly like every other pointer
read on the part. The implementation follows the vectors. Failing evidence, from the run that caught it:
`7c e 1: cycle 4 expected [$72CB6A, $11, "d--remx-"], got [$72CB6A, $11, "-p-remx-"]`, and the same on
`7c n 1`, `fc e 1` and `fc n 1`. Rows 3a and 3b print `VDA=1 VPA=0` on the bank-0 pointer reads and *are*
right; the error is confined to the two indexed-indirect rows. Same shape as §13.1's row 16b and §14.1's
rows 22a/22c — a Table 5-7 cell the vectors overrule.

**(b) `JSR (abs,X)` wraps inside page one in emulation mode, and the old/new rule alone does not predict
it.** Clark §5.1.1 scopes the wrap to *"instructions and addressing modes … available on the 65C02"*, and
`(abs,X)` as a `JSR` mode is new to the 65816 — but §5.22 states the same rule over instructions alone
(*"for all interrupts and 'old' instructions"*), and that reading is the correct one. Measured across all
six stack-touching opcodes of this section, emulation mode: `$20` 0 out-of-page-one accesses, `$FC` 0,
`$40` 0, `$60` 0, `$22` 103, `$6B` 196. Discriminating vectors, each starting *at* the boundary rather
than merely near it: `20 e 1023` and `fc e 458` (`SL = $00`, second push at `$0001FF`), `22 e 61`
(`SL = $00`, pushes at `$000100`, `$0000FF`, `$0000FE`), `60 e 121` and `40 e 50` (`SL = $FF`, first pull
at `$000100`), `6b e 104` (`SL = $FF`, pulls at `$000200` onward). So `Cpu.StackWrapsInPageOne` gains
`Op.Jsr` — covering both `$20` and `$FC` — plus `Op.Rts` and `Op.Rti`, and `Op.Jsl`/`Op.Rtl` stay off.

**(c) `RTI`'s pulled `m` and `x` are not visible for the rest of the instruction.** Row 22g loads `P` at
cycle 4, and `RTI` is the only instruction on the part that restores `P` with cycles still to run —
`PLP`'s pull *is* its last cycle, which is why §14.1 never met this. The vectors show the two width bits
unchanged through the end of the instruction: `40 n 3` starts with `x = 1`, pulls a status byte whose `x`
is 0, and still prints `x` in the pin string of cycles 5, 6 and 7; `40 n 2` does the same for `m`. The
other six bits are unconstrained by the pin string and are applied at cycle 4, which keeps `I` restored in
time for this instruction's own last-cycle interrupt poll — the behaviour the five certified eight-bit
cores have. Nothing `RTI` does after cycle 4 is width-dependent, so where the two bits land is
unobservable except in the pin string. The final `P` is the pulled byte verbatim in native mode (all
10,000 `40.n` vectors, XOR `$00`) and differs from it by `$00`/`$10`/`$20`/`$30` and nothing else in
emulation (all 10,000 `40.e`) — the same two forced bits §14.1 measured for `PLP`, and the same defect-1
rule: no `~Flag.B` mask anywhere.

**Coverage notes for the eleven, measured from the files.** `$6C` has **36** vectors in `.e` and **35** in
`.n` whose pointer low byte is `$FF`, so the absence of the NMOS page-wrap bug is directly arbitrated. Two
of the `.e` vectors carry pointer `$FFFF` itself, so Clark §5.4's `$00FFFF → $000000` wrap is now
**measured, not merely cited**: `6c e 4469` and `6c e 6042` both read `$00FFFF`, then `$000000` — the same
two addresses as Clark's own worked example. `$DC` has one vector whose pointer crosses the boundary, at
its own third byte. `$7C` has 2,512 vectors per mode whose indexed pointer wraps inside bank K. `$6B` has
**no** vector that pulls `$FFFF`, so Clark §6.2.2.2's *"if $FF, $FF, and $12 are pulled from the stack, the
instruction at $120000 (rather than $130000) will be executed next"* — that `RTL`'s `+1` does not carry
into the pulled bank — has **zero vector coverage** and is certified by unit test alone.

### 14.7 `PEA`, `PEI`, `PER`

Transcribed from Table 5-7 rows 22d, 22e and 22f (p. 41).

```
22d. PEA ($F4)                                    1 opcode, 3 bytes, 5 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=1   PBR,PC+1     AAL         RWB=1
  3              VDA=0 VPA=1   PBR,PC+2     AAH         RWB=1
  4              VDA=1 VPA=0   0,S          AAH         RWB=0
  5              VDA=1 VPA=0   0,S-1        AAL         RWB=0

22e. PEI ($D4)                                    1 opcode, 2 bytes, 6 and 7 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode      RWB=1
  2              VDA=0 VPA=1   PBR,PC+1     DO          RWB=1
  2a  (2)        VDA=0 VPA=0   PBR,PC+1     IO          RWB=1   <- DL != $00
  3              VDA=1 VPA=0   0,D+DO       AAL         RWB=1
  4              VDA=1 VPA=0   0,D+DO+1     AAH         RWB=1
  5              VDA=1 VPA=0   0,S          AAH         RWB=0
  6              VDA=1 VPA=0   0,S-1        AAL         RWB=0

22f. PER ($62)                                    1 opcode, 3 bytes, 6 cycles
  1              VDA=1 VPA=1   PBR,PC       OpCode              RWB=1
  2              VDA=0 VPA=1   PBR,PC+1     Offset Low          RWB=1
  3              VDA=0 VPA=1   PBR,PC+2     Offset High         RWB=1
  4              VDA=0 VPA=0   PBR,PC+2     IO                  RWB=1
  5              VDA=1 VPA=0   0,S          PCH+Offset+Carry    RWB=0
  6              VDA=1 VPA=0   0,S-1        PCL+Offset          RWB=0
```

**Operand sources.** `PEA` takes a 16-bit immediate from `PC+1`/`PC+2` and pushes it without touching
memory — Clark §6.8.1: *"PEA #$1234 … simply pushes the value $1234, but does not access memory location
$1234 (in any bank)"*. `PEI` takes a one-byte direct-page offset and reads a 16-bit pointer from
**bank 0** at `0,D+DO` / `0,D+DO+1`, then pushes it — Clark: *"It pushes the same 16-bit value that
(assuming the m flag is 0) LDA $12 loads into the accumulator, rather that the value that LDA ($12) loads"*.
`PER` takes a 16-bit immediate displacement and pushes a computed address (see below).

**Cycle counts.** Clark §6.8.1: `F4 3 5 imm PEA`, `D4 2 6+w dir PEI`, `62 3 6 imm PER`. The row headers
reproduce all three exactly — `5 cycles`, `6 and 7 cycles`, `6 cycles` — and `PEI` is the **only one of the
44 opcodes in this phase that carries a `w` term** (`w = 1` when `DL != $00`, note 2). `PEA` and `PER` have
no conditional cycle at all. None of the three has an `m`, `x`, `e` or `p` term.

**All three push two bytes regardless of `m`.** Clark §6.8.1, verbatim: *"PEA, PEI, and PER all push a
16-bit value onto the stack"*, and, for the one that most invites doubt: *"Note, however, that PEI always
pushes a 16-bit value no matter what the value of the m flag (or, for that matter the x flag) is."* Table
5-7 corroborates from the bus side: rows 22d/22e/22f each print exactly two write cycles, neither of them
carrying note `(1)`. This is the difference from row 22c, where the high push *is* `(1)`-gated.

**`PER`'s displacement is relative to the address of the next instruction — `PC + 3`, wrapping inside the
program bank.** Clark §5.14, verbatim:

> Incidentally, PER is an unusual case. It can be considered 16-bit immediate data, like PEA. Unlike PEA
> (which pushes the immediate data onto the stack), PER adds the immediate data to the address of the next
> instruction. This is the same formula that relative16 addressing uses for the destination address, and
> thus PER is often documented as relative16 addressing rather than immediate addressing.

and §5.18's relative16 formula is `K : PC+3+$HHLL`, where Clark's `PC` throughout §5 means *the address of
the opcode*. `PER` is three bytes, so `PC+3` is the next instruction. The 16-bit sum wraps within the bank
— §5.1.2's "the Program Counter … is confined to bank K" — and only the 16-bit result is pushed; no bank
byte is pushed. Table 5-7's `PCH+Offset+Carry` / `PCL+Offset` is the same arithmetic written as two bytes
with an explicit carry, and it does **not** disambiguate which `PC` the datasheet means; Clark does, and is
the source relied on here.

**`PEI` and `PEA` never page-wrap in emulation mode.** Clark §5.1.1: *"since PEI is a "new" instruction,
PEI $FF does not wrap at a page boundary (either the direct page part, or the (pushing onto the) stack
part)"*. §7 already records this; it is repeated because `PEI` is the only direct-page instruction in this
phase and the exception applies to it specifically.

**Implemented, 2026-08-06, phase 7d task 8 — 60,000 of 60,000 vectors green on the first run, and with
them the 65816 reaches 256 of 256 opcodes.** Every statement above went in unaltered; nothing in this
section needed correcting against the vectors, which is why this block records no deviation. Three things
it does record:

- **The page-one measurement, extended to all three.** Counting the emulation-mode write cycles falling
  outside `$000100`-`$0001FF`, over 20,000 stack writes per opcode: `$F4` **51**, `$D4` **41**, `$62`
  **34**. §14.1's amended predicate lists none of them, and all 60,000 vectors agree. `$D4`'s 41 is new
  here — §14.1 and the task brief had measured only `$F4` and `$62`.
- **`PER`'s base is `PC+3` and the vectors cannot tell you so.** Every `$62` vector's pushed word is
  consistent with the implementation that produced it, so the only defence against an off-by-one is the
  source: Clark §5.14/§5.18, quoted above. `W65C816ControlFlowTests.Per_PushesTheNextInstructionsAddressPlusTheDisplacement`
  computes the expected value from the instruction's own address and the literal 3 for exactly that reason.
- **`PEI`'s low byte reuses `MicroOp.PtrReadLo816`, its high byte does not reuse `DpPtrReadHi`.** The
  high-byte micro-op of the indirect direct-page modes page-wraps in emulation mode, as an "old" mode
  must; `PEI`, being new, must not. `MicroOp.RmwRead` — the other obvious reuse for the low byte — would
  have asserted `MLB`, which row 22e does not print. Both are the shape trap tasks 6 and 7 hit.

### 14.8 A cycle formula for every one of the 44

Format and symbols are §5's and §13.3's, plus Clark's `t` (1 when a branch is taken). Every row carries two
independent sources — Clark's `CYCLES` column and Table 5-7's per-row cycle-count header — except the three
noted at the foot.

| Group | Op | Mnemonic | Cycles | Bytes | Clark § | Table 5-7 row |
| --- | --- | --- | --- | --- | --- | --- |
| Pushes | `$48` | `PHA` | `4-m` | 1 | 6.8.2 | 22c `3 and 4` |
| | `$08` | `PHP` | `3` | 1 | 6.8.3 | 22c |
| | `$DA` | `PHX` | `4-x` | 1 | 6.8.2 | 22c |
| | `$5A` | `PHY` | `4-x` | 1 | 6.8.2 | 22c |
| | `$8B` | `PHB` | `3` | 1 | 6.8.3 | 22c |
| | `$0B` | `PHD` | `4` | 1 | 6.8.3 | 22c |
| | `$4B` | `PHK` | `3` | 1 | 6.8.3 | 22c |
| Pulls | `$68` | `PLA` | `5-m` | 1 | 6.8.2 | 22b `4 and 5` |
| | `$28` | `PLP` | `4` | 1 | 6.8.3 | 22b |
| | `$FA` | `PLX` | `5-x` | 1 | 6.8.2 | 22b |
| | `$7A` | `PLY` | `5-x` | 1 | 6.8.2 | 22b |
| | `$AB` | `PLB` | `4` | 1 | 6.8.3 | 22b |
| | `$2B` | `PLD` | `5` | 1 | 6.8.3 | 22b |
| Interrupts | `$00` | `BRK` | `8-e` | 2 | 6.3.1 | 22j `7 and 8` |
| | `$02` | `COP` | `8-e` | 2 | 6.3.1 | 22j |
| | `$42` | `WDM` | `2` | 2 | 6.7 | **none** |
| Block move | `$54` | `MVN` | `7` per byte, `7*(C+1)` total | 3 | 6.6 | 9a `7 cycles` |
| | `$44` | `MVP` | `7` per byte, `7*(C+1)` total | 3 | 6.6 | 9b `7 cycles` |
| Halt | `$CB` | `WAI` | `3` | 1 | 6.9 | 19d `3 cycles` |
| | `$DB` | `STP` | `3` | 1 | 6.9 | 19c `3 cycles` |
| Branches | `$10` | `BPL` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 `2,3 and 4` |
| | `$30` | `BMI` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$50` | `BVC` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$70` | `BVS` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$90` | `BCC` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$B0` | `BCS` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$D0` | `BNE` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$F0` | `BEQ` | `2+t+t*e*p` | 2 | 6.2.1.1 | 20 |
| | `$80` | `BRA` | `3+e*p` | 2 | 6.2.1.1 | 20 |
| | `$82` | `BRL` | `4` | 3 | 6.2.1.2 | 21 `4 cycles` |
| Jumps | `$4C` | `JMP abs` | `3` | 3 | 6.2.2.1 | 1b `3 cycles` |
| | `$6C` | `JMP (abs)` | `5` | 3 | 6.2.2.1 | 3b `5 cycles` |
| | `$7C` | `JMP (abs,X)` | `6` | 3 | 6.2.2.1 | 2a `6 cycles` |
| | `$5C` | `JMP long` / `JML` | `4` | 4 | 6.2.2.1 | 4b `4 cycles` |
| | `$DC` | `JMP [abs]` / `JML` | `6` | 3 | 6.2.2.1 | 3a `6 cycles` |
| Calls | `$20` | `JSR abs` | `6` | 3 | 6.2.2.1 | 1c `6 cycles` |
| | `$FC` | `JSR (abs,X)` | `8` | 3 | 6.2.2.1 | 2b `8 cycles` |
| | `$22` | `JSL long` | `8` | 4 | 6.2.2.1 | 4c `8 cycles` |
| Returns | `$40` | `RTI` | `7-e` | 1 | 6.3.2 | 22g `6 and 7` |
| | `$60` | `RTS` | `6` | 1 | 6.2.2.2 | 22h `6 cycles` |
| | `$6B` | `RTL` | `6` | 1 | 6.2.2.2 | 22i `6 cycles` |
| Stack, address | `$F4` | `PEA` | `5` | 3 | 6.8.1 | 22d `5 cycles` |
| | `$D4` | `PEI` | `6+w` | 2 | 6.8.1 | 22e `6 and 7` |
| | `$62` | `PER` | `6` | 3 | 6.8.1 | 22f `6 cycles` |

Forty-four rows. Every enumerated Table 5-7 header value is reproduced by the corresponding formula and no
header value is left over.

**What the symbol set says about this phase, and it is the opposite of §13's.** §13.3 found that across all
59 of its opcodes *"the only symbols that appear in a cycle formula are `m` and `w`"*. Here:

- **`e` appears, and it is new.** `8-e`, `7-e`, and `t*e*p` inside the branches. No phase before 7d has had
  a cycle count that depends on the emulation flag. Three groups pay it and they pay it for three unrelated
  reasons: the interrupts because `PBR` is not pushed, `RTI` because `PBR` is not pulled, and the branches
  because the page-cross penalty is a 6502 compatibility artifact.
- **`p` appears only inside `t*e*p`.** There is no unconditional page-cross penalty anywhere in this phase.
- **`w` appears exactly once, on `PEI`** — consistent with §5's rule that `w` appears only on direct-page
  modes, `PEI` being the phase's only direct-page instruction.
- **`m` and `x` appear only on the four width-dependent pushes and four pulls.** Neither appears on any
  branch, jump, call, return, interrupt, block move or halt, nor on `PEA`/`PEI`/`PER`.
- **`t` is new and belongs to the branches alone.**

**Three rows whose second source is not Table 5-7,** flagged so nobody records them as two-source facts:
`WDM` has **no Table 5-7 row at all**, and its `2` comes from Clark §6.7 and from the vectors (§14.2);
`MVN`/`MVP`'s *total* is Clark's prose (*"it will take 7 cycles per byte moved total"*) and the vectors
(§14.3), Table 5-7 stating only the per-iteration 7; and the branches' `t*e*p` shape comes from Clark's
formula and from Notes 5 and 6, the row header (`2,3 and 4 cycles`) being merely consistent with it rather
than a second derivation.

### 14.9 The gaps this section records, listed in one place

Everything above either carries a named source, or is labelled a measurement, or appears here. Same practice
as §12.6 and §13.6.

| # | Gap | Status |
| --- | --- | --- |
| 1 | **`IRQ` and `NMI` cannot be certified against the vector set at all.** (Discharged as coverage 2026-08-05: `W65C816InterruptTests` unit-tests both, per this row's own "unit tests only".) `SingleStepTests/65816` is 512 files, one per opcode per mode (§2.3); there is no interrupt-line stimulus in any of them. Table 5-7 row 22a specifies the sequence fully and §14.2 transcribes it, but **nothing in the arbiter can check it** | **Open, structural.** Not a documentation gap — a coverage gap. `BRK` and `COP` share cycles 3–8 with row 22a and *are* vector-covered, so the shared sequence is arbitrated; what is not covered is the two leading `IO` cycles at `PBR,PC`, the recognition timing, and the `NMI`/`IRQ`/`ABORT` vector selection. Unit tests only |
| 2 | **The `NMI`-hijack anomaly.** Whether an `NMI` asserted during a `BRK` sequence causes the `BRK` to fetch the `NMI` vector, as it does on the NMOS parts this repository has certified. **The sources are silent**: neither Clark nor the datasheet mentions the case. Clark §6.3.1.1's *"the instruction is completed before pushing anything"* is about a different case and does not cover it | **Still open as a question; CLOSED as a decision, 2026-08-05, phase 7d task 3.** The question is unanswerable and follows from gap 1. The implementation has **no hijack**, and, for the same reason, does **not** extend `Cpu.Tick`'s `VectorHi` recognition blackout to the 65816 — both are NMOS die behaviour that no 65816 source states. Written down in §14.2's "Implemented" block with the reasoning and the visible consequence, as this row required |
| 3 | **Note 9's two-cycle wait** — *"Wait at cycle 2 for 2 cycles after NMIB or IRQB active input."* §8 deferred this note to phase 7d. It is transcribed in §14.2, but what it specifies is a hardware recognition handshake, not a cycle any vector records | **Recorded, not actionable.** Discharges §8's deferral. Follows from gap 1: nothing can check it |
| 4 | **Note 16, "COP Latches."** §8 deferred this note to phase 7d too. That five-character sentence is the entire note, and it is attached to an address-bus cell, not to a behaviour | **Recorded, not actionable.** Discharges §8's deferral. The note carries no content; the `COP` sequence is fully specified by row 22j without it |
| 5 | **What `PHP` pushes into bits 4 and 5.** Clark §6.8.3 describes `PHP` as pushing "the P register" and says nothing about the two mode bits; the datasheet's Table 5-5 gives `P → Ms` with no qualification | **CLOSED 2026-08-05, measured.** `P` verbatim, all eight bits, both modes, 20,000 of 20,000. See §14.1's measured block — including the caveat that in emulation mode this is observationally identical to forcing `$30` |
| 6 | **Whether `PLP` setting `x = 1` forces `XH = YH = $00`.** Clark §4 states the rule as a property of the *flag* and illustrates it with `SEP` only; no source names `PLP` | **CLOSED 2026-08-05, measured.** It does, immediately. 2,494 non-vacuous `28 n` vectors, all with final `XH = YH = $00`. See §14.1's measured block |
| 7 | **What emulation-mode `COP` pushes in bit 4.** Note 11 and Table 8-1 enumerate `IRQ`, `NMI` and `ABORT`; Clark §6.3.1's "the b flag will be set" is scoped to `BRK`. **The sources are silent on `COP`** | **CLOSED 2026-08-05, measured.** `1`, the same as emulation `BRK` — because the push is `P` verbatim and emulation `P` always has bit 4 set. Same observational caveat as gap 5 |
| 8 | **`JSR (abs,X)`'s push-before-`AAH` ordering has one source, not two.** Table 5-7 row 2b states it; **Clark is silent on the ordering**, giving only the cycle count | **CLOSED 2026-08-06, phase 7d task 7.** The vectors agree with the row: all 20,000 `$FC` vectors put the two writes at cycles 3 and 4 and the `AAH` fetch at cycle 5. The row was the only thing behind it and the row is right. (The same two rows' *pin* cells are not — see §14.6's amendment (a)) |
| 9 | **The stack's bank-0 wrap at `S == $0000` is cited but not measured.** Clark §5.1.2 and §5.22 state it plainly and Table 5-7 writes the bank as a literal `0` on every stack cycle. But the `08.n` and `28.n` vector sets contain **no vector with `S <= $0001` or `S >= $FFFE`** | **Recorded, cited only.** The emulation-mode page-one wrap *is* measured (`02 e 1`). Noted so a later reader knows a green `PHP`/`PLP` run does not prove the native wrap |
| 10 | **Table 5-7's note column is misaligned on rows 22a and 22c**, verified against 400 dpi renderings: row 22a prints note `(7)` beside cycle 2 rather than the `PBR` push at cycle 3, and row 22c prints note `(1)` beside cycle 2 rather than the conditional push at `3a` | **Resolved, not open.** Both resolved by cross-row comparison (22j and 22b put the same notes unambiguously) and by §7.11/Clark. Recorded in the form §13.1 uses for row 16b, because the extracted text and the rendered page show the same offset and a reader checking either alone would see it |
| 11 | **`WAI` and `STP` cannot be fully certified by vectors.** The files model the three executed cycles and then a `[null, null, "--------"]` sentinel; the hold, the wake, `WAI`'s `i`-flag special case and `STP`'s reset-only exit are absent | **CLOSED, 2026-08-05, phase 7d task 5.** Measured, §14.4. The three cycles are arbitrated by all 40,000 vectors; everything else is `WaiStpTests`. The harness needed **no** null-address cycle kind in the end — the sentinel means "no address, no access", the core's hold performs none, and `AssertCycles` compares the three access entries against three logged accesses, which makes the sentinel an assertion rather than an exemption. `$CB`/`$DB` join `$54`/`$44` on `VectorsTruncatedMidInstruction` for the boundary assertion. Probed: a halt that ends the instruction and only sets a flag passes **all 40,000** and fails 14 of 17 unit cases — see §14.4's resolution note |
| 12 | **`MVN`/`MVP` vectors are truncated at 100 cycles** and their final state is mid-instruction | **CLOSED, 2026-08-05, phase 7d task 4.** Measured, §14.3. `Harte816Tests`' `AtInstructionBoundary` assertion fails on 9,999 of 10,000 `54 n` vectors regardless of how correct the core is, so it is skipped for `$54`/`$44` alone — every cycle, register and memory assertion still runs against the vector's own mid-instruction final state, no file is excluded and no vector is skipped. **The truncation costs the harness a second change the row did not predict**, and any future truncated opcode will need it too: see §14.3's "What the exemption actually costs" |
| 13 | **Whether any of the 44 behaves differently in emulation mode beyond the documented `e` terms and the forced `m = 1`/`x = 1`.** Clark's §6 preamble *"In general, in emulation mode … the 65C816 has the same behavior as 65C02"* is the only general statement, and §12.6 records that it turned out **wrong** for decimal `SBC` | **Open by policy**, exactly as §13.6 gap 5 leaves it. Treat as hypothesis; the vectors arbitrate |

**Not gaps, so nobody re-opens them:** the stack's bank-0 confinement as a *rule* (§14.1, Clark §5.1.2 and
§5.22, plus every address cell in rows 22a–22j); push-high-first and pull-low-first (§14.1, rows 22b/22c and
four separate Clark passages); all thirteen push/pull cycle counts (§14.1, Clark and the row headers); the
six interrupt vector addresses in both modes (§14.2, Tables 5-2/5-3 and Clark §6.3.1.1, agreeing cell for
cell); `PBR` pushed in native only and cleared in both (§14.2, §7.11 verbatim); `D = 0` and `I = 1` after
the push (§14.2, Clark and p. 30, and measured); `VPB` on the two vector cycles with `VDA` alongside
(§14.2, the table, the p. 30 prose, and measured); the branch page-cross cycle being emulation-only
(§14.5, Note 6 verbatim and Clark's `t*e*p`); `BRL` flat 4 (§14.5, both sources, no note markers);
branch and `BRL` bank wrapping (§14.5, Clark §4 with worked values for each width); every jump's pointer
bank (§14.6, Clark §5.4/§5.5 and the table's address cells); the absence of the NMOS `JMP ($xxFF)` bug
(§14.6, Clark §5.4 verbatim); what `JSR`/`JSL` push and that it is the instruction's last byte (§14.6,
Clark §6.2.2.1 verbatim with two worked examples); the byte counts and increments of `RTS`/`RTL`/`RTI`
(§14.6, Clark §6.2.2.2/§6.3.2 and rows 22g–22i); all three of `PEA`/`PEI`/`PER` pushing 16 bits regardless
of `m` (§14.7, Clark §6.8.1 verbatim and two un-gated write cycles per row); `PER`'s base being the next
instruction (§14.7, Clark §5.14 and §5.18); and all 44 cycle formulas (§14.8).

**And four things a reader of one source alone would get wrong, repeated here because they are this
section's most expensive findings:** the `WDM` second byte is *not* read, against Clark's plain sentence
(§3.4, §14.2); the two vector-pull cycles assert `VDA` as well as `VPB`, which no amount of reasoning about
their purpose would give (§14.2); a taken branch pays no page-cross cycle in native mode, unlike every
8-bit core in this repository (§14.5); and `MVN`/`MVP` re-fetch their own opcode and both operand bytes on
every iteration, so `PC` is rewound seven cycles at a time and the vectors are truncated mid-instruction
(§14.3).

---

## 15. Phase 7e's oracle — what 64tass actually assembles

Added 2026-08-06, before any phase 7e code was written. Same practice as §9, §12, §13 and §14:
establish the facts first, implement second. The phase 7e plan
(`docs/superpowers/plans/2026-08-06-phase7e-disassembler.md`) cites this material as **§15.1**–**§15.4**,
which is the numbering used here.

**This section is different in kind from every one before it.** §9 and §13 transcribe a datasheet;
§12, §13 and §14 measure a vector corpus. Phase 7e's subject is a *program* — 64tass, already a
prerequisite for the Klaus interrupt test and already the oracle behind `RoundTripTests` — so almost
nothing here is cited and almost everything is measured. Every byte string below was produced by
running the assembler, not recalled.

**The tool, and the method.** `64tass --version` reports **`64tass Turbo Assembler Macro V1.60.3243`**.
Unless a row says otherwise, each source line was assembled *alone* in a file of the form

```
	.cpu "65816"
	*=$1000
	<the line>
```

with `64tass --nostart -o f.bin f.asm`, and the whole of `f.bin` is the byte string recorded. Where an
`.as`/`.al`/`.xs`/`.xl` directive is named, it is a line of its own ahead of the instruction and its
(zero) bytes are excluded. `--nostart` is what `RoundTripTests.Assemble` already passes, so these bytes
are the bytes that harness compares.

**Notation.** `m` and `x` are the flag values as everywhere else in this document (1 = eight bits,
0 = sixteen). "Operative width" means the width the flag selects for that instruction: `m` for the
accumulator operations, `x` for the index operations. `AddrMode` names are this repository's, from
`src/SixtyFiveXX/AddrMode.cs`.

**Nothing in this section contradicts a published source, so it adds no §3 entry.** 64tass agreed with
the datasheet, with Clark and with §14.3 everywhere the three could be compared. What it *did*
contradict is one line of the phase 7e plan, and that correction is recorded in §15.2.

### 15.1 The dialect, the width directives, and a notation row for every mode

**The dialect name is exactly `65816`.** Measured: `.cpu "65816"` assembles `JSL $123456` to
`22 56 34 12`. `.cpu "65c816"`, `.cpu "w65816"` and `.cpu "65802"` are all rejected —
`error: unknown processor`. The command-line form works with no `.cpu` line at all:
`64tass --m65816 --nostart` assembles the same `JSL $123456` to the same `22 56 34 12`.

**The width directives, and what happens on a mismatch.**

> `.as` / `.al` set the accumulator width and `.xs` / `.xl` the index width, and the length of an
> immediate operand comes from the **directive**, never from the value:
>
> | Source | Bytes |
> | --- | --- |
> | (no directive) `LDA #$34` | `a9 34` |
> | (no directive) `LDA #$1234` | **error** — *too large for a 8 bit unsigned integer bits '$1234'* |
> | `.as` `LDA #$34` | `a9 34` |
> | `.as` `LDA #$1234` | **error** — same message |
> | `.al` `LDA #$34` | `a9 34 00` |
> | `.al` `LDA #$0034` | `a9 34 00` |
> | `.al` `LDA #$1234` | `a9 34 12` |
> | `.xs` `LDX #$34` | `a2 34` |
> | `.xs` `CPY #$1234` | **error** — same message |
> | `.xl` `LDX #$34` | `a2 34 00` |
> | `.xl` `LDX #$1234` | `a2 34 12` |
> | `.al` + `.xs` `CPX #$34` | `e0 34` |
>
> Three consequences, all load-bearing for phase 7e. **The default with no directive at all is eight
> bits**, which is what makes the existing five round-trips continue to work unchanged. **A width
> mismatch is a hard error, not a silent truncation** — a disassembler that rendered a 16-bit immediate
> while the listing said `.as` would fail the round-trip loudly rather than round-trip wrongly. And
> **the two widths are independent**: `.al` does not widen an `x`-sized immediate.

> **`REP` and `SEP` do not change the assembler's width in the default mode.** `REP #$30` followed by
> `LDA #$34` assembles to `c2 30 a9 34` — the `LDA` is still two bytes. Under `.autsiz` it does track:
> the same two lines become `c2 30 a9 34 00`, and `.autsiz` with `SEP #$30` gives `e2 30 a9 34`.
> `.mansiz` restores the non-tracking behaviour, and **`.mansiz` is the default** — measured, since the
> no-directive run and the explicit `.mansiz` run produce identical bytes.
>
> This matters to phase 7e's round-trip more than it looks. That listing contains `$C2` and `$E2` as
> *data* — every opcode appears in it — and their rendered text is `REP #$30` / `SEP #$30`. In the
> default mode those lines are inert and the width the listing declared at the top holds all the way
> down. A listing assembled under `.autsiz` would change width halfway through and every subsequent
> immediate would be the wrong length.

> **`REP`/`SEP` themselves never widen.** `REP #$30` is `c2 30` and `SEP #$30` is `e2 30` under `.as`
> and under `.al` alike — they are `AddrMode.ImmediateByte`, and the width flags do not reach them.

**The complete notation table.** One row per addressing mode this repository has, including the modes
phase 6a already renders, so this is a table rather than a delta. Assembled at `*=$1000` under `.as`
and `.xs` unless the row names a directive. The `@w`/`@l` prefixes are explained in §15.2; they are
included here because they are part of the text phase 7e has to emit.

| `AddrMode` | Source text | Assembled bytes | Length |
| --- | --- | --- | --- |
| `Implied` | `NOP` | `ea` | 1 |
| `Accumulator` | `ASL A` | `0a` | 1 |
| `Immediate`, `m = 1` (`.as`) | `LDA #$34` | `a9 34` | 2 |
| `Immediate`, `m = 0` (`.al`) | `LDA #$1234` | `a9 34 12` | 3 |
| `Immediate`, `x = 1` (`.xs`) | `LDX #$34` | `a2 34` | 2 |
| `Immediate`, `x = 0` (`.xl`) | `LDX #$1234` | `a2 34 12` | 3 |
| `ImmediateByte` | `REP #$30` | `c2 30` | 2 |
| `ImmediateByte` | `SEP #$30` | `e2 30` | 2 |
| `ImmediateByte` | `WDM #$12` | `42 12` | 2 |
| `DirectPage` | `LDA $12` | `a5 12` | 2 |
| `DirectPageX` | `LDA $12,X` | `b5 12` | 2 |
| `DirectPageY` | `LDX $12,Y` | `b6 12` | 2 |
| `DirectPageIndirect` | `LDA ($12)` | `b2 12` | 2 |
| `DirectPageIndirectY` | `LDA ($12),Y` | `b1 12` | 2 |
| `DirectPageIndexedIndirectX` | `LDA ($12,X)` | `a1 12` | 2 |
| `DirectPageIndirectLong` | `LDA [$12]` | `a7 12` | 2 |
| `DirectPageIndirectLongY` | `LDA [$12],Y` | `b7 12` | 2 |
| `StackRelative` | `LDA $12,S` | `a3 12` | 2 |
| `StackRelativeIndirectY` | `LDA ($12,S),Y` | `b3 12` | 2 |
| `Absolute` | `LDA @w $1234` | `ad 34 12` | 3 |
| `AbsoluteX` | `LDA @w $1234,X` | `bd 34 12` | 3 |
| `AbsoluteY` | `LDA @w $1234,Y` | `b9 34 12` | 3 |
| `AbsoluteLong` | `LDA @l $123456` | `af 56 34 12` | 4 |
| `AbsoluteLongX` | `LDA @l $123456,X` | `bf 56 34 12` | 4 |
| `Indirect` | `JMP @w ($1234)` | `6c 34 12` | 3 |
| `AbsoluteIndexedIndirect` | `JMP @w ($1234,X)` | `7c 34 12` | 3 |
| `AbsoluteIndirectLong` | `JML @w [$1234]` | `dc 34 12` | 3 |
| `Relative` | `BEQ $1036` | `f0 34` | 2 |
| `RelativeLong` | `BRL $2237` | `82 34 12` | 3 |
| `BlockMove` | `MVN $12,$34` | `54 34 12` | 3 |
| `BlockMove` | `MVP $12,$34` | `44 34 12` | 3 |
| `Stack`, `Op.Brk` | `BRK #$12` | `00 12` | 2 |
| `Stack`, `Op.Cop` | `COP #$12` | `02 12` | 2 |
| `Stack`, `Op.Jmp` | `JMP @w $1234` | `4c 34 12` | 3 |
| `Stack`, `Op.Jsr` | `JSR @w $1234` | `20 34 12` | 3 |
| `Stack`, `Op.Pea` | `PEA @w $1234` | `f4 34 12` | 3 |
| `Stack`, `Op.Pei` | `PEI ($12)` | `d4 12` | 2 |
| `Stack`, `Op.Per` | `PER $2237` | `62 34 12` | 3 |
| `Stack`, `Op.Rtl` | `RTL` | `6b` | 1 |
| `Stack`, the pushes and pulls | `PHB` | `8b` | 1 |
| `AbsoluteLong`, `JSL` | `JSL @l $123456` | `22 56 34 12` | 4 |
| `AbsoluteLong`, `JML` long | `JML @l $123456` | `5c 56 34 12` | 4 |

**Three rows deserve their own note.**

> **`MVN`/`MVP`'s operands reverse between the text and the byte stream, and 64tass agrees with §14.3.**
> `MVN $12,$34` is `54 34 12` and `MVP $AB,$CD` is `44 cd ab`. §14.3 established from `54.n.json` that
> the byte at `PC+1` is the **destination** bank — it becomes the final `DBR` — and the byte at `PC+2`
> is the source. So the byte immediately after the opcode is `$34`, the operand written **second** in
> the text. **The text is `src,dst`; the stream is `dst,src`.** A renderer that emits the operand bytes
> in the order it read them produces a `MVN` that moves the wrong way, and 64tass will assemble it
> without complaint because both operands are syntactically valid banks.

> **`PER` and `BRL` encode a displacement and must be rendered as a target, and the target depends on
> the address the instruction sits at.** Measured at four origins, target `$1234` throughout:
>
> | Address | `PER $1234` | `BRL $1234` |
> | --- | --- | --- |
> | `$1000` | `62 31 02` | `82 31 02` |
> | `$1024` | `62 0d 02` | `82 0d 02` |
> | `$1027` | `62 0a 02` | `82 0a 02` |
> | `$2000` | `62 31 f2` | `82 31 f2` |
>
> The rule, stated so no one has to re-measure: **the encoded word is `(target − (address + 3)) & $FFFF`,
> little-endian** — a 16-bit signed displacement from the byte *after* the three-byte instruction,
> which is §14.5's and §14.7's rule for the part itself. Rendering runs the other way:
> `target = (address + 3 + (int16)displacement) & $FFFF`.
>
> **This corrects the phase 7e plan.** Its "Established facts" block records `PER $1234` → `62 0d 02`
> and `BRL $1234` → `82 0a 02` as if they were properties of the instruction. They are not: they are
> the bytes at `$1024` and `$1027` respectively, which is where those two lines happened to fall in the
> file the plan's author assembled. The table above reproduces both exactly at those addresses, so the
> plan's numbers are right and its framing is not. Anything phase 7e writes that quotes a `PER` or
> `BRL` byte string has to quote the address with it.

> **`JML` is 64tass's mnemonic for two different opcodes, told apart by syntax alone.**
> `JML $123456` is `5c 56 34 12` (`AddrMode.AbsoluteLong`) and `JML [$1234]` is `dc 34 12`
> (`AddrMode.AbsoluteIndirectLong`). This repository's table already spells both `JML`, so no
> translation is needed — unlike the `RMB0`/`BBS7` case `RoundTripTests.ForAssembler` exists for.

### 15.2 The shortest-encoding rule, the forcing prefixes, and the predicate

**64tass emits the shortest encoding of that mnemonic whose operand range covers the value.** It is a
property of the *value together with the mnemonic*, not of the value alone, and that second half is
what makes the naive rule wrong. Measured, both boundaries the plan asked for:

> | Source | Bytes | Encoding chosen |
> | --- | --- | --- |
> | `LDA $00FF` | `a5 ff` | direct page — **collapsed** |
> | `LDA $0100` | `ad 00 01` | absolute |
> | `LDA $FFFF` | `ad ff ff` | absolute |
> | `LDA $00FFFF` | `ad ff ff` | absolute — **collapsed** |
> | `LDA $010000` | `af 00 00 01` | long |
> | `LDA $10000` | `af 00 00 01` | long |
>
> So the two boundaries are exactly `$0100` and `$010000`, and both are exclusive at the bottom: a value
> of `$00FF` collapses and a value of `$0100` does not.

**The forcing prefixes are `@b`, `@w` and `@l`**, written between the mnemonic and the operand:

> | Source | Bytes |
> | --- | --- |
> | `LDA @w $0012` | `ad 12 00` |
> | `LDA @w $00FF` | `ad ff 00` |
> | `LDA @w $0100` | `ad 00 01` |
> | `LDA @w $1234` | `ad 34 12` |
> | `LDA @l $001234` | `af 34 12 00` |
> | `LDA @l $00FFFF` | `af ff ff 00` |
> | `LDA @l $010000` | `af 00 00 01` |
> | `LDA @l $123456` | `af 56 34 12` |
>
> **Forcing a width the value already needs is a no-op**, as the `@w $1234` and `@l $123456` rows show.
> That is the fact that makes the predicate below safe to state unconditionally.

**Which modes actually collapse, and which are immune.** Measured with the operand value `$0012`
(and `$000012` for the long forms), each line assembled alone:

> | Opcode | Unforced text | Unforced bytes | Forced text | Forced bytes |
> | --- | --- | --- | --- | --- |
> | `$AD` | `LDA $0012` | `a5 12` | `LDA @w $0012` | `ad 12 00` |
> | `$BD` | `LDA $0012,X` | `b5 12` | `LDA @w $0012,X` | `bd 12 00` |
> | `$B9` | `LDA $0012,Y` | `b9 12 00` | `LDA @w $0012,Y` | `b9 12 00` |
> | `$BE` | `LDX $0012,Y` | `b6 12` | `LDX @w $0012,Y` | `be 12 00` |
> | `$9E` | `STZ $0012,X` | `74 12` | `STZ @w $0012,X` | `9e 12 00` |
> | `$AF` | `LDA $000012` | `a5 12` | `LDA @l $000012` | `af 12 00 00` |
> | `$BF` | `LDA $000012,X` | `b5 12` | `LDA @l $000012,X` | `bf 12 00 00` |
> | `$6C` | `JMP ($0012)` | `6c 12 00` | `JMP @w ($0012)` | `6c 12 00` |
> | `$7C` | `JMP ($0012,X)` | `7c 12 00` | `JMP @w ($0012,X)` | `7c 12 00` |
> | `$DC` | `JML [$0012]` | `dc 12 00` | `JML @w [$0012]` | `dc 12 00` |
> | `$4C` | `JMP $0012` | `4c 12 00` | `JMP @w $0012` | `4c 12 00` |
> | `$20` | `JSR $0012` | `20 12 00` | `JSR @w $0012` | `20 12 00` |
> | `$F4` | `PEA $0012` | `f4 12 00` | `PEA @w $0012` | `f4 12 00` |
> | `$22` | `JSL $000012` | `22 12 00 00` | `JSL @l $000012` | `22 12 00 00` |
> | `$5C` | `JML $000012` | `5c 12 00 00` | `JML @l $000012` | `5c 12 00 00` |
>
> **`LDA $0012,Y` does not collapse but `LDX $0012,Y` does.** There is no `LDA dp,Y` on the part, so
> `abs,Y` is already the shortest `LDA` form; there *is* an `LDX dp,Y` (`$B6`), so `LDX abs,Y` collapses.
> A rule phrased purely on the value would predict both or neither and be wrong about one of them.
> Likewise `JMP`, `JSR`, `PEA`, `JSL`, `JML` and the three indirect-absolute forms have no shorter
> encoding of their own mnemonic and are immune whatever the value.

**The predicate, stated so a reader can implement it without re-measuring.** Two forms, and they are
measurably equivalent:

> - **Conditional** — emit `@w ` before the operand of `Absolute`, `AbsoluteX`, `AbsoluteY`, `Indirect`,
>   `AbsoluteIndexedIndirect`, `AbsoluteIndirectLong` and the `Op.Jmp`/`Op.Jsr`/`Op.Pea` arms of
>   `Stack` **when the rendered value is `< $0100`**; emit `@l ` before the operand of `AbsoluteLong`
>   and `AbsoluteLongX` **when the rendered value is `< $010000`**.
> - **Unconditional** — emit the same prefix for those modes always.
>
> They produce identical bytes for every one of the 256 opcodes, because forcing a width the value
> already needs is a no-op. The conditional form is what the phase 7e spec states and it renders more
> readable text; the unconditional form is one fewer branch. **Both are correct — this is a
> presentation choice, not a correctness one.** Verified by assembling the complete 256-opcode listing
> both ways with operand bytes that make every absolute operand `$1234`: identical output, 559 bytes.

**Nothing collapses below two bytes, and `@b` is never needed.** `LDA $12` is `a5 12`, `LDA $00` is
`a5 00`, and `LDA @b $12` and `LDA @b $00` are the same two bytes. There is no one-byte-operand
addressing mode on the part for `@b` to reach for, and every direct-page operand this disassembler
renders is written with two hex digits and so is already under `$100`. `@b` would only earn its place
if the listing ever set `.dpage` to something other than zero, which nothing in this repository does.

**One immediate-mode consequence, because it is the exception that makes the rule easy.** Immediates
never collapse and take no prefix: `.al` + `LDA #$0034` is `a9 34 00`, not `a9 34`. The operand length
comes from the width directive (§15.1), so the value is irrelevant. The immediate is the only operand
on the part with that property.

### 15.3 The ambiguity set is empty, and the covered count is 256

`RoundTripTests.AmbiguousOpcodes` excludes any opcode that renders as text some *other* opcode of the
same variant also renders, because an assembler handed that text has to pick one encoding and the
others cannot come back as themselves. The five 8-bit cores lose 43, 43, 79, 46 and 44 opcodes that
way, which is where the pinned counts of 213, 213, 177, 210 and 212 come from.

> **Measured, not cited: the 65816 loses none. The covered count is 256 of 256, under all four `m`/`x`
> combinations.**
>
> Derived by rendering all 256 opcodes of `Opcodes65C816.Table` under the notation of §15.1 and
> grouping by text, which is exactly what `AmbiguousOpcodes` does. Every one of the 256 renders as a
> distinct string. It survives a sweep of **576 combinations** — eight low operand bytes
> (`$00 $01 $12 $34 $7F $80 $FD $FF`) × six high bytes (`$00 $01 $12 $7F $80 $FF`) × three bank bytes
> (`$00 $56 $FF`) × four `m`/`x` combinations — with zero ambiguity in every one, so the result does
> not depend on which operand bytes phase 7e's harness picks.
>
> **The set does not differ per width combination**, which the plan's step 5 flagged as a possibility.
> It cannot: widening an immediate changes `#$34` to `#$1234` for every `Width.M`/`Width.X` opcode at
> once, and those opcodes already differ from each other by mnemonic.
>
> **Why it comes out empty is worth one sentence, because it is a property of the part rather than
> luck.** WDC assigned all 256 opcodes and this repository's table defines all 256 with no
> `OpcodeInfo.Undefined` entry, so there are no `???` or `JAM` shapes to collide, and no undocumented
> `NOP` aliases of the kind that cost the NMOS parts 43 opcodes and Synertek 79.

> **Measured, not cited: all 256 round-trip through 64tass exactly, at every width, today.**
>
> A complete listing was laid out the way `RoundTripTests.Build` lays one out — every opcode at
> consecutive addresses from `*=$1000`, operand bytes `$34`, `$12` and (for the four-byte forms) `$56`,
> each instruction given only as many operand bytes as its length — rendered under §15.1's notation with
> `@w`/`@l` forcing, prefixed with the two width directives, and assembled:
>
> | `.as`/`.al` | `.xs`/`.xl` | Result |
> | --- | --- | --- |
> | `.as` | `.xs` | **559 bytes, byte-for-byte identical** |
> | `.al` | `.xs` | **567 bytes, byte-for-byte identical** |
> | `.as` | `.xl` | **563 bytes, byte-for-byte identical** |
> | `.al` | `.xl` | **571 bytes, byte-for-byte identical** |
>
> The steps are the eight `Width.M` immediates (`ORA AND EOR ADC SBC CMP LDA BIT`) and the four
> `Width.X` ones (`CPY CPX LDY LDX`) gaining a byte each — `+8` for `.al`, `+4` for `.xl`, `+12` for
> both. So **the expected covered count for phase
> 7e's round-trip is 256 for all four combinations, with an empty exclusion list**, and the notation in
> §15.1 is sufficient to reach it — this is not a prediction, it is a run.
>
> **And the forcing is load-bearing exactly where §15.2 says.** The same listing rebuilt with operand
> bytes `$12`/`$00`/`$00` — making every absolute operand `$0012` and every long operand `$000012` —
> assembles to **559 bytes identical with forcing** and to **485 bytes, a 74-byte shortfall, without
> it**. With the operand bytes `$34`/`$12` the harness uses today, the unforced listing also round-trips
> exactly: the forcing is invisible at those operand values. That is the same blind spot §15.4's first
> question is about.

### 15.4 The two questions the phase 7e spec left open

#### Question 1: the five 8-bit cores are affected by the collapse, and have been since phase 6a

> **Measured, not cited. The answer is yes, under both 8-bit dialects.**
>
> ```
> .cpu "6502i"    LDA $0012  ->  a5 12       (direct page — collapsed)
> .cpu "w65c02"   LDA $0012  ->  a5 12       (direct page — collapsed)
> ```
>
> `Disassembler`'s `AddrMode.Absolute`, `AbsoluteX`, `AbsoluteY`, `NopAbsolute` and `NopAbsoluteExtra`
> arms all render `${value:X4}` with no prefix, so any of those opcodes whose operand word is under
> `$0100` renders text that 64tass assembles as a *different, shorter opcode*.
>
> **The extent, per variant.** Every opcode counted in the pinned covered count was rendered with
> operand bytes `$12`/`$00` — making every absolute operand `$0012` — and assembled on its own; the
> count below is how many produced bytes other than the opcode and operand they were built from:
>
> | Variant | Dialect | Covered | Renders wrongly with an operand `< $0100` |
> | --- | --- | --- | --- |
> | `Mos6502Variant` | `6502i` | 213 | **53** |
> | `Mos6510Variant` | `6502i` | 213 | **53** |
> | `Synertek65C02Variant` | `w65c02` | 177 | **42** |
> | `Rockwell65C02Variant` | `w65c02` | 210 | **42** |
> | `Wdc65C02Variant` | `w65c02` | 212 | **42** |
>
> Examples from the 6502 set: `$0C NOP $0012` → `04 12`, `$0D ORA $0012` → `05 12`,
> `$0E ASL $0012` → `06 12`, `$0F SLO $0012` → `07 12`, `$1D ORA $0012,X` → `15 12`. From the 65C02
> set: `$0C TSB $0012` → `04 12`, `$1C TRB $0012` → `14 12`, `$9E STZ $0012,X` → `74 12`.
>
> **Why no existing test sees it.** `RoundTripTests` uses `OperandLo = $34` and `OperandHi = $12`, so
> every absolute operand in the image it builds is `$1234` — above the boundary, and the shortest
> encoding is the one the disassembler meant. The gate is structurally incapable of reaching the
> collapse, not merely unlucky. All five round-trips were reproduced here at those operand bytes and at
> `$12`/`$00`; the pinned counts 213, 213, 177, 210 and 212 all reproduce, and the failures appear only
> at the second operand pair.
>
> **What is NOT settled here, deliberately.** Whether to fix it, and how. Adding `@w` would change the
> rendered text of five certified cores — text that is public API in the sense that
> `Instruction.Operand` is documented as "the usual 6502 notation", and `@w` is a 64tass spelling rather
> than a 6502 one. Doing nothing leaves a disassembler that emits reassemble-wrong text for page-zero
> addresses on every 8-bit core. **That decision belongs to the phase owner and this task does not
> make it.** Recorded as gap 1 in §15.5.

#### Question 2: `PublicSurfaceTests` would not see a new overload. The spec is right

> **Answered by reading `tests/SixtyFiveXX.Conformance/PublicSurfaceTests.cs`, not by measurement.**
>
> Every assertion in that file is about **types**, and none about members:
>
> - `PackagedAssembly_ExposesExactlyTheIntendedPublicSurface` compares `ExpectedPublicTypes` — a list of
>   namespace-qualified type names such as `SixtyFiveXX.Disassembler` — against
>   `PackedAssemblies.PublicTypesFor(tfm)`.
> - `PublicTypesFor` is built by `ReadPublicTypes`, which enumerates `MetadataReader.TypeDefinitions`
>   and projects each to `FullName`. **It never reads a `MethodDefinition`.**
> - `PackagedAssembly_KeepsTheDescriptorModelInvisible` checks `MustStayInternal` against the same
>   type-name array.
> - `Package_ShipsEveryDeclaredTargetFramework` compares TFM directory names.
>
> The class remarks say so outright: *"Scope: types, not members."* Adding
> `Decode<TBus, TVariant>(in TBus, int, bool, bool)` to the existing public static class
> `SixtyFiveXX.Disassembler` adds a `MethodDefinition` and no `TypeDefinition`, so the set this test
> compares is unchanged and **task 2 needs no edit to this file**.
>
> **The one way task 2 could still break it**, stated because it is a real edge and not a hypothetical:
> if the overload's parameters were typed on a *new public type* of this assembly — an options struct,
> a `Widths` enum — that type would appear in `TypeDefinitions` and the exact-set assertion would fail.
> Two `bool` parameters introduce no such type. The same applies to a compiler-generated nested type,
> which `IsVisibleOutsideTheAssembly` would only surface if it were `NestedPublic`.

### 15.5 The gaps this section records, listed in one place

Everything above either carries a named source, or is labelled a measurement, or appears here. Same
practice as §12.6, §13.6 and §14.9.

| # | Gap | Status |
| --- | --- | --- |
| 1 | **The five 8-bit cores render absolute operands under `$0100` as text 64tass reassembles to a different opcode** — 53 opcodes on the two NMOS parts, 42 on each 65C02. Present since phase 6a; invisible to `RoundTripTests` because its operand bytes put every absolute operand at `$1234` | **Open as a decision, measured as a fact.** §15.4 question 1 carries the numbers and the two dialect measurements. Fixing it changes the rendered text of five certified cores and is the phase owner's call, not this task's. If it is fixed, the round-trip's operand bytes have to change too, or the fix will be as invisible as the defect |
| 2 | **`@w`/`@l` are 64tass spellings, not 6502 notation.** `Instruction.Operand` is documented as "the usual 6502 notation"; a prefix that only one assembler understands is a step away from that | **Recorded, not actionable here.** It is the price of an assembler-checked gate, and `RoundTripTests.ForAssembler` already exists as the place where "what a reader wants" is rewritten into "what 64tass accepts" — the `RMB0`/`rmb 0,` case. Whether the prefix belongs in the library or in that method is a phase 7e design choice |
| 3 | **Only 64tass was probed.** Every claim in this section is a claim about 64tass 1.60.3243 and about no other assembler. ca65, ACME and WDC's own `WDC02AS` may collapse differently, spell the prefixes differently, or reverse `MVN` differently | **Recorded, by design.** 64tass is the project's only assembler oracle and is already a Klaus prerequisite. Nothing here should be read as a statement about 65816 assembly syntax in general |
| 4 | **The round-trip listing's width directives are an assumption about how phase 7e's harness will be written**, not a measurement of it. §15.3's four runs each declared one `.as`/`.al` and one `.xs`/`.xl` at the top of the listing and never changed width | **Recorded as a constraint on task 4.** It is measured that this *works*; it is not measured that any other arrangement does. `.autsiz` specifically must not be used — §15.1 shows it would let the `$C2`/`$E2` opcodes in the image change the width of everything after them |

**Not gaps, so nobody re-opens them:** the dialect name `65816` and the `--m65816` equivalent (§15.1,
both assembled); the width directives and the hard error on a mismatch (§15.1, ten assembled rows); that
`REP`/`SEP` never widen and do not track in the default mode (§15.1, four assembled rows including the
`.autsiz` contrast); every notation row in §15.1's table (each one assembled); the two collapse
boundaries at `$0100` and `$010000` (§15.2, six assembled rows); that forcing an already-necessary width
is a no-op (§15.2, four assembled rows); that `@b` is never needed and nothing collapses below two bytes
(§15.2, four assembled rows); `MVN`/`MVP`'s reversed operands (§15.1, two assembled rows agreeing with
§14.3's vector reading); `PER`/`BRL`'s displacement rule (§15.1, eight assembled rows across four
addresses); the empty ambiguity set and the covered count of 256 (§15.3, 576 swept combinations); and
that all 256 round-trip exactly at all four widths (§15.3, four complete assembled listings).
