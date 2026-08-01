# SixtyFiveXX Phase 2b — Interrupts, RDY and SO

> **Errata (post-merge).** Four code snippets below were superseded during implementation
> or final review; the shipped code, not this plan, is the source of truth. (1) The poll
> placement: `_intPoll` is assigned only on cycles that continue an in-progress instruction
> (`_mpc >= 0`), not "immediately after `_cycles++`" as Tasks 1 and 4 show it. (2) Dispatch
> enters at `_table.IrqEntry` with no `+1`, not `IrqEntry + 1` as Tasks 1 and 2 show it. (3)
> The hijack guard shipped as `_nmiPending && _vector == IrqVector` (Task 3 proposed
> `_vector != NmiVector`) — and a later fix moved it again: that guard now lives only at
> `MicroOp.PushPInt`; `MicroOp.PushPBrk` needs no guard at all, because it is the only site
> that assigns `_vector = IrqVector`, so `_vector == IrqVector` would be a tautology there.
> (4) The feedback port is written-1-asserts, not written-0-asserts as Task 6's
> `FeedbackBus.Write` snippet has it — see `FeedbackBus.cs`'s own remarks for why.
>
> **Errata 2 (the hijack window moved one tick earlier).** Two more statements above no
> longer describe the shipped hijack test, for the same reason as (3). "The interrupt
> model" section (line ~57) says NMI hijacks a BRK/IRQ sequence "before the vector is
> read"; the File Structure table (line ~68) describes the `MicroOpTable.cs` change as an
> "NMI re-check before the vector read". Both described the window as Task 3 shipped it —
> the hijack test at `MicroOp.VectorLo`. A later hardware-fidelity fix moved that test one
> cycle earlier, out of `VectorLo` and into `MicroOp.PushPBrk` and `MicroOp.PushPInt` (the
> P-push cycle, before the stack write), because real NMOS silicon commits the vector at
> T5 φ1: the P-push cycle *forms* the vector-low address that the following cycle only
> reads off the pins. `MicroOp.VectorHi` now also forces `_intPoll = false`, modelling the
> interrupt-recognition blackout that keeps a late NMI from being recognised until the
> handler's first instruction. See
> `docs/superpowers/investigations/investigation-window-and-reset.md` for the evidence and
> `tests/SixtyFiveXX.Tests/HijackTests.cs` for the tests pinning both edges of the window.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the NMOS 6502 as a chip — IRQ and NMI with hardware-correct sampling and edge latching, BRK/NMI hijacking, the RDY halt line, and the SO pin — validated by cycle-exact unit tests and by Klaus Dormann's interrupt test.

**Architecture:** The interrupt sequence already exists in `MicroOpTable.IrqEntry` (6 micro-ops plus the boundary cycle = 7). This phase adds the *pins* that drive it, the *poll* that decides when it fires, and the *vector selection* that distinguishes IRQ from NMI from BRK. RDY becomes a gate in the tick loop rather than a micro-op.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit 2.9.3 (plain asserts), 64tass (for assembling the ported Klaus test).

## Global Constraints

- Target framework `net10.0`. `Nullable` enabled. Warnings as errors.
- `src/SixtyFiveXX` has **zero** NuGet dependencies. Test projects may take them.
- Licence MIT. All original work. Only permissively-licensed implementations may be consulted, and only for behaviour, never text.
- Every public member of `src/SixtyFiveXX` needs an XML doc comment — `GenerateDocumentationFile` is on with warnings as errors.
- XML doc `cref` to a type that does not exist yet is CS1574 → a build error. Use plain `<c>...</c>`.
- A `public` method cannot take `internal` parameter types (CS0051). Such a test method must be declared `internal`; xUnit still runs it.
- **Scratch fields are declared on first use.** No `#pragma warning disable` anywhere.
- **The core invariant:** one `Tick()` = one clock cycle = at most one bus access. RDY is the sole exception in spirit — a halted cycle still drives the address bus, so it still performs exactly one read.
- **The 2,560,000 Harte vectors and the Klaus functional test must stay green after every task.** They are the regression net. Harte's vectors contain no interrupt lines, so they cannot validate this phase — but they will catch any damage to the 256 opcodes.
- **Re-read the file header before editing a file.** Five of eight findings in Phase 2a's final review were comments that were true when written and went stale as later tasks landed. If a summary or remark you are editing near is now wrong, fix it in the same commit.

## Established facts — verified before this plan was written, do not re-derive

- **Harte's 6502 v1 vectors have no interrupt fields.** The JSON carries only `pc, s, a, x, y, p, ram` and a cycle list. `SingleStepTests/65x02` contains only `6502/v1`; there is no interrupt-aware set anywhere in that organisation.
- **Klaus ships exactly two prebuilt binaries** — `6502_functional_test.bin` and `65C02_extended_opcodes_test.bin`. There is no prebuilt `6502_interrupt_test.bin`, in the upstream repo or in the forks checked.
- **`6502_interrupt_test.a65` is 1,025 lines of AS65 source**, GPL-licensed, and its configuration is:
  - feedback port `I_port = $BFFC`, no DDR, `I_drive = 1` (open collector)
  - `IRQ_bit = 0`, `NMI_bit = 1` — bit 0 of the port drives IRQ, bit 1 drives NMI
  - `I_filter = $7F` — bit 7 set means "diagnostic stop"
  - `D_clear = 0` — NMOS: the decimal flag is **not** cleared on interrupt entry
  - `zero_page = $A`, `data_segment = $200`, `code_segment = $400`
  - success and failure are both `jmp *` self-loops; success is the one reached from line 830
- **64tass 1.60 rejects the AS65 source on directive syntax only** (`if`, `macro`, `endm`), not on any 6502 instruction. The port is mechanical: directives change, the 6502 code does not.
- **Assemblers already installed on this machine:** `64tass` 1.60, `ca65` 2.19, `acme` 0.97, `xa` 2.4.1.

## The interrupt model

**Polling.** A real 6502 samples the interrupt lines during φ2 of the penultimate cycle of each instruction. This core polls at the **start of every cycle**, storing the result in `_intPoll`. At an instruction boundary the value in `_intPoll` is the one computed at the start of the final cycle — which is the same instant as φ2 of the penultimate cycle. That equivalence is what makes the following well-known behaviours fall out for free rather than needing special cases:

- **`CLI` does not let a pending IRQ in immediately.** `CLI` is two cycles; both polls happen before its effect, so the boundary sees `I` still set. The IRQ is taken after the *next* instruction.
- **`SEI` does not keep a pending IRQ out.** Both polls happen before `I` is set, so an IRQ already asserted is still taken at that boundary.
- The same reasoning covers `PLP` and `RTI`.

**NMI** is edge-triggered: a false→true transition on the line latches `_nmiPending`, which survives until the NMI is serviced. Asserting an already-high line does nothing. NMI is never blocked by `I` and takes priority over IRQ.

**Hijacking.** If NMI becomes pending during a BRK or IRQ sequence before the vector is read, the vector fetched is NMI's. Klaus's test exercises this directly.

**RDY** halts the processor on read cycles only; writes complete regardless. A halted cycle still drives the address bus.

**Decimal.** On NMOS the `D` flag is **not** cleared on interrupt entry (`D_clear = 0`). The 65C02 does clear it — that is Phase 4's problem, not this one.

## File Structure

| File | Change |
| --- | --- |
| `src/SixtyFiveXX/Cpu.cs` | Pins, poll, dispatch, vector selection, RDY gate |
| `src/SixtyFiveXX/MicroOpTable.cs` | Fix `PushPInt`'s vector contract; NMI re-check before the vector read |
| `tests/SixtyFiveXX.Tests/InterruptTests.cs` | IRQ, NMI, priority, the CLI/SEI/PLP timing quirks |
| `tests/SixtyFiveXX.Tests/HijackTests.cs` | BRK/NMI hijacking |
| `tests/SixtyFiveXX.Tests/PinTests.cs` | RDY and SO |
| `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` | The 64tass port, committed |
| `tests/SixtyFiveXX.Conformance/klaus/build.sh` | Assembles it |
| `tests/SixtyFiveXX.Conformance/FeedbackBus.cs` | 64 KB bus with the `$BFFC` feedback register wired to the pins |
| `tests/SixtyFiveXX.Conformance/KlausInterruptTests.cs` | The runner |

---

### Task 1: IRQ — pins, polling, and dispatch

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/MicroOpTable.cs`
- Test: `tests/SixtyFiveXX.Tests/InterruptTests.cs`

**Interfaces:**
- Consumes: `MicroOpTable.IrqEntry`, `MicroOp.PushPInt`, `MicroOp.VectorLo`, `MicroOp.VectorHi`, `_vector`, `IrqVector`, `NmiVector`.
- Produces: `public void SetIrq(bool asserted)`; `public bool IrqLine { get; }`; private `_irqLine`, `_intPoll`.

The `IrqEntry` sequence already exists and is six micro-ops long. Phase 1 built it but nothing ever dispatched to it, and Phase 2a's review found that `MicroOp.PushPInt` sets `I` but **not** `_vector` — so a dispatcher that jumped straight to `IrqEntry` on a freshly-reset CPU would still hold `ResetVector` and vector the handler through `$FFFC`. Fixing that contract is part of this task.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/InterruptTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class InterruptTests
{
    /// <summary>A CPU with an IRQ handler vector at $9000 and NOPs to execute.</summary>
    private static (Cpu<FlatBus> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, program);
        ram[0xFFFE] = 0x00;
        ram[0xFFFF] = 0x90;   // IRQ/BRK vector -> $9000
        ram[0xFFFA] = 0x00;
        ram[0xFFFB] = 0x80;   // NMI vector -> $8000
        cpu.State.P = Flag.U; // I clear, interrupts enabled
        return (cpu, ram);
    }

    [Fact]
    public void Irq_IsNotTakenWhileTheInterruptDisableFlagIsSet()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA);
        cpu.State.I = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x0202, cpu.State.PC);   // both NOPs ran, no vector taken
    }

    [Fact]
    public void Irq_IsTakenAtTheNextInstructionBoundary()
    {
        var (cpu, ram) = Machine(0xEA, 0xEA);
        cpu.State.S = 0xFD;
        cpu.SetIrq(true);

        cpu.Step();                            // the NOP completes first
        Assert.Equal(0x0201, cpu.State.PC);

        var cycles = cpu.Step();               // then the interrupt sequence

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
        Assert.True(cpu.State.I);              // I is set on entry
        Assert.Equal(0xFA, cpu.State.S);       // three pushes
        Assert.Equal(0x02, ram[0x01FD]);       // PCH of the return address
        Assert.Equal(0x01, ram[0x01FC]);       // PCL
    }

    [Fact]
    public void Irq_PushesStatusWithTheBreakFlagClear()
    {
        var (cpu, ram) = Machine(0xEA);
        cpu.State.S = 0xFD;
        cpu.State.C = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        var pushed = ram[0x01FB];
        Assert.Equal(0, pushed & Flag.B);      // B clear distinguishes IRQ from BRK
        Assert.Equal(Flag.U, pushed & Flag.U); // U always set
        Assert.Equal(Flag.C, pushed & Flag.C);
    }

    [Fact]
    public void Irq_DoesNotClearDecimalModeOnNmos()
    {
        var (cpu, _) = Machine(0xEA);
        cpu.State.D = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.True(cpu.State.D);              // NMOS leaves D alone; the 65C02 clears it
    }

    [Fact]
    public void Irq_IsLevelSensitiveAndStopsFiringWhenTheLineIsReleased()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA);
        cpu.SetIrq(true);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);

        cpu.SetIrq(false);
        cpu.State.I = false;
        cpu.State.PC = 0x0201;

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);    // no second dispatch
    }

    [Fact]
    public void Cli_DelaysAPendingIrqByOneInstruction()
    {
        // CLI polls before its own effect, so the IRQ is taken after the NEXT instruction.
        var (cpu, _) = Machine(0x58, 0xEA, 0xEA);   // CLI, NOP, NOP
        cpu.State.I = true;
        cpu.SetIrq(true);

        cpu.Step();                                  // CLI
        Assert.Equal(0x0201, cpu.State.PC);
        Assert.False(cpu.State.I);

        cpu.Step();                                  // the NOP runs, not the handler
        Assert.Equal(0x0202, cpu.State.PC);

        cpu.Step();                                  // now the handler
        Assert.Equal(0x9000, cpu.State.PC);
    }

    [Fact]
    public void Sei_DoesNotPreventAnAlreadyPendingIrq()
    {
        // SEI polls before its own effect, so an IRQ asserted beforehand still lands.
        var (cpu, _) = Machine(0x78, 0xEA);          // SEI, NOP
        cpu.SetIrq(true);

        cpu.Step();                                  // SEI
        Assert.True(cpu.State.I);

        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);          // taken anyway
    }

    [Fact]
    public void IrqLine_ReportsTheCurrentPinState()
    {
        var (cpu, _) = Machine(0xEA);

        Assert.False(cpu.IrqLine);
        cpu.SetIrq(true);
        Assert.True(cpu.IrqLine);
        cpu.SetIrq(false);
        Assert.False(cpu.IrqLine);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter InterruptTests -v q`
Expected: FAIL — compile error, `SetIrq` does not exist.

- [ ] **Step 3: Add the pin state and the poll to `src/SixtyFiveXX/Cpu.cs`**

Fields, next to the existing ones:

```csharp
    /// <summary>Current level on the IRQ pin. Level-sensitive, not latched.</summary>
    private bool _irqLine;

    /// <summary>
    /// Interrupt poll result, recomputed at the start of every cycle. At an instruction
    /// boundary this holds the value from the start of the final cycle, which is the same
    /// instant a real 6502 samples during phase 2 of the penultimate cycle.
    /// </summary>
    private bool _intPoll;
```

The public pin:

```csharp
    /// <summary>The current level on the IRQ pin.</summary>
    public bool IrqLine => _irqLine;

    /// <summary>
    /// Drives the IRQ pin. The line is level-sensitive: while it is held asserted and the
    /// interrupt-disable flag is clear, an interrupt is taken at each instruction boundary.
    /// </summary>
    public void SetIrq(bool asserted) => _irqLine = asserted;
```

- [ ] **Step 4: Poll at the start of every cycle, and dispatch at the boundary**

In `Tick()`, immediately after `_cycles++` and **before** the `_mpc < 0` branch:

```csharp
        // Poll before doing any work this cycle. At an instruction boundary the value
        // left here is the one taken at the start of the final cycle — the same instant
        // real hardware samples. This is what makes CLI delay a pending IRQ by one
        // instruction while SEI fails to block one, with no special case for either.
        _intPoll = _irqLine && !_s.I;
```

Then in `FetchOpcode()`, before reading the opcode:

```csharp
        if (_intPoll)
        {
            // Take the interrupt instead of an instruction. The sequence's first cycle
            // is a read at PC that does not advance it, so perform it here and enter the
            // table at its second micro-op.
            _bus.Read(_s.PC);
            _vector = IrqVector;
            _mpc = _table.IrqEntry + 1;
            return;
        }
```

`_table.IrqEntry` is the offset of the sequence's `IntDummy`; entering at `+1` skips it because this method has just performed that read itself.

- [ ] **Step 5: Fix `PushPInt`'s vector contract**

`MicroOp.PushPInt` sets `I` but not `_vector`, which Phase 2a's review identified as a latent trap. Now that a dispatcher exists, make the contract explicit rather than implicit. In `src/SixtyFiveXX/Cpu.cs`'s `PushPInt` case, leave the vector alone but correct its comment:

```csharp
            case MicroOp.PushPInt:
                // Deliberately does not set _vector: only the dispatcher knows whether
                // this is an IRQ or an NMI, and they use different vectors. FetchOpcode
                // sets it before entering this sequence.
                _bus.Write(0x0100 + _s.S, (byte)((_s.P | Flag.U) & ~Flag.B));
                _s.S--;
                _s.I = true;
                break;
```

Also update the `IrqEntry` XML doc in `src/SixtyFiveXX/MicroOpTable.cs`, which currently says the caller must set the vector — that is now true and satisfied, so make it read as a live contract rather than a warning about unreachable code.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter InterruptTests -v q`
Expected: PASS — 8 passed.

- [ ] **Step 7: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS — 258/258. Harte's vectors never assert the IRQ line, so `_intPoll` is always false there; if conformance breaks, the poll or the dispatch is firing when it should not.

- [ ] **Step 8: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests/InterruptTests.cs
git commit -m "feat: add the IRQ pin with hardware-correct interrupt polling"
```

---

### Task 2: NMI — edge latching and priority

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`
- Test: `tests/SixtyFiveXX.Tests/InterruptTests.cs` (extend)

**Interfaces:**
- Consumes: everything from Task 1.
- Produces: `public void SetNmi(bool asserted)`; `public bool NmiLine { get; }`; private `_nmiLine`, `_nmiPending`.

NMI differs from IRQ in three ways, all observable: it is **edge-triggered** (a false→true transition latches a pending interrupt that survives the line going low again), it is **never blocked by `I`**, and it **takes priority** over a simultaneous IRQ.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SixtyFiveXX.Tests/InterruptTests.cs`:

```csharp
    [Fact]
    public void Nmi_IsTakenEvenWithInterruptsDisabled()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.State.I = true;
        cpu.SetNmi(true);

        cpu.Step();
        var cycles = cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_IsEdgeTriggeredAndFiresOnlyOncePerTransition()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA, 0xEA);
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);

        // The line is still high, but there has been no new edge.
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);   // no second dispatch
    }

    [Fact]
    public void Nmi_LatchesEvenIfTheLineIsReleasedBeforeTheBoundary()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.SetNmi(true);
        cpu.SetNmi(false);                     // pulse: high then low

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);    // the latch survived the release
    }

    [Fact]
    public void Nmi_FiresAgainAfterANewEdge()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA, 0xEA);
        cpu.SetNmi(true);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);

        cpu.SetNmi(false);
        cpu.SetNmi(true);                      // a fresh edge
        cpu.State.PC = 0x0201;
        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);
    }

    [Fact]
    public void Nmi_TakesPriorityOverASimultaneousIrq()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.SetIrq(true);
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);    // the NMI vector, not $9000
    }

    [Fact]
    public void Nmi_PushesStatusWithTheBreakFlagClear()
    {
        var (cpu, ram) = Machine(0xEA);
        cpu.State.S = 0xFD;
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0, ram[0x01FB] & Flag.B);
    }

    [Fact]
    public void NmiLine_ReportsTheCurrentPinState()
    {
        var (cpu, _) = Machine(0xEA);

        Assert.False(cpu.NmiLine);
        cpu.SetNmi(true);
        Assert.True(cpu.NmiLine);
    }
```

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter InterruptTests -v q`
Expected: FAIL — compile error, `SetNmi` does not exist.

- [ ] **Step 3: Add the NMI pin and latch to `src/SixtyFiveXX/Cpu.cs`**

Fields:

```csharp
    /// <summary>Current level on the NMI pin, tracked only to detect a rising edge.</summary>
    private bool _nmiLine;

    /// <summary>
    /// Latched by a rising edge on NMI and cleared when the interrupt is serviced. NMI is
    /// edge-triggered, so this survives the line going low again and holding it high does
    /// not produce a second interrupt.
    /// </summary>
    private bool _nmiPending;
```

The public pin:

```csharp
    /// <summary>The current level on the NMI pin.</summary>
    public bool NmiLine => _nmiLine;

    /// <summary>
    /// Drives the NMI pin. NMI is edge-triggered: only a low-to-high transition latches an
    /// interrupt, and that latch survives the line being released. Holding the line high
    /// produces exactly one interrupt, not a stream of them.
    /// </summary>
    public void SetNmi(bool asserted)
    {
        if (asserted && !_nmiLine) _nmiPending = true;
        _nmiLine = asserted;
    }
```

- [ ] **Step 4: Include NMI in the poll and give it priority**

Change the poll line in `Tick()` to:

```csharp
        _intPoll = _nmiPending || (_irqLine && !_s.I);
```

And in `FetchOpcode()`, select the vector by priority and consume the latch:

```csharp
        if (_intPoll)
        {
            _bus.Read(_s.PC);
            // NMI outranks IRQ, and servicing it consumes the latch. IRQ is level-sensitive
            // and so needs no clearing — it fires again next boundary if still asserted.
            if (_nmiPending)
            {
                _nmiPending = false;
                _vector = NmiVector;
            }
            else
            {
                _vector = IrqVector;
            }
            _mpc = _table.IrqEntry + 1;
            return;
        }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter InterruptTests -v q`
Expected: PASS — 15 passed.

- [ ] **Step 6: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS — 258/258.

- [ ] **Step 7: Commit**

```bash
git add src/SixtyFiveXX/Cpu.cs tests/SixtyFiveXX.Tests/InterruptTests.cs
git commit -m "feat: add the NMI pin with edge latching and priority over IRQ"
```

---

### Task 3: BRK and NMI hijacking

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`
- Test: `tests/SixtyFiveXX.Tests/HijackTests.cs`

**Interfaces:**
- Consumes: everything from Task 2, plus `MicroOp.PushPBrk`, `MicroOp.VectorLo`.
- Produces: no new public API — a behaviour change inside `VectorLo`.

If an NMI becomes pending while a BRK or IRQ sequence is running, and it does so before the vector is read, the vector fetched is NMI's. The interrupt that was already in progress is not abandoned — its pushes have happened and its `B` flag is whatever it pushed — but control lands in the NMI handler.

This is the behaviour that makes an NMI arriving mid-BRK land at `$FFFA` while still having pushed `B` set.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/HijackTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class HijackTests
{
    private static (Cpu<FlatBus> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, program);
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;   // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;   // NMI     -> $8000
        cpu.State.P = Flag.U;
        cpu.State.S = 0xFD;
        return (cpu, ram);
    }

    [Fact]
    public void Brk_VectorsThroughFffeWhenNoNmiArrives()
    {
        var (cpu, _) = Machine(0x00);   // BRK

        var cycles = cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_HijacksABrkInProgress()
    {
        var (cpu, ram) = Machine(0x00);   // BRK

        // Drive BRK's first four cycles, then assert NMI before the vector read.
        cpu.Tick();   // opcode fetch
        cpu.Tick();   // BrkPad
        cpu.Tick();   // push PCH
        cpu.Tick();   // push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // push P
        cpu.Tick();   // vector low
        cpu.Tick();   // vector high

        Assert.Equal(0x8000, cpu.State.PC);          // NMI's vector, not BRK's
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);  // but BRK's pushed B is still set
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void HijackingConsumesTheNmiLatch()
    {
        var (cpu, _) = Machine(0x00, 0xEA, 0xEA);

        cpu.Tick(); cpu.Tick(); cpu.Tick(); cpu.Tick();
        cpu.SetNmi(true);
        cpu.Tick(); cpu.Tick(); cpu.Tick();
        Assert.Equal(0x8000, cpu.State.PC);

        // The latch was consumed, so the next boundary must not dispatch again.
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void NmiArrivingAfterTheVectorReadDoesNotHijack()
    {
        var (cpu, _) = Machine(0x00, 0xEA);

        for (var i = 0; i < 6; i++) cpu.Tick();   // through the vector-low read
        cpu.SetNmi(true);
        cpu.Tick();                                // vector high

        Assert.Equal(0x9000, cpu.State.PC);        // BRK's own vector stood
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter HijackTests -v q`
Expected: FAIL — `Nmi_HijacksABrkInProgress` expects `$8000`, gets `$9000`.

- [ ] **Step 3: Re-check NMI at the vector read**

In `src/SixtyFiveXX/Cpu.cs`, change the `MicroOp.VectorLo` case:

```csharp
            case MicroOp.VectorLo:
                // An NMI that arrives before the vector is read hijacks the sequence in
                // progress: the pushes already happened with whatever B flag the original
                // interrupt used, but control lands in the NMI handler.
                if (_nmiPending && _vector != NmiVector)
                {
                    _nmiPending = false;
                    _vector = NmiVector;
                }
                _tmp = _bus.Read(_vector);
                break;
```

The `_vector != NmiVector` guard stops an NMI sequence from consuming a *second* latch that arrived during its own run — that one must survive to fire again.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter HijackTests -v q`
Expected: PASS — 4 passed.

- [ ] **Step 5: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS — 258/258. Harte's vectors include all 10,000 `$00` BRK cases and never assert NMI, so `_nmiPending` is always false there.

- [ ] **Step 6: Commit**

```bash
git add src/SixtyFiveXX/Cpu.cs tests/SixtyFiveXX.Tests/HijackTests.cs
git commit -m "feat: add NMI hijacking of an in-progress BRK or IRQ sequence"
```

---

### Task 4: The RDY and SO pins

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/MicroOp.cs`
- Test: `tests/SixtyFiveXX.Tests/PinTests.cs`

**Interfaces:**
- Consumes: `MicroOp`, `Tick()`.
- Produces: `public void SetRdy(bool ready)`; `public bool Ready { get; }`; `public void SetSo()`; private `_rdy`; `internal static bool IsWriteCycle(MicroOp)`.

**RDY** halts the processor on **read** cycles only — a write in progress completes. This is how a VIC-II steals the bus for a badline: it pulls RDY low three cycles ahead, and the CPU keeps running until its next read. Phase 8's C64 personality drives this pin, which is why it is modelled now rather than faked later.

A halted cycle still drives the address bus, so it still performs exactly one read — the one-access invariant is preserved, not excepted.

**SO** sets the overflow flag. On real hardware it is edge-triggered on the SO pin and takes effect at the end of φ1; modelling it as an immediate set is indistinguishable at instruction granularity.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/PinTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class PinTests
{
    [Fact]
    public void Rdy_HaltsTheProcessorOnAReadCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);   // LDA $1234
        ram[0x1234] = 0x42;

        cpu.Tick();                 // opcode fetch
        cpu.SetRdy(false);

        var pcBefore = cpu.State.PC;
        for (var i = 0; i < 10; i++) cpu.Tick();

        Assert.Equal(pcBefore, cpu.State.PC);      // no progress while halted
        Assert.False(cpu.State.A == 0x42);

        cpu.SetRdy(true);
        cpu.Step();

        Assert.Equal(0x42, cpu.State.A);           // resumes and completes
    }

    [Fact]
    public void Rdy_StillDrivesTheAddressBusWhileHalted()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0xAD, 0x34, 0x12);

        cpu.Tick();
        var afterFetch = log.Count;
        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(afterFetch + 2, log.Count);   // one access per halted cycle
        Assert.All(log, a => Assert.False(a.IsWrite));
    }

    [Fact]
    public void Rdy_DoesNotHaltAWriteCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x8D, 0x00, 0x30);   // STA $3000
        cpu.State.A = 0x5A;

        cpu.Tick();   // opcode
        cpu.Tick();   // address low
        cpu.Tick();   // address high
        cpu.SetRdy(false);
        cpu.Tick();   // the write must complete despite RDY being low

        Assert.Equal(0x5A, ram[0x3000]);
    }

    [Fact]
    public void Rdy_CountsHaltedCyclesAgainstTheCycleCounter()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(2, cpu.Cycles);
    }

    [Fact]
    public void Ready_ReportsTheCurrentPinState()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        Assert.True(cpu.Ready);
        cpu.SetRdy(false);
        Assert.False(cpu.Ready);
    }

    [Fact]
    public void So_SetsTheOverflowFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.V = false;

        cpu.SetSo();

        Assert.True(cpu.State.V);
    }

    [Fact]
    public void So_DoesNotDisturbAnyOtherFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.P = Flag.U | Flag.C | Flag.N;

        cpu.SetSo();

        Assert.Equal(Flag.U | Flag.C | Flag.N | Flag.V, cpu.State.P);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter PinTests -v q`
Expected: FAIL — compile error, `SetRdy` does not exist.

- [ ] **Step 3: Classify write cycles in `src/SixtyFiveXX/MicroOp.cs`**

RDY needs to know whether the cycle about to run is a read or a write. Add a lookup next to the enum, in the same file, so it cannot drift from the members it classifies:

```csharp
/// <summary>Classifies micro-ops by bus direction, for the RDY halt line.</summary>
internal static class MicroOps
{
    private static readonly bool[] Writes = BuildWriteTable();

    /// <summary>True when this micro-op drives a write. RDY does not halt a write cycle.</summary>
    public static bool IsWriteCycle(MicroOp op) => Writes[(int)op];

    private static bool[] BuildWriteTable()
    {
        var writes = new bool[Enum.GetValues<MicroOp>().Length];

        foreach (var op in new[]
                 {
                     MicroOp.ExecWrite, MicroOp.RmwModifyWrite, MicroOp.RmwWrite,
                     MicroOp.PushPch, MicroOp.PushPcl, MicroOp.Push,
                     MicroOp.PushPBrk, MicroOp.PushPInt,
                 })
        {
            writes[(int)op] = true;
        }

        return writes;
    }
}
```

- [ ] **Step 4: Gate the tick loop on RDY**

In `src/SixtyFiveXX/Cpu.cs`, add the field and the public pins:

```csharp
    /// <summary>Level on the RDY pin. Low halts the processor on read cycles.</summary>
    private bool _rdy = true;

    /// <summary>The current level on the RDY pin. True means the processor runs freely.</summary>
    public bool Ready => _rdy;

    /// <summary>
    /// Drives the RDY pin. Pulling it low halts the processor on its next read cycle; a
    /// write already in progress completes. A halted processor keeps driving the address
    /// bus, which is how a video chip steals cycles without disturbing the CPU's state.
    /// </summary>
    public void SetRdy(bool ready) => _rdy = ready;

    /// <summary>
    /// Pulses the SO pin, setting the overflow flag. Nothing clears it but an instruction
    /// that writes P.
    /// </summary>
    public void SetSo() => _s.V = true;
```

In `Tick()`, after the poll and before the `_mpc < 0` branch:

```csharp
        if (!_rdy && !IsWriteCycleNext())
        {
            // Halted: re-drive the address bus without advancing. One access, as always.
            _bus.Read(_mpc < 0 ? _s.PC : _addr);
            return;
        }
```

And the helper next to it:

```csharp
    /// <summary>True when the cycle about to run is a write. RDY cannot halt a write.</summary>
    private bool IsWriteCycleNext() => _mpc >= 0 && MicroOps.IsWriteCycle(_ops[_mpc]);
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter PinTests -v q`
Expected: PASS — 7 passed.

If `Rdy_StillDrivesTheAddressBusWhileHalted` fails on the address it reads rather than the count, adjust the address expression in Step 4 — the count and the read/write direction are what the test pins, and the exact halted address is not something the available suites can settle.

- [ ] **Step 6: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS — 258/258. `_rdy` defaults to true and no conformance test lowers it.

- [ ] **Step 7: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests/PinTests.cs
git commit -m "feat: add the RDY halt line and the SO pin"
```

---

### Task 5: Port Klaus's interrupt test to 64tass

**Files:**
- Create: `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm`, `tests/SixtyFiveXX.Conformance/klaus/build.sh`, `tests/SixtyFiveXX.Conformance/klaus/README.md`

**Interfaces:**
- Produces: a 64 KB binary at `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.bin`, gitignored and built on demand.

Klaus's interrupt test is the only independent validation of 6502 interrupt behaviour that exists. It is distributed only as AS65 source, and AS65 is a Windows binary — so this task ports the directives to 64tass, which is already installed and available in CI.

**The 6502 instructions must not change.** Only directives differ:

| AS65 | 64tass |
| --- | --- |
| `label macro` … `endm` | `label .macro` … `.endm` |
| `if expr` … `endif` | `.if expr` … `.endif` |
| `org $400` | `* = $400` |
| `ds n` | `.fill n` |
| `db`, `dw` | `.byte`, `.word` |
| macro invocation `name` | `#name` |

The source is GPL-3.0. It is a *test program* we assemble and execute; we do not link to it or derive our source from it, so its licence does not reach this project. Record that in the README, and keep Klaus's original copyright header intact at the top of the ported file.

- [ ] **Step 1: Fetch the original and confirm its configuration**

```bash
mkdir -p tests/SixtyFiveXX.Conformance/klaus
curl -sL -o /tmp/6502_interrupt_test.a65 \
  https://raw.githubusercontent.com/Klaus2m5/6502_65C02_functional_tests/master/6502_interrupt_test.a65
grep -nE "I_port|IRQ_bit|NMI_bit|I_drive|I_filter|D_clear|zero_page|data_segment|code_segment" \
  /tmp/6502_interrupt_test.a65 | head -12
```

Expected, and already verified — do not change any of these values, the runner in Task 6 depends on them:

```
I_port      = $bffc     ;feedback port address
I_drive     = 1         ;open collector
IRQ_bit     = 0         ;bit 0 drives IRQ
NMI_bit     = 1         ;bit 1 drives NMI
I_filter    = $7f       ;bit 7 = diag stop
D_clear     = 0         ;NMOS: D not cleared on interrupt
zero_page   = $a
data_segment = $200
code_segment = $400
```

- [ ] **Step 2: Port the directives**

Copy the source to `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm` and apply the directive translations in the table above. Keep every 6502 instruction, label and comment as-is, including the copyright header.

Work iteratively: run the assembler, fix the first error, repeat. 64tass reports one construct at a time and the errors are all directive syntax.

```bash
64tass --nostart --long-branch -o tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.bin \
       tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm
```

- [ ] **Step 3: Verify the output is a correct 64 KB image**

The assembled image must be exactly 65,536 bytes and must place code at `$0400`:

```bash
ls -l tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.bin
```

Expected: `65536` bytes. If 64tass emits a shorter file, add `* = $ffff` / `.byte 0` padding at the end, or assemble with an explicit full-image layout — the runner in Task 6 loads it straight into a 64 KB array and `FlatBus` rejects any other length.

**Record the entry address and the success trap address in the README**, read from the 64tass listing rather than assumed. Generate the listing with `-L` and find the address of the `success` macro's `jmp *`, exactly as was done for the functional test (whose success trap is `$3469`).

- [ ] **Step 4: Write `tests/SixtyFiveXX.Conformance/klaus/build.sh`**

```bash
#!/usr/bin/env bash
# Assembles Klaus Dormann's interrupt test, ported from AS65 to 64tass.
# The binary is not committed; run this to produce it.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v 64tass >/dev/null 2>&1; then
    echo "64tass not found. Install it (brew install 64tass / apt-get install 64tass)." >&2
    exit 1
fi

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
```

Make it executable: `chmod +x tests/SixtyFiveXX.Conformance/klaus/build.sh`

- [ ] **Step 5: Write the README recording provenance and licence**

`tests/SixtyFiveXX.Conformance/klaus/README.md`:

```markdown
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

`FeedbackBus` in the parent directory implements that register.

## Building

    ./build.sh

Requires `64tass`. The `.bin` and `.lst` outputs are gitignored.
```

- [ ] **Step 6: Gitignore the build outputs**

Add to `.gitignore`:

```gitignore
tests/SixtyFiveXX.Conformance/klaus/*.bin
tests/SixtyFiveXX.Conformance/klaus/*.lst
```

- [ ] **Step 7: Commit**

```bash
chmod +x tests/SixtyFiveXX.Conformance/klaus/build.sh
git add tests/SixtyFiveXX.Conformance/klaus .gitignore
git commit -m "build: port Klaus Dormann's interrupt test from AS65 to 64tass"
```

---

### Task 6: Run the Klaus interrupt test

**Files:**
- Create: `tests/SixtyFiveXX.Conformance/FeedbackBus.cs`, `tests/SixtyFiveXX.Conformance/KlausInterruptTests.cs`
- Modify: `.woodpecker.yml`

**Interfaces:**
- Consumes: `Cpu<TBus>`, `IBus`, `SetIrq`, `SetNmi`, `IsJammed`, the binary from Task 5.
- Produces: `struct FeedbackBus : IBus`; one xUnit fact.

The test drives its own interrupt pins by writing to `$BFFC`. Bit 0 drives IRQ, bit 1 drives NMI, and the register is open collector, meaning a **written 0 asserts** the corresponding pin. Bit 7 is a diagnostic-stop signal the test uses to say "something went wrong before the trap".

`FeedbackBus` therefore needs a reference back to the CPU so a write can move its pins — a genuine circular dependency, which is why the bus is constructed with a setter rather than through the constructor.

- [ ] **Step 1: Write `tests/SixtyFiveXX.Conformance/FeedbackBus.cs`**

```csharp
namespace SixtyFiveXX.Conformance;

/// <summary>
/// A 64 KB bus with the feedback register Klaus Dormann's interrupt test uses to drive
/// its own interrupt pins.
/// </summary>
/// <remarks>
/// The register lives at <c>$BFFC</c> and is open collector: a written <b>0</b> asserts
/// the corresponding pin. Bit 0 drives IRQ, bit 1 drives NMI. Bit 7 is a diagnostic-stop
/// signal the test raises when it detects a problem it cannot trap on.
/// </remarks>
public sealed class FeedbackBus : IBus
{
    /// <summary>Address of the feedback register.</summary>
    public const int FeedbackPort = 0xBFFC;

    private readonly byte[] _ram;
    private Cpu<RefBus>? _cpu;

    /// <summary>Wraps a 64 KB image.</summary>
    public FeedbackBus(byte[] ram)
    {
        ArgumentNullException.ThrowIfNull(ram);
        if (ram.Length != 0x10000)
            throw new ArgumentException($"Expected 65536 bytes, got {ram.Length}.", nameof(ram));
        _ram = ram;
    }

    /// <summary>True once the test has raised the diagnostic-stop bit.</summary>
    public bool DiagnosticStop { get; private set; }

    /// <summary>
    /// Connects the CPU whose pins this register drives. Set after construction because
    /// the CPU needs the bus and the bus needs the CPU.
    /// </summary>
    public void Attach(Cpu<RefBus> cpu) => _cpu = cpu;

    /// <inheritdoc />
    public byte Read(int address) => _ram[address & 0xFFFF];

    /// <inheritdoc />
    public void Write(int address, byte value)
    {
        address &= 0xFFFF;
        _ram[address] = value;

        if (address != FeedbackPort || _cpu is null) return;

        // Open collector: a written 0 pulls the line down, which is the asserted state.
        _cpu.SetIrq((value & 0x01) == 0);
        _cpu.SetNmi((value & 0x02) == 0);

        if ((value & 0x80) != 0) DiagnosticStop = true;
    }
}
```

- [ ] **Step 2: Write `tests/SixtyFiveXX.Conformance/KlausInterruptTests.cs`**

```csharp
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs Klaus Dormann's interrupt test. It is the only independent validation of 6502
/// interrupt behaviour available: the SingleStepTests vectors carry no interrupt lines,
/// so nothing else in this project's suites exercises IRQ or NMI against an external
/// oracle.
/// </summary>
public class KlausInterruptTests(ITestOutputHelper output)
{
    private const ushort StartAddress = 0x0400;
    private const long CycleCeiling = 100_000_000;

    /// <summary>
    /// The binary is built on demand by <c>klaus/build.sh</c> rather than committed. If it
    /// is missing the test fails with instructions rather than silently passing.
    /// </summary>
    private static string BinaryPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "klaus", "6502_interrupt_test.bin"));

    [Fact]
    public void InterruptTest_RunsToTheSuccessTrap()
    {
        Assert.True(File.Exists(BinaryPath),
            $"{BinaryPath} is missing. Build it with " +
            $"tests/SixtyFiveXX.Conformance/klaus/build.sh (requires 64tass).");

        var ram = File.ReadAllBytes(BinaryPath);
        Assert.Equal(0x10000, ram.Length);

        var feedback = new FeedbackBus(ram);
        var cpu = new Cpu<RefBus>(new RefBus(feedback));
        feedback.Attach(cpu);

        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        ushort previous = 0xFFFF;
        while (cpu.Cycles < CycleCeiling)
        {
            previous = cpu.State.PC;
            cpu.Step();

            if (cpu.State.PC == previous) break;   // a jmp * trap, success or failure
            if (cpu.IsJammed) break;
        }

        output.WriteLine($"Trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles.");

        Assert.False(cpu.IsJammed,
            $"The processor jammed at ${cpu.State.PC:X4}.");

        Assert.False(feedback.DiagnosticStop,
            $"The test raised its diagnostic-stop bit; trapped at ${cpu.State.PC:X4}.");

        Assert.True(cpu.Cycles < CycleCeiling,
            $"Did not terminate within {CycleCeiling:N0} cycles; last PC ${cpu.State.PC:X4}.");

        Assert.Equal(SuccessAddress, cpu.State.PC);
    }

    /// <summary>
    /// Address of the success trap, read from the 64tass listing that <c>klaus/build.sh</c>
    /// generates. Task 5's README records where it came from.
    /// </summary>
    private const ushort SuccessAddress = 0x0000;   // replace — see Step 2a below
}
```

- [ ] **Step 2a: Determine the success trap address and substitute it**

`SuccessAddress` is written above as `0x0000`, which is deliberately wrong so the test cannot
pass by accident. Get the real value from the listing Task 5's build produced:

```bash
grep -nE "jmp \*" tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.lst | tail -5
```

The success trap is the `jmp *` reached from the `success` macro at the end of the test —
the same structure as the functional test, whose success trap is `$3469`. Cross-check the
address against the source: it should be the `success` invocation near line 830 of
`6502_interrupt_test.asm`, not one of the many failure traps.

Replace the `0x0000` literal with that address and note it in `klaus/README.md`. **Do not
guess it and do not take it from the functional test** — the two programs are different
sizes and their traps are at different addresses.

- [ ] **Step 3: Build the binary and run the test**

```bash
tests/SixtyFiveXX.Conformance/klaus/build.sh
dotnet test tests/SixtyFiveXX.Conformance -c Release --filter KlausInterruptTests -v n
```

Expected: PASS, with the output line reporting a trap at the success address.

**If it traps elsewhere, that address is the diagnosis.** The generated `.lst` maps addresses to sub-tests; the surrounding source names what failed. Fix the core, never the test. A failure here is a genuine finding — nothing else in the project can catch an interrupt-timing defect.

- [ ] **Step 4: Add the build step to CI**

In `.woodpecker.yml`, the conformance stage must build the binary before running. Add to that step's `commands`, before the `dotnet test` line:

```yaml
      - apt-get update && apt-get install -y --no-install-recommends 64tass
      - tests/SixtyFiveXX.Conformance/klaus/build.sh
```

- [ ] **Step 5: Run everything**

Run: `dotnet test -c Release --filter "Category!=Performance" -v q`
Expected: PASS — unit tests, 2,560,000 Harte vectors, the Klaus functional test, and the Klaus interrupt test.

- [ ] **Step 6: Commit**

```bash
git add tests/SixtyFiveXX.Conformance .woodpecker.yml
git commit -m "test: run Klaus Dormann's interrupt test as an independent gate"
```

---

## Phase 2b complete

The NMOS 6502 is now complete as a chip: every opcode, plus IRQ, NMI, RDY and SO with
hardware-correct timing.

**Phase 3** — the disassembler and the sim6502 adapter swap — gets its own plan. Note that
`IExecutionBackend` has no interrupt surface at all, so nothing in this phase is required
by that swap; interrupts are here because the C64 personality in Phase 8 cannot exist
without them, and because a core missing them is not a 6502.

## Self-review notes

Checked against `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md`:

- **Spec §5.3** lists the cycle-accuracy requirements: IRQ/NMI sampled on the penultimate
  cycle (Task 1's poll model), NMI edge-latched (Task 2), NMI hijacking a BRK (Task 3),
  RDY halting read cycles only (Task 4), SO setting overflow (Task 4). All five delivered.
- **Spec §10 phase 2** — undocumented opcodes landed in Phase 2a; this plan completes the
  row. The `ICpuVariant` refactor was moved to Phase 4, where the first real variant
  difference appears.
- **Spec §7** names Bruce Clark's decimal test as a phase 2 gate. It is not distributed
  prebuilt, and Harte's vectors already cover decimal ADC/SBC/ARR/RRA/ISC per-cycle across
  roughly 80,000 D-set vectors. Recorded as deferred in the spec, not silently dropped.
- **Global constraints** — no new dependencies in `src/SixtyFiveXX`; no pragma; each of
  `_irqLine`, `_intPoll`, `_nmiLine`, `_nmiPending`, `_rdy` is introduced by the task that
  first reads it.

Type consistency verified: `Cpu<TBus>`, `FlatBus`, `RefBus`, `CpuState`, `Flag`, `MicroOp`,
`MicroOpTable`, `TestMachine.Flat`/`Logged`, `BusAccess` all match their existing
definitions.

Two things this plan deliberately leaves undetermined rather than guessing, because only
the port can settle them: the Klaus interrupt test's **success trap address** (Task 5
Step 3 reads it from the generated listing) and whether 64tass emits a full 64 KB image
without extra padding (Task 5 Step 3 checks and says what to do if not).
