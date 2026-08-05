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
