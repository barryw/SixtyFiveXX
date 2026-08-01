# Experiment: what does patching just `$075C` buy?

**Question.** If the one documented BRK/NMI B-flag check at `$075C` is neutralised (not
the core, not the test file — only an in-memory patch of the assembled image), how far
past it does Klaus's interrupt test get? Does the `B`-set condition cascade?

**Answer.** It gets **192 more cycles** (2,721 → 2,913) and trips a **second, distinct**
trap at `$06F3` — the very next check after the one just neutralised, in the same
sub-test, caused by the identical single hijack event. That second trap was **not**
patched: it is a different invariant than the documented B-flag check, so it falls
outside this experiment's mandate. `$06F5` (success) is **not** reached.

This corroborates, at the byte level, what the prior investigation
(`docs/superpowers/investigations/investigation-075c.md`, section 1) already flagged from reading the
source alone: asm comments at lines 936–937 and 830 "describe one thing: the NMOS
BRK/NMI hijack." Patching only the first exposes the second immediately.

---

## 1. Harness

Standalone console project in a local scratch directory outside the repo, **not** added
to the solution, **no** edits under `src/` or `tests/`:

- `harness.csproj` — `net10.0` exe, `ProjectReference` to `src/SixtyFiveXX/SixtyFiveXX.csproj`.
- `FeedbackBus.cs` — verbatim copy (shape *and* polarity) of
  `tests/SixtyFiveXX.Conformance/FeedbackBus.cs`: written-1-asserts on `$BFFC`
  (bit 0 → IRQ, bit 1 → NMI, bit 7 → diagnostic stop), not inverted.
- `Program.cs` — loads the 64 KB image built by `klaus/build.sh`, patches the byte pairs
  in the `patches` list from `D0 FE` (`bne *`) to `EA EA` (`NOP NOP`), then runs the same
  loop as `KlausInterruptTests.cs` (`PC=$0400`, `S=$FD`, `P=U|I`, step until PC stalls
  or the core jams), and on trap prints the PC, cycle count, register file, and the
  matching `.lst` line(s) for that address.

Build/run (from the harness directory):

```
dotnet build -c Release
dotnet run -c Release --no-build
```

## 2. Site patched

| Address | Bytes | Justification |
| --- | --- | --- |
| `$075C` | `D0 FE` (`bne $075C`) → `EA EA` | asm 933–937 / lst 543–547 — `nmi_trap`'s B-flag check: `lda $102,x` / `and #break` / `#trap_ne ;unexpected B-flag! - this may fail on a real 6502` / `;due to a hardware bug on concurrent BRK & NMI`. This is the sole site in the whole 6502_interrupt_test.asm carrying that exact rationale (confirmed by `grep -n "hardware bug\|concurrent" 6502_interrupt_test.asm`: it appears exactly once). Branch not taken on the pass path, so NOPping it falls through to `$075E` exactly as a passing check would. |

Harness output confirming the patch:

```
Patching $075C: D0 FE -> EA EA (NOP NOP)
```

## 3. Trap reached next

```
Trapped at $06F3 after 2,913 cycles.
IsJammed: False
DiagnosticStop: False
A=01 X=09 Y=9C S=FD P=20
```

`.lst` line for `$06F3` (and its immediate context, lst 465–484):

```
.06e4	8d fc bf	sta $bffc      sta I_port      ;interrupt next instruction plus outbound delay
.06e7	00		brk #		       brk
.06e8..06ef	                       inx  (x8)
.06f0	ad 03 02	lda $0203      lda I_src       ;test all done?
.06f3	d0 fe		bne $06f3      bne *           ;failed not equal (non zero)
```

Source (asm 809–831), the same sub-test whose failure at `$075C` was already
investigated:

```
;test overlapping NMI, IRQ & BRK
        ldx #0
        lda #7
        sta I_src
        ...
        #I_set 8         ;trigger NMI + IRQ
        brk
        inx (x8)
        lda I_src       ;test all done?
;may fail due to a bug on a real NMOS 6502 - NMI could mask BRK
        #trap_ne         ;lost an interrupt
```

**This was not patched.** Reasoning:

- It is mechanistically the direct, deterministic consequence of the *same single
  event* that caused `$075C`: one `STA $BFFC` write (asserting IRQ+NMI together) is
  immediately followed by one `BRK`. As already traced for `$075C`, the NMI edge lands
  before the intPoll boundary is next evaluated, so `BRK` is fetched and begins
  normally; `PushPBrk` pushes `P|B` before `VectorLo` hijacks the vector to `$FFFA`
  (`nmi_trap`). With `$075C` neutralised, `nmi_trap` runs to completion: it clears only
  the NMI bit of `I_src` (`and #$ff-4`, asm 939) and `rti`s. Because the hijack means
  `BRK`'s own vector fetch never lands in `irq_trap`/`brk_trap`, nothing ever clears
  `I_src`'s BRK-expected bit (bit 0, value 1). The IRQ pin, still asserted (only
  `NMI_bit` was cleared), is recognised separately at the next boundary as a plain IRQ
  and serviced by `irq_trap`, clearing bit 1. Net: `I_src` starts at `7`, ends at `1`
  (matches the harness's `A=01` at the trap) — exactly "lost an interrupt", exactly what
  asm 830's comment predicts ("NMI could mask BRK").
- Despite that shared root cause, it is a **different check** than the one this
  experiment was scoped to neutralise: it asserts "no interrupt was lost"
  (`I_src == 0`), not "B was not unexpectedly set". The task's guard rail is explicit
  and literal: "Do not patch any trap that is not the documented concurrent-BRK/NMI
  B-flag check." `$06F3` is not a B-flag check.
- The project's own prior investigation (`investigation-075c.md`, section 1) already
  identifies asm 830 as "a second acknowledgement of the same silicon behaviour" and
  groups it with asm 936–937 as describing "one thing: the NMOS BRK/NMI hijack" — but
  that document, too, stopped at flagging it, not at asserting the two checks are
  interchangeable for patching purposes.
- Per instructions: **stop and report**, not patch through it.

## 4. Final outcome

- `$06F5` (success) was **not** reached.
- Run halted at `$06F3` after **2,913 cycles** (not jammed, no diagnostic-stop bit
  raised — a clean self-loop trap, same shape as `$075C`).
- Net yield of patching the one documented `$075C` check: **192 additional cycles**
  (2,721 → 2,913) of test execution, covering zero additional sub-tests — the extra
  cycles are entirely inside the tail of the same "overlapping NMI, IRQ & BRK" sub-test
  that `$075C` already belongs to. No prior sub-test result changes.

## 5. Bearing on the test-green decision

This measures, rather than assumes, what "neutralise just this one check" would buy:
essentially nothing runs past it — the very same hijack event trips a second,
independently-documented-as-hardware-affected check 192 cycles later, and that one is
outside this experiment's patch mandate. The two options framed for the project were:

1. Assert the `$075C` trap and its exact cycle count (gates the first 2,720 cycles).
2. Neutralise just the `$075C` check and let the rest of the test run.

Option 2 does not, in practice, unlock "the rest of the test": it unlocks 192 more
cycles of the *same* sub-test before hitting a second self-documented NMOS limitation.
Reaching `$06F5` would additionally require a decision on `$06F3` (asm 830), which is a
distinct check from the one this experiment was authorized to touch.
