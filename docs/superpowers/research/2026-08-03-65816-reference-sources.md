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
truncated to 8 bits.

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

Two conflicts surfaced while extracting the above. Both are recorded here because in both cases the
outlier is the book, and a later reader holding only the book would otherwise "correct" the
implementation into a bug.

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
| Direct-page page-wrap condition | `E == 1 && DL == $00`, keeping `DH`. Not `D == 0`. | 2.2, §5.1.1 + appendix |
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
- **Note 17** — mode-dependent RMW direction. Phase 7c, with the read-modify-write opcodes; noted now
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
