# Investigation: the BRK/NMI hijack window, and whether `Reset()` clears the NMI latch

Two open hardware-behaviour questions, settled from evidence. **Nothing in `src/` or `tests/`
was changed.** All observations of current behaviour come from a scratch copy of
`src/SixtyFiveXX/*.cs` outside the repo, driven by a throwaway harness (details in §0).

**Headline:** Q1 — yes, the window is genuinely one tick too wide; the fix is two lines and the
`$075C` / 2,721-cycle Klaus gate does **not** move. Q2 — yes, `Reset()` should clear
`_nmiPending`; one line, no existing test moves.

---

## 0. Method and licence note

- Scratch copy of the nine core `.cs` files plus `FeedbackBus.cs` in a local scratch
  directory outside the repo, patched with three runtime switches (`FixHijackWindow`,
  `FixSequencePoll`, `FixResetClearsNmi`) so every variant runs from one build. A second
  scratch project runs the repo's **unit** suite against that patched copy via a
  `[ModuleInitializer]`. The committed Conformance suite was not run; the Klaus
  binaries were read directly from
  `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.bin` and
  `tests/SixtyFiveXX.Conformance/.klaus-cache/6502_functional_test.bin` and driven by the
  harness's own copy of the runner logic.
- Baseline sanity: unpatched scratch copy reproduces **303/303 unit tests**, Klaus interrupt
  **`$075C` @ 2,721 cycles**, Klaus functional **`$3469` @ 96,241,367 cycles**.
- **Licence:** every source cited below is documentation (NESdev wiki, the Visual6502 wiki,
  Wikipedia). No emulator source was read for this investigation, so no text and no code from
  any implementation — permissive or otherwise — is reflected here.

---

## Question 1 — the BRK/NMI hijack window

### 1.1 Unambiguous cycle numbering

Cycle 1 = the BRK opcode fetch (the cycle with SYNC high). Visual6502's T-state labels line up
with the external cycles as follows; the mapping is pinned by
[6502 Interrupt Hijacking](https://www.nesdev.org/wiki/Visual6502wiki/6502_Interrupt_Hijacking),
which says RES "control[s] the fetch of the low byte of its vector in T5" and that the fetch it
controls happens "in the next cycle", i.e. T6 is the cycle in which the vector-low byte appears
on the pins.

| ext. cycle | T-state | bus | this core's micro-op |
| --- | --- | --- | --- |
| 1 | T1 | R `PC` — fetch opcode `$00`, PC++ | (`FetchOpcode`) |
| 2 | T2 | R `PC` — signature byte, discarded, PC++ | `BrkPad` |
| 3 | T3 | W `$0100,S` — push PCH | `PushPch` |
| 4 | T4 | W `$0100,S` — push PCL | `PushPcl` |
| **5** | **T5** | W `$0100,S` — push P (B set) — *and internally commands the vector-low address* | `PushPBrk` |
| 6 | T6 | R vector low — *and internally commands the vector-high address*; set I | `VectorLo` |
| 7 | T0 | R vector high | `VectorHi` |

The hardware IRQ/NMI sequence is the identical 7 cycles with the two PC reads discarded and B
pushed clear (`IrqEntry` = `IntDummy, PushPch, PushPcl, PushPInt, VectorLo, VectorHi` plus the
free read in `FetchOpcode`), so every cycle number below applies to both.

### 1.2 Where silicon actually commits the vector

> "For NMI to hijack an IRQ (or a soft BRK instruction), stage 1 of its recognition may appear
> as late as T5 phase 1 during the BRK instruction's execution. This requires the NMI line to go
> down no later than the end of clock cycle T4 (up to just before clocking in T5 phase 1). With
> the node ~NMIG low during T5 and T6, the low and high bytes of the vector for indirect jump to
> the NMI handler are fetched together."
> — [Visual6502 wiki, *6502 Interrupt Hijacking*](https://www.nesdev.org/wiki/Visual6502wiki/6502_Interrupt_Hijacking)

So the decision is **not** taken at the vector read. `~NMIG` (NMI recognition stage 1) must
already be low **at the φ1 edge that starts cycle 5**, because cycle 5 is the cycle that
internally forms the vector-low address. In external terms: **the NMI pin must fall during
cycle 4 or earlier.**

The same page documents the *mechanism* that enforces the cutoff, which is what makes this a
hard edge rather than a soft one:

> "the 6502 has some explicit engineering to prevent an NMI half-hijacking IRQ and BRK. The node
> chain ~VEC, pipe~VEC, 1578, 1368 is the secret sauce… node 1368 is kept grounded from T5 phase
> 2 through T0 phase 1. That prevents NMI low from being passed through to cause the NMI to be
> recognized at stage 1 and affect the vector fetches. NMI stage 1 is not allowed to be
> recognized through all of T6 and T0 at the tail end of BRK execution. As long as the NMI line
> stays down, the NMI will finally be stage-1-recognized at T1 phase 1. That will allow the
> first instruction of the IRQ/BRK handler to run before the BRK for the NMI is started."

Two consequences, both load-bearing:

1. An NMI edge landing in cycles 5, 6 or 7 **cannot** touch the vector — deliberately, by a
   dedicated transistor chain whose only purpose is to prevent a mixed `$FFFE`/`$FFFB` vector.
2. Such an NMI is not lost (in a real system): it is recognised at T1 φ1 of the handler's first
   instruction, so **exactly one handler instruction runs first**, then the NMI's own sequence
   starts.

NESdev's page states the same window and places the marker between cycles 4 and 5 in its own
tick table:

> "if NMI is asserted during the first four ticks of a BRK instruction, the BRK instruction will
> execute normally at first … but execution will branch to the NMI vector instead of the IRQ/BRK
> vector"
> …
> ` 4  $0100,S  W  push PCL on stack, decrement S`
> `*** At this point, the signal status determines which interrupt vector is used ***`
> ` 5  $0100,S  W  push P on stack (with B flag set), decrement S`
> — [NESdev wiki, *CPU interrupts*](https://www.nesdev.org/wiki/CPU_interrupts)

and independently states the rule the core already implements correctly for the boundary poll —
"it's really the status of the interrupt lines at the *end of the second-to-last cycle* that
matters" — plus the guarantee in (2): "The interrupt sequences themselves do not perform
interrupt polling, meaning at least one instruction from the interrupt handler will execute
before another interrupt is serviced."

| source | strength |
| --- | --- |
| Visual6502 wiki, *6502 Interrupt Hijacking* / *…Recognition Stages and Tolerances* | **Strongest available.** Written from the transistor netlist plus simulator experiments, names the actual nodes (`~NMIG`, `~VEC`, `INTG`, `RESG`, 1578, 1368) and ships runnable JSSim permalinks for each case. |
| NESdev wiki, *CPU interrupts* | Strong, and *independent in wording only* — its interrupt section is explicitly derived from Visual6502. Its tick table is the clearest statement of the cut point. |
| Wikipedia, *Interrupts in 65xx processors* (cites the WDC datasheet) | Medium. Good for "NMOS and 6510 behave alike, CMOS fixed it"; says nothing about cycle counts. |
| Klaus's `6502_interrupt_test.asm` | Weak on this question. It never places an NMI edge inside BRK cycles 1–7; its overlapping test asserts both pins before the BRK begins. Confirmed empirically in §1.4. |

### 1.3 Is the difference real, or only a counting convention?

**Real.** Both directions of the check agree.

*Convention check.* This core has no φ1/φ2, so "the host called `SetNmi(true)` before tick N"
must be mapped to a pin event. Derive the mapping from a case the core already gets right: for
a 2-cycle instruction, `SetNmi(true)` before tick 2 causes a dispatch at the following boundary,
which on hardware requires the line low at φ2 of tick 1. So

> `SetNmi(true)` called before tick N  ≡  the pin fell during tick N−1  ≡  the internal signal is
> high from the start of tick N.

That is exactly NESdev's "the internal signal goes high during φ1 of the cycle that follows the
one where the edge is detected", and exactly Visual6502's "stage 0 at any cycle φ2, stage 1 at
any cycle + 1, φ1". The two numbering systems therefore *do* line up, and can be compared
directly:

| | hijack happens when… | last pin edge that still hijacks |
| --- | --- | --- |
| **NMOS silicon** | `~NMIG` low by T5 φ1 → latched before **tick 5** | during **tick 4** |
| **this core today** | `_nmiPending` true when `VectorLo` runs → latched before **tick 6** | during **tick 5** |

*Mechanism check.* The core's cutoff is "before the `VectorLo` micro-op reads `_vector`", i.e.
at the start of cycle 6. Silicon's cutoff is one cycle earlier, at the start of cycle 5, because
cycle 5 is where the address is formed, not where it is driven. The core is modelling the
address *read* where the hardware models the address *formation*. That is a genuine one-cycle
error, not a relabelling.

**Verdict: the core's window is wrong by exactly one tick — too generous.** The correct NMOS
window is BRK/interrupt-sequence **ticks 1–4** (equivalently: the NMI must be latched before
tick 5 begins). Confidence: **high**.

### 1.4 What this core actually does, measured

Harness sweep, BRK at `$0200`, NMI vector `$8000`, IRQ/BRK vector `$9000`, both handlers NOPs.
"before tick N" = `cpu.SetNmi(true)` between tick N−1 and tick N.

| NMI asserted before tick | today | with fix 1 | with fixes 1+2 | **hardware** |
| --- | --- | --- | --- | --- |
| 1 | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` |
| 2–4 | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` |
| **5** | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` | NMI `$FFFA` (last hijacking edge) |
| **6** | **NMI `$FFFA`** ✗ | IRQ `$FFFE`, NMI fires with **no** handler instruction ✗ | IRQ `$FFFE`, then 1 handler instruction, then NMI ✓ | IRQ `$FFFE`, then 1 handler instruction, then NMI |
| **7** | IRQ `$FFFE`, NMI fires with **no** handler instruction ✗ | same ✗ | IRQ `$FFFE`, 1 handler instruction, then NMI ✓ | as above |
| 8 | IRQ `$FFFE`, 1 handler instruction, then NMI ✓ | ✓ | ✓ | as above |

In every hijack case the pushed P byte is `$30` (B set) for BRK and `$20` (B clear) for a
hardware IRQ — unchanged by either fix, and correct. The same sweep run against the hardware
IRQ dispatch (`PushPInt` path) gives an identical table shifted onto that sequence's own cycle
numbers, which is expected: same silicon sequence.

Note row 7 (and row 6 under fix 1 alone): the current core also **violates the "at least one
handler instruction runs" guarantee**, because `Tick()` refreshes `_intPoll` on the sequence's
own final cycle (`VectorHi`). That is the same silicon mechanism — the 1368 blackout runs from
T5 φ2 through T0 φ1 precisely so that a late NMI cannot be recognised until the handler's first
fetch. Fixing the window without fixing the poll converts one defect into another, so the two
belong in the same change.

### 1.5 Does the window differ across `CpuVariant`?

| variant | window | confidence |
| --- | --- | --- |
| `Mos6502` | ticks 1–4 | **high** (Visual6502 netlist analysis) |
| `Mos6510` | ticks 1–4 — identical | **high**. Same NMOS core die plus an I/O port; Wikipedia's *Interrupt anomalies* section names "the NMOS 6502 and derivatives (e.g., 6510)" together for exactly this anomaly. |
| `Wdc65C02`, `Rockwell65C02`, `Synertek65C02` | **no hijack at all** — direction is well attested, the exact replacement behaviour is **not** | **medium on the direction, undetermined on the detail.** Wikipedia, citing the WDC datasheet: the NMOS "simultaneous assertion of a hardware interrupt line and execution of `BRK`… the `BRK` instruction will be ignored in such a case… These anomalies were corrected in all CMOS versions." What is *not* established by any source I found: whether the CMOS part completes the BRK to `$FFFE` and then takes the NMI, or defers, and at which cycle. Vendor differences between WDC/Rockwell/Synertek are in the opcode set (`RMB`/`SMB`/`BBR`/`BBS`, `WAI`/`STP`), not interrupt sequencing. The 65C02 also clears **D** on interrupt entry, which this core deliberately does not (`InterruptTests.Irq_DoesNotClearDecimalModeOnNmos`). |
| `W65C816` | emulation mode ≈ 65C02; native mode has separate `BRK` (`$FFE6`) and `IRQ` (`$FFEE`) vectors, which removes the ambiguity that makes the NMOS hijack observable at all | **low / out of scope.** |

**Shaping.** Nothing needs to be built for variants today. The fix below keeps the decision
inside the micro-op that the variant's own `MicroOpTable` emits (`PushPBrk` / `PushPInt`), so a
CMOS table can later emit a different P-push micro-op — or the same one under a variant flag —
without touching `Cpu.Tick`, `VectorLo`, or the sequence layout. Do **not** hoist the decision
into `Tick()`: that would put per-variant behaviour in the one place every variant shares.

### 1.6 Recommendation — Q1

**Correct behaviour (NMOS 6502 / 6510).** The NMI must be latched **before tick 5 of the
BRK/interrupt sequence** — i.e. the pin falls during ticks 1–4 — to redirect the vector. The
vector-address decision is committed on real silicon at **T5 φ1, the φ1 edge that begins cycle 5
(the P push)**; cycle 5 forms the vector-low address that appears on the pins in cycle 6. An NMI
edge in cycles 5–7 is blocked from recognition by the `~VEC`/`pipe~VEC`/1578/1368 chain until
T1 φ1 of the handler's first instruction, so that instruction runs and *then* the NMI is taken.

**Confidence: high.** Transistor-level analysis plus a second documentation source agreeing on
the same cut point, plus a named mechanism explaining why the boundary is exactly there.

**Minimal change (described, not applied) — two parts, one commit:**

*Part 1 — move the decision one cycle earlier (`src/SixtyFiveXX/Cpu.cs`).*

- In `case MicroOp.PushPBrk:` — hoist `_vector = IrqVector;` to the top of the case, and
  immediately after it add the hijack test `if (_nmiPending) { _nmiPending = false; _vector =
  NmiVector; }`, **before** the `_bus.Write`. (Before the write matters: it is what makes the
  test read the state as of the start of the cycle, so a bus-side-effect NMI raised by this very
  cycle cannot be seen — the same reason `_intPoll` is computed before `Execute`.)
- In `case MicroOp.PushPInt:` — add the same test, guarded as today by
  `&& _vector == IrqVector` so an NMI sequence cannot hijack itself.
- In `case MicroOp.VectorLo:` — delete the hijack block entirely; it becomes just the read. Its
  comment moves to the two push cases, and gains the cycle-5 rationale.

Exactly one cycle's behaviour changes: **cycle 6 (`VectorLo`) no longer looks at
`_nmiPending`**, and cycle 5 does instead. Nothing else in the sequence moves; cycle counts,
bus accesses, addresses and the pushed P byte are all identical.

*Part 2 — the sequence's last cycle must not poll (same file).* In `Tick()`, after
`var micro = _ops[_mpc];`, add `if (micro == MicroOp.VectorHi) _intPoll = false;`. This is the
model of the 1368 blackout and of NESdev's "the interrupt sequences themselves do not perform
interrupt polling". Without it, Part 1 turns a wrong vector into a skipped handler instruction.

**Tests that would pin it.**

- New, discriminating (fails today, passes after Part 1) — in `HijackTests.cs`, a sibling of
  `Nmi_HijacksABrkInProgress`: tick BRK **five** times, then `SetNmi(true)`, then tick twice.
  Assert `PC == 0x9000` (BRK's own vector stood) and that the pushed byte at `$01FB` still has
  B set. Today this yields `$8000`.
- New, for Part 2: same setup, then `Step()` once and assert `PC == 0x9001` (the handler's
  first NOP ran), and `Step()` again and assert `PC == 0x8000` (only now the NMI).
- Keep `Nmi_HijacksABrkInProgress` exactly as it is — it asserts NMI after **four** ticks, which
  is the last hijacking edge on real silicon, so it becomes the boundary's other side.
- Optionally rename `NmiArrivingAfterTheVectorReadDoesNotHijack` → `...AfterThePushOfP...`,
  since the cutoff is no longer the vector read.

**Existing-test knock-on (measured, patched scratch copy of the full unit suite):**

| change | unit suite |
| --- | --- |
| Part 1 alone | **303 / 303 pass** — no existing test edited |
| Part 2 alone | 302 / 303 — `HijackTests.NmiArrivingDuringItsOwnSequenceSurvivesToFireAgain` fails |
| Parts 1 + 2 | 302 / 303 — same single failure |

That one test encodes the divergent behaviour in its own comment ("the poll it leaves behind is
already hot… so the second dispatch fires on the very next fetch"). It must be updated, not
worked around: after Part 2 the first `Step()` runs the NOP at `$0201` (PC → `$0202`) and the
second `Step()` dispatches the NMI to `$8000`. That is one extra `Step()` and one changed
expectation — and it is the more correct behaviour, per the blackout quote in §1.2.

**Klaus gate: does NOT move.** Measured for all four flag combinations:

| | `6502_interrupt_test` | `6502_functional_test` |
| --- | --- | --- |
| today | `$075C` @ **2,721** | `$3469` @ 96,241,367 |
| Part 1 | `$075C` @ **2,721** | `$3469` @ 96,241,367 |
| Part 2 | `$075C` @ **2,721** | `$3469` @ 96,241,367 |
| Parts 1+2 | `$075C` @ **2,721** | `$3469` @ 96,241,367 |

The reason is structural, not luck: Klaus's overlapping sub-test asserts both pins from the
write cycle of the `STA $BFFC` that *precedes* the BRK, so the latch is set before BRK tick 1 —
comfortably inside both the old and the new window (see
`docs/superpowers/investigations/investigation-075c.md` §2). Nothing in his program places an
NMI edge inside a BRK's cycles 5–7. **So the fix and the gate are independent and can land
separately.** The `NmosTrapCycles = 2721` assertion in `KlausInterruptTests.cs` needs no edit.

**External oracle, if one is ever wanted:** blargg's `cpu_interrupts_v2` test ROM, subtests
`3-nmi_and_brk` and `4-irq_and_dma`, are the community's discriminator for this exact window
(NESdev's *CPU interrupts* names it as the test for this behaviour). Not run here; it needs an
NES PPU/APU harness this project does not have.

---

## Question 2 — does `Reset()` clear a pending NMI latch?

### 2.1 The physical mechanism

RESET is not a separate sequence on this die. Stage 3 of interrupt recognition is:

> "RESG or INTG high causes the fetch cycle to prepare to substitute a BRK instruction into the
> IR in phase 2 instead of the opcode that was read from memory in phase 1."
> — [Visual6502 wiki, *6502 Interrupt Recognition Stages and Tolerances*](https://www.nesdev.org/wiki/Visual6502wiki/6502_Interrupt_Recognition_Stages_and_Tolerances)

A reset therefore *runs a BRK*, and the same page tabulates what that BRK clears, unconditionally
and without reference to which vector was selected:

> **Tabulation of what BRK clears**
> `T6 phase 2` — NMI/IRQ stage 2 cleared (INTG low)…
> `T0 phase 1` — RES stage 2 cleared (RESG low) *if* RES stage 1 was cleared…; **NMI stage 1
> cleared (~NMIG high)**. This always disconnects NMI/IRQ stage 2 recognition from the
> clock-T0/branch-T2 signal chain; IRQ disable bit set.

`~NMIG` is precisely this core's `_nmiPending` — the same page calls it "stage 1 of pending NMI
status", and it "is also responsible for selecting the NMI vector used by BRK". Note the
asymmetry in that table: the RESG clear is conditional, the `~NMIG` clear is not. **The reset
sequence clears the pending-NMI latch at T0 φ1 — cycle 7 of the reset — exactly as an IRQ, NMI
or soft BRK sequence does.**

The re-arming rule is separate and is *not* affected:

> "NMI falling edge detection reset requires the action of BOTH the BRK instruction above AND
> releasing the NMI line to go high. They may happen in either order… Edge detection is reset by
> NMI already being up when the BRK instruction clears NMI stage 1 recognition, or by coming up
> after that."

So a line held low across a reset yields **no** post-reset NMI: the pending latch is cleared by
the reset's own BRK, and the edge detector cannot re-fire until the line goes high and falls
again. That is exactly the core's existing `_nmiLine` + `_nmiPending` split, and it means the
fix must clear `_nmiPending` **only** — touching `_nmiLine` would re-arm the edge detector and
manufacture a phantom NMI on the host's next `SetNmi(true)` call for a pin that never moved.

Also relevant: a real RES is held for thousands of cycles ("Manual resets may keep the RES line
down at least a few tenths of a second"), during which the clock is pinned in T0 T+ states and
fetch is suppressed; the BRK that finally runs when RES is released is the one that clears
`~NMIG`. Any NMI edge that arrives during the held reset is therefore also discarded. There is
no realistic timing in which a pre-reset NMI survives.

### 2.2 Today's behaviour, measured

Harness: `SetNmi(true)`, then `Reset()`, then `Step()` (reset sequence, PC → `$7000`), then
`Step()`.

- today: second `Step()` → `$8000` — **the pre-reset NMI fires immediately after reset.**
- with the fix: second `Step()` → `$7001` — the first instruction at the reset vector runs.

### 2.3 The power-on case

At power-on the core has `_nmiLine == false` (pin deasserted) and `_nmiPending == false`. A host
whose first call is `SetNmi(true)` therefore latches an NMI. That is correct **iff** the pin
really was high beforehand, which is the normal case for both a NES and a C64 (NMI is pulled
high; CIA2's `/IRQ` output is open-drain and idle-high, and the RESTORE key's monostable is at
rest). If a host's pin is genuinely low at power-on and the host's first call is `SetNmi(true)`,
the core invents an edge — but that is a host modelling error (the host must report the true
level from tick 0), not a core defect, and clearing `_nmiPending` in `Reset()` makes it
harmless anyway, since a correctly-written host resets before ticking.

For the planned **C64 personality** this matters in one concrete, reachable way. NMI there is
CIA2 (`/IRQ` → `/NMI`) plus the RESTORE key. Today, pressing RESTORE (or having a CIA2 timer or
serial NMI armed) at the moment the reset button is pressed leaves `_nmiPending` set, so the
core vectors through `$FFFA` immediately after reset — into the KERNAL's NMI entry at `$FE43`,
which almost immediately does `JMP ($0318)` through a **RAM vector that the reset routine has
not initialised yet**. That is a jump to garbage on a warm reset with RESTORE held: a real C64
does not do this, and the failure would be intermittent and extremely hard to attribute. That
alone justifies the change independently of the silicon argument.

### 2.4 Recommendation — Q2

**Correct behaviour: `Reset()` clears the pending NMI latch. It does not touch the line state.**

**Confidence: high** on the silicon (an explicit, unconditional entry in the Visual6502 wiki's
"what BRK clears" table, combined with the same wiki's statement that a reset *is* a BRK).
**Medium** only on one detail of placement — see the divergence note below — and that detail is
not worth code today.

**Minimal change (described, not applied).** In `src/SixtyFiveXX/Cpu.cs`, `Reset()`:

```
_s.I = true;
_nmiPending = false;   // <- added
_vector = ResetVector;
```

One line. Do **not** clear `_nmiLine`: on silicon the edge detector's fired state persists while
the pin is low, and clearing the level would re-arm it.

**Known divergence, deliberately accepted.** Hardware clears `~NMIG` at T0 φ1 — the *seventh*
cycle of the reset sequence — whereas the one-liner clears it when `Reset()` is called. They
differ only for an NMI edge asserted by the host during the 7 reset cycles: hardware discards it
(edges in reset cycles 1–4) or defers it by one instruction (cycles 5–7); the one-liner keeps it
and fires at the first boundary. Modelling that faithfully means clearing in the sequence's tail
instead, which cannot simply be hung on `MicroOp.VectorHi` because that micro-op is shared with
BRK/IRQ/NMI, where an unconditional clear would change other behaviour (§2.5). A 7-cycle window
requiring the host to move the NMI pin mid-reset is not worth a new micro-op. Worth a
`ponytail:` comment naming the ceiling.

**The test that would pin it** — new in `tests/SixtyFiveXX.Tests/HijackTests.cs`, alongside
`Reset_IsNotDivertedByAPendingNmi`, which already documents this as unsettled and can keep its
existing assertions:

```
[Fact] public void Reset_ClearsAPendingNmi()
    - Machine(0xEA); RESET -> $7000, NMI -> $8000
    - cpu.SetNmi(true);            // latch before the reset
    - cpu.Reset(); cpu.Step();     // 7 cycles, PC == $7000
    - cpu.Step();                  // the instruction at $7000, NOT the NMI
    - Assert.Equal(0x7001, cpu.State.PC);
```
plus, to prove the line state was not disturbed: `Assert.True(cpu.NmiLine)` if the line was left
asserted, and a follow-up `SetNmi(false); SetNmi(true); Step(); Step();` reaching `$8000` to show
a genuine new edge still works.

**Existing-test knock-on: none.** Measured on the patched scratch copy: **303 / 303 unit tests
pass** with this change alone. `HijackTests.Reset_IsNotDivertedByAPendingNmi` continues to pass
unchanged (it asserts only `$7000` and 7 cycles, and its comment explicitly declines to assert
the latch's fate — that comment can now be replaced by the citation above).

**Klaus gate: no effect.** Klaus's interrupt test never calls `Reset()`; the runner sets PC
directly. Measured: `$075C` @ 2,721 with the fix on.

### 2.5 Adjacent finding (flagged, not claimed, no action recommended)

The same Visual6502 clearing rules imply a third, subtler divergence that this investigation did
**not** set out to settle: because `~NMIG` is cleared at T0 φ1 of *every* interrupt sequence and
edge detection is only re-armed once the line has gone high *and* the BRK has done its clear, a
second NMI edge that arrives **during an NMI's own sequence** (line released and re-asserted
before that sequence's cycle 7) appears to be lost on silicon, whereas this core keeps it —
`HijackTests.NmiArrivingDuringItsOwnSequenceSurvivesToFireAgain` asserts the core's behaviour.
Likewise the "lost NMI" case (a pulse inside cycles 5–7 that is released before the handler's
first fetch) is not modelled: this core keeps it pending, hardware drops it. Both require
sub-microsecond NMI pulses that no real peripheral generates — the Visual6502 authors call them
"us hackers… driv[ing] the simulator with transient interrupt signals that act like flaky
hardware". Recorded here so the next person does not rediscover them as bugs; settling either
would need its own investigation and a JSSim run.

---

## Sources

- [NESdev wiki — *CPU interrupts*](https://www.nesdev.org/wiki/CPU_interrupts)
- [Visual6502 wiki — *6502 Interrupt Hijacking*](https://www.nesdev.org/wiki/Visual6502wiki/6502_Interrupt_Hijacking) (nesdev.org mirror; visual6502.org's own cert has expired)
- [Visual6502 wiki — *6502 Interrupt Recognition Stages and Tolerances*](https://www.nesdev.org/wiki/Visual6502wiki/6502_Interrupt_Recognition_Stages_and_Tolerances)
- [Visual6502 wiki — *6502 Timing of Interrupt Handling*](https://www.nesdev.org/wiki/Visual6502wiki/6502_Timing_of_Interrupt_Handling)
- [Wikipedia — *Interrupts in 65xx processors*](https://en.wikipedia.org/wiki/Interrupts_in_65xx_processors) (cites the WDC W65C816S datasheet)
- `docs/superpowers/investigations/investigation-075c.md` — the prior `$075C` investigation whose §7 raised Q1
