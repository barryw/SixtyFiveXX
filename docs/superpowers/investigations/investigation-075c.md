# Investigation: Klaus interrupt test traps at `$075C`

**Question.** Is the trap at `$075C` after 2,721 cycles correct behaviour for a faithful
NMOS 6502 core, or a defect in this core's interrupt timing?

**Verdict: (A) expected.** Confidence: **high**.

The core's interrupt poll and its BRK/NMI hijack both match documented NMOS 6502
behaviour. Klaus's check at `$075C` encodes an idealised expectation that no faithful
NMOS core can satisfy at the timing his own test sets up, and his source says so in a
comment on the very line that traps.

---

## 1. The sub-test

The failing sub-test is the last one in the test program, `;test overlapping NMI, IRQ & BRK`
— `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` lines **809–831**,
assembled at `$06C5`–`$06F5`
(`tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.lst` lines **465–484**).

Source (asm 809–820):

```
;test overlapping NMI, IRQ & BRK
        ldx #0
        lda #7
        sta I_src
        lda #$ff        ;measure timing
        sta nmi_count
        sta irq_count
        sta brk_count
        #push_stat 0
        #I_set 8         ;trigger NMI + IRQ
        brk
```

`#I_set 8` expands (lst 468–474) to the assembled sequence that arms **both** pins in one
store:

```
.06da	ad fc bf	lda $bffc       ;turn on interrupt by bit
.06dd	29 7f		and #$7f        ;and #I_filter
.06df	09 03		ora #$03        ;ora #(1<<IRQ_bit|1<<NMI_bit)
.06e1	28		plp
.06e2	48		pha
.06e3	08		php
.06e4	8d fc bf	sta $bffc       ;interrupt next instruction plus outbound delay
.06e7	00		brk
```

So the whole event is: a 4-cycle `STA $BFFC` writes `$03`, asserting IRQ **and** NMI on
its **final (write) cycle**, and the very next instruction is `BRK`.

The trap is in the NMI handler (asm 933–937, lst 545–548):

```
.0756	ba		tsx
.0757	bd 02 01	lda $0102,x     ;test break on stack
.075a	29 10		and #$10        ;and #break
.075c	d0 fe		bne $075c       ;#trap_ne
```

with Klaus's own comment on the trap macro at asm line 936–937:

```
        #trap_ne         ;unexpected B-flag! - this may fail on a real 6502
                         ;due to a hardware bug on concurrent BRK & NMI
```

and a second acknowledgement of the same silicon behaviour eleven lines later, at asm 830,
guarding the `lda I_src` "all three serviced" check that this run never reaches:

```
;may fail due to a bug on a real NMOS 6502 - NMI could mask BRK
        #trap_ne         ;lost an interrupt
```

Both comments describe one thing: the NMOS BRK/NMI hijack.

## 2. Cycle-level trace of this core

Captured with a read-only scratch harness (a copy of `src/SixtyFiveXX/*.cs` in a local
scratch directory outside the repo; no repo edits). It reproduces the committed runner
exactly: unmodified path gives `$075C` at 2,721 cycles.

```
c 2647  FETCH PC=$06E4  R $06E4=$8D     STA abs, cycle 1
c 2648        PC=$06E5  R $06E5=$FC     cycle 2 (addr lo)
c 2649        PC=$06E6  R $06E6=$BF     cycle 3 (addr hi)  <-- penultimate cycle
c 2650        PC=$06E7  W $BFFC=$03     cycle 4 (write)    <-- IRQ+NMI asserted here
c 2651  FETCH PC=$06E7  R $06E7=$00     BRK fetched, NOT replaced by an interrupt
c 2652        PC=$06E8  R $06E8=$E8     BrkPad
c 2653        PC=$06E9  W $01FD=$06     PushPch
c 2654        PC=$06E9  W $01FC=$E9     PushPcl
c 2655        PC=$06E9  W $01FB=$30     PushPBrk  <-- P pushed as $30: B set, U set
c 2656        PC=$06E9  R $FFFA=$39     VectorLo  <-- HIJACK: _vector switched to NmiVector
c 2657        PC=$06E9  R $FFFB=$07     VectorHi
c 2658  FETCH PC=$0739  R $0739=$08     nmi_trap entered
...
c 2716        PC=$075A  R $01FB=$30     lda $0102,X (X=$F9) reads the pushed status
c 2717  FETCH PC=$075A  R $075A=$29     and #$10 -> $10, non-zero
c 2719  FETCH PC=$075C                  bne * — trapped, 2721 cycles
```

**The micro-op that pushed B set is `MicroOp.PushPBrk`** —
`src/SixtyFiveXX/Cpu.cs:601`:

```csharp
case MicroOp.PushPBrk:
    _bus.Write(0x0100 + _s.S, (byte)(_s.P | Flag.B | Flag.U));
```

reached from BRK's own sequence (`src/SixtyFiveXX/MicroOpTable.cs:263-267`). The vector is
then redirected by `MicroOp.VectorLo` (`src/SixtyFiveXX/Cpu.cs:617`) without rewriting the
already-pushed byte. Exactly the intended hijack.

Two facts to hold on to:

1. The interrupt was **not** taken at the `STA` → `BRK` boundary, because `_intPoll` for
   that boundary was computed at the start of cycle 2650 — before the write that asserts
   the pins.
2. Having missed the boundary, the NMI latch was set before BRK cycle 1, so `VectorLo`
   hijacked, leaving `$30` (B set) on the stack.

## 3. What real NMOS hardware does at this timing

### 3.1 Poll timing — the core is correct

NESdev wiki, *CPU interrupts*
(<https://www.nesdev.org/wiki/CPU_interrupts>):

> "Many references will claim that interrupts are polled during the last cycle of an
> instruction, but this is true only when talking about the output from the edge and level
> detectors. As can be deduced from above, it's really the status of the interrupt lines at
> the **end of the second-to-last cycle** that matters."

That is precisely this core's model: `_intPoll` is evaluated at the *start* of the final
cycle, which is the same instant as the end of the penultimate cycle. The same rule is
implemented in Mesen (`_prevRunIrq`/`_prevNeedNmi`, carried one cycle behind the live pin
state) — behaviour only, no text taken.

Now apply it. The pins here are driven by the `STA`'s **own** write cycle. Data is only on
the bus during φ2 of that write cycle, so the external latch physically cannot change
`/NMI` any earlier than φ2 of the `STA`'s final cycle. The boundary decision uses the
sample from the *previous* cycle. Therefore **no real 6502 can take this NMI at the
`STA` boundary** — the `BRK` opcode is always fetched and always begins executing.

Real hardware is in fact slightly *later* than this core, never earlier: the NESdev page
also notes the edge detector's output "goes high during φ1 of the cycle following the one
where the edge is detected", and any real port has an output delay on top (which is what
Klaus's own comment "plus outbound delay" refers to). Later never helps; see §4.

### 3.2 The hijack and the B flag — the core is correct

Same page, on interrupt hijacking:

> "if NMI is asserted during the first four ticks of a BRK instruction, the BRK instruction
> will execute normally at first ... but execution will branch to the NMI vector instead of
> the IRQ/BRK vector" — and the status word is "pushed with the **B** flag set".

In this run the NMI edge lands *before* BRK tick 1, so it is inside that window with a
cycle to spare. Hardware pushes `$30` and vectors through `$FFFA`. The core does exactly
that. `tests/SixtyFiveXX.Tests/HijackTests.cs::Nmi_HijacksABrkInProgress` already asserts
this pair (`$8000` vector, `Flag.B` still set at `$01FB`).

So on real NMOS silicon the NMI handler sees B set and Klaus's `#trap_ne` fires. The
`$075C` trap is the documented hardware behaviour, and Klaus flagged it as such.

## 4. The one-cycle discriminator

I swept the moment the pins change relative to the `STA`'s write, holding everything else
identical. `shift = 0` is what the core does today and reproduces the committed result
byte for byte, which validates the harness.

| shift | pin edge occurs during | result |
| --- | --- | --- |
| −6 … −1 | before / during `STA` cycle 3 (penultimate or earlier) | **PASS** `$06F5`, 3,016 cycles |
| **0** | **`STA` cycle 4 — the write (today)** | **trap `$075C`, 2,721 cycles** |
| +1 … +4 | BRK ticks 1–4 | trap `$075C`, 2,721 cycles |
| +5 | BRK tick 5 (`PushPBrk`) | trap `$075C`, 2,721 cycles |
| +6 … +12 | BRK tick 6 (`VectorLo`) or later | **PASS** `$06F5`, 3,016 cycles |

Both escape hatches exist, and both are physically unreachable at this timing:

- **shift −1 (earlier).** This requires `/NMI` to be low by the end of the `STA`'s
  *penultimate* cycle — i.e. one cycle **before the CPU writes the byte that asserts it**.
  Impossible. A one-cycle-earlier poll is not a plausible hardware model; it is the
  off-by-one that the NESdev "second-to-last cycle" rule explicitly warns against, and
  adopting it would break the CLI/SEI delay behaviour the core currently gets right.
- **shift +6 (later).** This requires ~6 cycles of port output delay between the CPU's
  write and the pin actually moving. A 6522/6821-class port moves its pin within the same
  cycle; six cycles is not a real latency. And if Klaus's own hardware had had it, he would
  not have written the "may fail on a real 6502" comment on this exact check.

So the trap is stable across the entire physically-realisable timing range (shift 0…+5),
which is the opposite of an off-by-one signature. This is the strongest evidence for (A).

## 5. Verdict

**(A) Expected.** High confidence.

- The core's poll timing matches the documented "end of the second-to-last cycle" rule.
- The core's hijack matches the documented NMOS behaviour, including leaving B set.
- The `STA`-drives-its-own-pin timing makes a clean NMI unreachable on any real 6502.
- Klaus documented the expected failure on the trapping line itself, and again at asm 830.

What would falsify this: a run of Klaus's interrupt test on real NMOS silicon (or a
visual6502 transistor-level simulation of the `$06E4`–`$06E7` sequence) reaching `$06F5`.
I consider that very unlikely given §3, but it is the only thing that would settle it
absolutely.

## 6. What to do about a test a faithful NMOS core cannot pass

`tests/SixtyFiveXX.Conformance/KlausInterruptTests.cs` currently asserts
`Assert.Equal(SuccessAddress, cpu.State.PC)` with `SuccessAddress = 0x06F5`. That
assertion can never hold. Options:

1. **Delete or `Skip` the test.** Throws away 2,720 cycles of genuine, passing interrupt
   coverage — the only independent oracle this project has for IRQ/NMI. Reject.
2. **Patch the ported `.asm` to skip the final sub-test.** Breaks the README's promise that
   "only directives were changed", and forks a GPL test program. Reject.
3. **Make `FeedbackBus` delay the pins by 6+ cycles so the test passes.** The sweep shows
   this works. It is also fudging the hardware model to satisfy a test. Reject explicitly —
   it would silently widen the gap between the model and the silicon.
4. **Expect `$075C`, and pin the prefix with an exact cycle count.** Recommended.

**Recommendation (option 4), described not applied:** in `KlausInterruptTests.cs`, replace
the success-trap constant and assertion with

- `Assert.Equal(0x075C, cpu.State.PC)` — the known NMOS BRK/NMI-hijack divergence, and
- `Assert.Equal(2721, cpu.Cycles)` — the run is fully deterministic, so exact equality
  makes the whole 2,720-cycle prefix a regression gate: any timing change anywhere earlier
  in the test moves this number.

Keep the existing `IsJammed` / `DiagnosticStop` / cycle-ceiling assertions unchanged, and
document in the XML comment (and `klaus/README.md`'s address table) that `$06F5` is
unreachable on NMOS, citing Klaus's own asm lines 936–937 and 830 plus the NESdev
interrupt-hijacking section. That converts an impossible pass criterion into a precise
statement of what the core is certified to do, without weakening the gate.

## 7. Secondary finding (not this bug — worth its own look)

The sweep shows this core hijacks when the NMI edge lands anywhere in BRK ticks **1–5**;
NESdev states the hardware window is "the first four ticks". The cutoff here is "before
`MicroOp.VectorLo` executes on tick 6" (`src/SixtyFiveXX/Cpu.cs:617`), and
`HijackTests.cs::NmiArrivingAfterTheVectorReadDoesNotHijack` encodes exactly that. So the
window may be one tick too generous.

It does not affect this trap — the NMI here arrives before BRK tick 1 either way — and
resolving it needs its own evidence (blargg's `cpu_interrupts_v2` "3-nmi_and_brk" is the
usual oracle, and hardware's own φ1 propagation delay may account for tick 5 legitimately).
Flagged, not claimed.
