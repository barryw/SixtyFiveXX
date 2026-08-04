# Phase 7a — Widening the register file

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Widen `CpuState` to hold the 65816's register file, and add the one bus concept the 65816 needs that no 8-bit core has — a cycle that drives an address without accessing memory — **with zero behaviour change to the five cores that already pass.**

**Architecture:** `A`, `X`, `Y` and `S` become `ushort`; `DP`, `DBR`, `PBR` and `E` are added. The 8-bit cores keep byte semantics through four private byte-wide shim properties on `Cpu`, so every existing use site reads and writes the low byte exactly as it does today. This is not cosmetic: `S--` on a `ushort` field wraps at 16 bits and puts the stack pointer at `$FFFF`, which is measurably wrong (see Established facts). `IBus` gains a defaulted `Internal(int)` method.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7a".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md` — §3.1 is why `Flag.X` is bit 4 and `Flag.M` is bit 5, and it must be read before touching the flag constants.

**Scope:** No 65816 code whatsoever. No opcode, no addressing mode, no variant struct, no opcode table. This phase adds capacity and nothing that uses it.

**Deviation from the spec, applied deliberately:** the spec listed the per-micro-op pin classification (`BusPins`) as a 7a deliverable. It is **moved to 7b**. Nothing consumes it until the 65816 conformance harness exists, so in 7a it would be untested code, and 7a's whole justification is that every line in it is covered by an existing gate. The spec has been amended to match.

## Global Constraints

- **Zero behaviour change.** Every existing suite green on **both** `net8.0` and `net10.0`. Baselines measured this session: **442 unit tests** (`Category!=Performance`) and **1,309 conformance tests**, both TFMs, zero failures.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on, so **every public member needs an XML doc comment** — a missing one is a build failure, not a warning.
- Target frameworks are `net8.0;net10.0`. `dotnet test` runs both; a change that passes one and not the other is a failure.
- Conventional Commits. Work on a branch off `main`. **Do not push `main` without `[skip ci]` in the commit subject** — a push cuts a public nuget.org release.
- `PublicSurfaceTests` compares the exact set of public **types**. This phase adds no public type, so that list must not change. If you find yourself editing it, you have added something this phase should not have.

## Established facts — verified this session, do not re-derive

Every number below was measured on this machine today, not estimated.

- **Baseline is green:** `442` passed, `0` failed, on both TFMs.
- **Naive widening breaks the build in exactly two files.** Changing the four fields to `ushort` and adding the new ones produces **184 compiler errors**, all in `src/SixtyFiveXX/Cpu.cs` and `src/SixtyFiveXX/Cpu.Exec.cs`, and all of one of two kinds: `CS1503` (passing a `ushort` register to a `byte` parameter such as `SetZN`, `Compare`, `Asl`) and `CS0266` (assigning a `ushort` register into `_data`).
- **The shim approach reduces that to zero.** With the four private byte properties in place and `_s.A`/`_s.X`/`_s.Y`/`_s.S` renamed to `A`/`X`/`Y`/`S`, the **entire solution builds with 0 errors** and the unit suite is **442/442 on both TFMs**.
- **No test file needs editing.** 292 sites across `tests/` and `bench/` reference `State.A`/`X`/`Y`/`S`, and **all of them still compile.** `Assert.Equal(0x42, cpu.State.A)` infers `T = int` whether the property is `byte` or `ushort`, so the assertions are unaffected. Do not pre-emptively "fix" test files.
- ***** The stack-pointer wrap is a real hazard, not a theoretical one. ***** Bypassing the shim in one micro-op (`MicroOp.Push`, using `_s.S--` directly) and pushing with `S = $00` yields `S = 65535` instead of `255`. The existing suite catches it — but with **exactly one failing test out of 442**, which is thin cover for the single most dangerous line in this phase. Hence the dedicated characterisation test in Task 1.
- **No test asserts on `CpuState.ToString()`.** The format change is therefore additive coverage, not a test update.
- **Throughput is ~110 MHz** simulated (`net10.0`, Release, 50 M cycles) against a 50 MHz floor. *** Do not compare against a single stored figure. *** This gate is contention-sensitive — that is why CI excludes it — and an early idle-machine run read 137.6 MHz while every later run under load read 106–117 MHz. Task 3 must rebuild the pre-change baseline and interleave the two run-for-run; comparing a fresh median against a stale reading manufactures a regression that is not there.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SixtyFiveXX/CpuState.cs` | Modify: widen the register file; add `DP`/`DBR`/`PBR`/`E`, the `M`/`X` flag aliases and their properties, and the new `ToString` |
| `src/SixtyFiveXX/Cpu.cs` | Modify: add the four private byte shims; mechanical rename of the register use sites |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Modify: mechanical rename only |
| `src/SixtyFiveXX/IBus.cs` | Modify: add `Internal(int)`; implement it explicitly on `FlatBus` and `RefBus` |
| `tests/SixtyFiveXX.Tests/CpuStateTests.cs` | Modify: cover the new flag aliases, the width properties, and both `ToString` shapes |
| `tests/SixtyFiveXX.Tests/StackWrapTests.cs` | Create: the characterisation test for 8-bit stack wrap |
| `tests/SixtyFiveXX.Tests/BusTests.cs` | Modify: cover `Internal`'s default and `RefBus` forwarding |

---

### Task 1: Widen the register file

**Files:**
- Create: `tests/SixtyFiveXX.Tests/StackWrapTests.cs`
- Modify: `src/SixtyFiveXX/CpuState.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `tests/SixtyFiveXX.Tests/CpuStateTests.cs`

**Interfaces:**
- Produces: `CpuState` with `ushort PC, A, X, Y, S, DP`, `byte P, DBR, PBR`, `bool E`; `Flag.M = 0x20`, `Flag.X = 0x10`; `CpuState.M` and `CpuState.XFlag` `bool` properties. Phase 7b builds its addressing engine directly on these names.

This task is **one commit's worth of change that cannot be split**, because widening the fields without the shims leaves the tree unbuildable. Do not stop halfway.

- [ ] **Step 1: Write the characterisation test.** This encodes behaviour that exists *today* and must survive the widening. It passes before the change — that is the point of it.

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The stack pointer is 8 bits on every core before the 65816, and it wraps at 8 bits.
/// Widening <c>CpuState.S</c> to a <c>ushort</c> puts that at risk: a bare <c>S--</c> on a
/// 16-bit field takes $00 to $FFFF rather than $FF, and the push then lands at the wrong
/// address. Measured, not hypothesised — bypassing the byte shim reproduces exactly that.
/// </summary>
public class StackWrapTests
{
    [Fact]
    public void Push_WrapsTheStackPointerAtEightBits()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0x48;                 // PHA

        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;
        cpu.State.S = 0x00;
        cpu.State.A = 0x42;

        cpu.Step();

        Assert.Equal(0x42, ram[0x0100]);    // written at $0100 + S, with S still $00
        Assert.Equal(0xFF, cpu.State.S);    // then wrapped to $FF, not $FFFF
    }

    [Fact]
    public void Pull_WrapsTheStackPointerAtEightBits()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0x68;                 // PLA
        ram[0x0100] = 0x37;

        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;
        cpu.State.S = 0xFF;

        cpu.Step();

        Assert.Equal(0x37, cpu.State.A);    // read from $0100 + (S + 1), wrapping to $00
        Assert.Equal(0x00, cpu.State.S);
    }
}
```

- [ ] **Step 2: Run it against unchanged code.**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "FullyQualifiedName~StackWrapTests"`
Expected: PASS, 2 tests, both TFMs. If it fails, stop — the baseline is not what this plan assumes.

- [ ] **Step 3: Commit the characterisation test on its own.**

```bash
git add tests/SixtyFiveXX.Tests/StackWrapTests.cs
git commit -m "test: pin the 8-bit stack pointer wrap before widening the state"
```

- [ ] **Step 4: Widen the fields in `src/SixtyFiveXX/CpuState.cs`.** Replace the field block. Keep the existing `C`/`Z`/`I`/`D`/`V`/`N` properties exactly as they are.

```csharp
/// <summary>The architectural register file of a 65xx core.</summary>
/// <remarks>
/// Sized for the 65816 on every variant. The 8-bit cores use the low byte of each widened
/// register and leave <see cref="DP"/>, <see cref="DBR"/>, <see cref="PBR"/> and
/// <see cref="E"/> alone; <c>TVariant.Variant</c> folds away the mode checks they never
/// take, exactly as it already does for the 6510's port.
/// </remarks>
public struct CpuState
{
    /// <summary>Program counter. 16-bit on every variant; the 65816 pairs it with <see cref="PBR"/>.</summary>
    public ushort PC;

    /// <summary>
    /// Accumulator. The full 16 bits — what Bruce Clark's reference calls the "C accumulator",
    /// reserving "A" for its low byte. Named <c>A</c> here because that is what the
    /// conformance vectors call the 16-bit value.
    /// </summary>
    public ushort A;

    /// <summary>X index register. The high byte is forced to $00 whenever the x flag is 1.</summary>
    public ushort X;

    /// <summary>Y index register. The high byte is forced to $00 whenever the x flag is 1.</summary>
    public ushort Y;

    /// <summary>
    /// Stack pointer. 8 bits on every core before the 65816, where the stack is at
    /// $0100 + S; 16 bits in 65816 native mode, where it is anywhere in bank 0.
    /// </summary>
    public ushort S;

    /// <summary>
    /// Direct register — the 65816's page-zero relocation base. Called <c>D</c> by WDC, but
    /// <see cref="D"/> is already the decimal-mode flag on this type, so <c>DP</c> it is.
    /// </summary>
    public ushort DP;

    /// <summary>Processor status register. See <see cref="Flag"/>.</summary>
    public byte P;

    /// <summary>Data bank register. The bank absolute and indexed data accesses use. 65816 only.</summary>
    public byte DBR;

    /// <summary>Program bank register. The bank instructions are fetched from. 65816 only.</summary>
    public byte PBR;

    /// <summary>
    /// Emulation mode. True selects 6502 emulation, which forces the m and x flags to 1, the
    /// high bytes of X and Y to $00, and the high byte of S to $01. Only <c>XCE</c> changes it.
    /// </summary>
    public bool E;
```

- [ ] **Step 5: Add the native-mode flag aliases to `Flag`, in the same file.** Place them immediately after `B` and `U` so the shared bits are obvious on sight.

```csharp
    /// <summary>
    /// Index register width select, 65816 native mode. 1 selects 8-bit index registers.
    /// <para>
    /// Deliberately the same bit as <see cref="B"/>. Bit 4 is the break flag in emulation
    /// mode and the index-width select in native mode — one bit, two meanings, which is what
    /// the silicon does. Confirmed by Clark §4 and by the WDC datasheet §2.8 ("the Break flag
    /// is written to stack memory as bit 4"). Eyes &amp; Lichty p. 72 places the break flag at
    /// bit 5 and is wrong; see the research document §3.1 before changing this.
    /// </para>
    /// </summary>
    public const byte X = 0x10;

    /// <summary>
    /// Accumulator and memory width select, 65816 native mode. 1 selects an 8-bit
    /// accumulator. The same bit as <see cref="U"/>; see <see cref="X"/>.
    /// </summary>
    public const byte M = 0x20;
```

- [ ] **Step 6: Add the two width properties, alongside the existing flag properties.**

```csharp
    /// <summary>
    /// Accumulator and memory width select. True means 8-bit. Meaningful only in native mode;
    /// emulation mode forces it to true.
    /// </summary>
    public bool M
    {
        readonly get => (P & Flag.M) != 0;
        set => P = value ? (byte)(P | Flag.M) : (byte)(P & ~Flag.M);
    }

    /// <summary>
    /// Index register width select. True means 8-bit. Named <c>XFlag</c> rather than <c>X</c>
    /// because <see cref="X"/> is the index register itself.
    /// </summary>
    public bool XFlag
    {
        readonly get => (P & Flag.X) != 0;
        set => P = value ? (byte)(P | Flag.X) : (byte)(P & ~Flag.X);
    }
```

- [ ] **Step 7: Replace `ToString`.** The 65816 tail appears only when something in it is non-default, so a 6502's line stays readable.

```csharp
    /// <inheritdoc />
    public override readonly string ToString()
    {
        var core = $"PC:{PC:X4} A:{A:X4} X:{X:X4} Y:{Y:X4} S:{S:X4} P:{P:X2}";

        return DBR == 0 && PBR == 0 && DP == 0 && !E
            ? core
            : $"{core} DBR:{DBR:X2} PBR:{PBR:X2} DP:{DP:X4} E:{(E ? 1 : 0)}";
    }
```

- [ ] **Step 8: Build the library and observe the expected breakage.**

Run: `dotnet build src/SixtyFiveXX`
Expected: **FAIL, ~184 errors**, all `CS1503`/`CS0266`, all in `Cpu.cs` and `Cpu.Exec.cs`. This is the documented halfway state. Continue.

- [ ] **Step 9: Rename the register use sites, mechanically.** The pattern matches only the four bare register names — `_s.PC`, `_s.P` and the flag properties are untouched, and `_s.XFlag` does not match because there is no word boundary after its `X`.

```bash
perl -pi -e 's/\b_s\.([AXYS])\b/$1/g' src/SixtyFiveXX/Cpu.cs src/SixtyFiveXX/Cpu.Exec.cs
```

***** Do this BEFORE adding the shims, not after. ***** The pattern matches the shim bodies too, so
running it on a file that already contains them rewrites `(byte)_s.A` into `(byte)A` and turns each shim
into an infinitely self-recursive property. That compiles clean and stack-overflows at run time, which no
build step catches. Found by the Task 1 implementer when the steps were ordered the other way round.

- [ ] **Step 10: Add the four byte shims to `src/SixtyFiveXX/Cpu.cs`,** immediately after the `private CpuState _s;` field.

```csharp
    /// <summary>
    /// The 8-bit view of the register file, used by every core before the 65816.
    /// </summary>
    /// <remarks>
    /// <see cref="CpuState"/> is sized for the 65816, so its registers are 16 bits wide on
    /// every variant. The cores that are 8-bit read and write only the low byte, and these
    /// shims say so once rather than scattering casts across two hundred use sites.
    /// <para>
    /// The setters assign the whole 16-bit field, which is correct here because these cores
    /// never put anything in the high byte. The getters are what matter: <c>S--</c> through
    /// this property wraps at 8 bits, as a 6502 stack pointer must, whereas <c>_s.S--</c> on
    /// the raw field takes $00 to $FFFF and pushes to the wrong address.
    /// </para>
    /// </remarks>
    private byte A { get => (byte)_s.A; set => _s.A = value; }

    /// <inheritdoc cref="A"/>
    private byte X { get => (byte)_s.X; set => _s.X = value; }

    /// <inheritdoc cref="A"/>
    private byte Y { get => (byte)_s.Y; set => _s.Y = value; }

    /// <inheritdoc cref="A"/>
    private byte S { get => (byte)_s.S; set => _s.S = value; }
```

**No `readonly get` here.** `readonly` members are a struct-only feature; `Cpu<TBus, TVariant>` is a
class, and `readonly get` on it is a compile error. The accessors on `CpuState` in Task 1 *do* carry
`readonly`, because that type is a struct — the two are not inconsistent.

- [ ] **Step 11: Build the whole solution.**

Run: `dotnet build SixtyFiveXX.sln`
Expected: **0 errors, 0 warnings.** If any `CS1591` appears, a new public member is missing its XML doc.

- [ ] **Step 12: Extend `tests/SixtyFiveXX.Tests/CpuStateTests.cs`.** Append these; leave the existing three tests alone.

```csharp
    [Fact]
    public void NativeModeFlagAliases_ShareBitsWithBreakAndUnused()
    {
        // One bit, two meanings — bit 4 is b in emulation mode and x in native mode. This
        // asserts the sharing deliberately, so that "fixing" it to distinct bits fails here
        // rather than thousands of vectors later. See research doc §3.1.
        Assert.Equal(Flag.B, Flag.X);
        Assert.Equal(Flag.U, Flag.M);
        Assert.Equal(0x10, Flag.X);
        Assert.Equal(0x20, Flag.M);
    }

    [Fact]
    public void WidthProperties_ReadAndWriteTheirBits()
    {
        var state = new CpuState { P = 0x00 };

        state.M = true;
        Assert.Equal(0x20, state.P);
        Assert.True(state.M);
        Assert.False(state.XFlag);

        state.XFlag = true;
        Assert.Equal(0x30, state.P);

        state.M = false;
        Assert.Equal(0x10, state.P);
        Assert.True(state.XFlag);
    }

    [Fact]
    public void NewRegisters_DefaultToZero()
    {
        var state = new CpuState();

        Assert.Equal(0, state.DP);
        Assert.Equal(0, state.DBR);
        Assert.Equal(0, state.PBR);
        Assert.False(state.E);
    }

    [Fact]
    public void Registers_HoldSixteenBitValues()
    {
        var state = new CpuState { A = 0x1234, X = 0x5678, Y = 0x9ABC, S = 0x01FF, DP = 0xDEF0 };

        Assert.Equal(0x1234, state.A);
        Assert.Equal(0x5678, state.X);
        Assert.Equal(0x9ABC, state.Y);
        Assert.Equal(0x01FF, state.S);
        Assert.Equal(0xDEF0, state.DP);
    }

    [Fact]
    public void ToString_OmitsTheSixteenBitTailWhenNothingInItIsSet()
    {
        var state = new CpuState { PC = 0xC000, A = 0x42, X = 0x01, Y = 0x02, S = 0xFD, P = 0x24 };

        Assert.Equal("PC:C000 A:0042 X:0001 Y:0002 S:00FD P:24", state.ToString());
    }

    [Fact]
    public void ToString_ShowsTheSixteenBitTailWhenAnyOfItIsSet()
    {
        var state = new CpuState
        {
            PC = 0xC000, A = 0x1234, X = 0x01, Y = 0x02, S = 0x01FD, P = 0x24,
            DBR = 0x7E, PBR = 0x01, DP = 0x2000, E = true,
        };

        Assert.Equal(
            "PC:C000 A:1234 X:0001 Y:0002 S:01FD P:24 DBR:7E PBR:01 DP:2000 E:1",
            state.ToString());
    }
```

- [ ] **Step 13: Run the unit suite.**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"`
Expected: **PASS, 450 tests** on each TFM — the 442 baseline, plus 2 stack-wrap and 6 `CpuState` tests.

- [ ] **Step 14: Run the conformance suite.** This is the real gate. A cold cache downloads ~3.8 GB; set `SIXTYFIVEXX_HARTE_DIR` to an existing checkout to avoid it.

Run: `dotnet test tests/SixtyFiveXX.Conformance`
Expected: **PASS, 1309 tests on each TFM, 0 failed** — the baseline measured on this branch before any code changed. Any failure here is a behaviour change and must be fixed, not accepted; this phase's entire contract is that there isn't one. Runtime is ~3 min on `net10.0` and ~4.5 min on `net8.0`.

- [ ] **Step 15: Confirm the public type set did not move.**

Run: `dotnet test tests/SixtyFiveXX.Conformance --filter "FullyQualifiedName~PublicSurfaceTests"`
Expected: PASS with `ExpectedPublicTypes` unedited.

- [ ] **Step 16: Commit.**

```bash
git add src/SixtyFiveXX/CpuState.cs src/SixtyFiveXX/Cpu.cs src/SixtyFiveXX/Cpu.Exec.cs tests/SixtyFiveXX.Tests/CpuStateTests.cs
git commit -m "feat!: widen the register file for the 65816

A, X, Y and S become 16-bit and DP, DBR, PBR and E are added, on every
variant. The 8-bit cores reach them through private byte shims, so their
arithmetic — the stack pointer's 8-bit wrap above all — is unchanged.

BREAKING CHANGE: CpuState.A, X, Y and S are now ushort."
```

---

### Task 2: Internal cycles on the bus

**Files:**
- Modify: `src/SixtyFiveXX/IBus.cs`, `tests/SixtyFiveXX.Tests/BusTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `void IBus.Internal(int address)`, defaulted to a no-op, implemented explicitly on `FlatBus` and `RefBus`. Phase 7b's micro-ops call it on every cycle that drives an address without accessing memory.

- [ ] **Step 1: Add the interface member** to `src/SixtyFiveXX/IBus.cs`, after `Write`.

```csharp
    /// <summary>
    /// An internal-operation cycle: the core drives an address but performs no bus access.
    /// </summary>
    /// <remarks>
    /// Only the 65816 has these. On every earlier core each cycle is a real access — the
    /// dummy reads are reads — so nothing else calls this, and the call is guarded by a
    /// compile-time variant test so the JIT does not even emit it for them.
    /// <para>
    /// Defaulted, so no existing bus breaks. A bus that models read side effects should
    /// implement it as a no-op — which is what the default does — and a bus that models the
    /// physical address bus can observe the address here.
    /// </para>
    /// </remarks>
    /// <param name="address">The address driven during this cycle.</param>
    void Internal(int address) { }
```

- [ ] **Step 2: Implement it explicitly on both shipped buses.** Empty on `FlatBus`, forwarding on `RefBus`. The bodies are trivial; the reason they exist is not. **An explicit implementation on a `struct` is a direct call. Inheriting the default is a constrained call that boxes**, which on a per-cycle path would allocate once per internal cycle. `FlatBus` is the bus the README pairs with every example, so it must not be the one that allocates.

In `FlatBus`:

```csharp
    /// <inheritdoc />
    /// <remarks>Flat memory has no side effects to suppress, so this does nothing. It is
    /// declared rather than inherited so the call does not box — see <see cref="IBus.Internal"/>.</remarks>
    public void Internal(int address) { }
```

In `RefBus`:

```csharp
    /// <inheritdoc />
    public void Internal(int address) => _inner.Internal(address);
```

- [ ] **Step 3: Write the tests** in `tests/SixtyFiveXX.Tests/BusTests.cs`.

**`BusTests` already has a private `RecordingBus(byte[] ram)`.** Extend it — do not add a second one, which
is a `CS0102` duplicate-definition error. Replace the existing nested class with this, and add the
`DefaultOnlyBus` struct beside it:

```csharp
    private sealed class RecordingBus(byte[] ram) : IBus
    {
        public List<int> Internals { get; } = [];
        public byte Read(int address) => ram[address & 0xFFFF];
        public void Write(int address, byte value) => ram[address & 0xFFFF] = value;
        public void Internal(int address) => Internals.Add(address);
    }

    /// <summary>A bus written before the 65816 existed: it does not implement Internal at all.</summary>
    private readonly struct DefaultOnlyBus : IBus
    {
        public byte Read(int address) => 0;
        public void Write(int address, byte value) { }
    }
```

Then add the three tests:

```csharp
    [Fact]
    public void RefBus_ForwardsInternalCyclesToTheInnerBus()
    {
        var inner = new RecordingBus(new byte[0x10000]);
        var bus = new RefBus(inner);

        bus.Internal(0x7E1234);

        Assert.Single(inner.Internals);
        Assert.Equal(0x7E1234, inner.Internals[0]);
    }

    [Fact]
    public void FlatBus_AcceptsInternalCyclesWithoutTouchingMemory()
    {
        var ram = new byte[0x10000];
        ram[0x1234] = 0xAB;
        var bus = new FlatBus(ram);

        bus.Internal(0x1234);

        Assert.Equal(0xAB, ram[0x1234]);
    }

    [Fact]
    public void Internal_IsOptionalForBusesThatDoNotCareAboutIt()
    {
        // The default implementation exists so that adding this member breaks nobody. A bus
        // written before the 65816 must still satisfy IBus, and the inherited default must
        // do nothing rather than throw.
        IBus bus = new DefaultOnlyBus();

        Assert.Null(Record.Exception(() => bus.Internal(0x1234)));
    }
```

- [ ] **Step 4: Run the bus tests.**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "FullyQualifiedName~BusTests"`
Expected: **PASS, 6 tests** on each TFM — the 3 that already existed plus these 3. Verified this session
against the real files.

- [ ] **Step 5: Run the whole unit suite.**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"`
Expected: **PASS, 453 tests** on each TFM — 450 from Task 1 plus 3.

- [ ] **Step 6: Confirm the public type set still did not move.** `Internal` is a member, not a type, so `PublicSurfaceTests` is scoped away from it — but `RecordingBus` and `DefaultOnlyBus` live in the test assembly and must not leak into `src`.

Run: `dotnet test tests/SixtyFiveXX.Conformance --filter "FullyQualifiedName~PublicSurfaceTests"`
Expected: PASS with `ExpectedPublicTypes` unedited.

- [ ] **Step 7: Commit.**

```bash
git add src/SixtyFiveXX/IBus.cs tests/SixtyFiveXX.Tests/BusTests.cs
git commit -m "feat: let a bus see the 65816's internal cycles

An internal cycle drives an address and performs no access, which no
earlier core has. Defaulted so no existing bus breaks; declared explicitly
on FlatBus and RefBus so the call does not box."
```

---

### Task 3: The measurement the architecture spec deferred to this phase

**Files:** none modified. This task produces a number and a decision.

§5.4 of `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md` committed to widening `CpuState` for every variant and said plainly: *"Whether that trade holds is a measurement for phase 7, not an assumption — if the wider state measurably hurts the 8-bit cores, the fallback is a separate `Cpu816`."* This is where that debt is paid, and it must be paid before phase 7b builds anything on the widened state.

- [ ] **Step 1: Run the throughput gate on the widened core.**

Run: `dotnet test tests/SixtyFiveXX.Tests -c Release -f net10.0 --filter "Category=Performance" --logger "console;verbosity=detailed"`
Expected: PASS, with a line of the form `NNN.N MHz simulated (NNN ms).`

- [ ] **Step 2: Build a fresh baseline and interleave.** Do **not** compare against a stored number. Create a worktree at the commit before this phase's first code change (`git worktree add /tmp/sfx-baseline <commit>`), then run the gate alternately — baseline, widened, baseline, widened — at least three pairs, and compare medians. Interleaving cancels machine drift; a median-vs-single-reading comparison does not. Measured 2026-08-04 this way: 110.7 MHz baseline vs 110.2 MHz widened, no detectable difference.

  - Within noise of the freshly rebuilt, interleaved baseline from Step 2 — not the single 137.6 MHz
    idle-machine reading in the established facts above, which does not reproduce under load — or above →
    the trade holds. Record it and continue to 7b.
  - A regression that still clears the floor → record the delta, continue, and raise it explicitly rather than letting it pass silently.
  - Below the 50 MHz floor → **stop.** Do not start 7b. The fallback named in §5.4 — a separate `Cpu816` over the same building blocks — becomes live, and that is a design decision to take back to the spec, not a thing to work around here.

  Run it three times and take the median; a single run on a contended machine is not a measurement. This is why the gate is excluded from CI in the first place.

- [ ] **Step 3: Run the full benchmark for the record.**

Run: `dotnet run -c Release --project bench/SixtyFiveXX.Benchmarks`
Expected: BenchmarkDotNet completes and writes to `BenchmarkDotNet.Artifacts/`.

- [ ] **Step 4: Record the result in the spec.** Append a short "Measured" note to the "Gate" subsection of §"Phase 7a" in `docs/superpowers/specs/2026-08-03-65816-core-design.md`, giving the before figure, the after figure, and the verdict. One paragraph. The point is that a later reader can see the trade was measured rather than assumed.

- [ ] **Step 5: Commit.**

```bash
git add docs/superpowers/specs/2026-08-03-65816-core-design.md
git commit -m "docs: record what the widened state costs the 8-bit cores"
```

---

## Done when

- `dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"` → 453 passed, both TFMs.
- `dotnet test tests/SixtyFiveXX.Conformance` → 1309 passed, 0 failed, both TFMs.
- `PublicSurfaceTests.ExpectedPublicTypes` is byte-identical to `main`.
- The throughput figure is recorded in the spec, and it clears the 50 MHz floor.
- No file under `src/SixtyFiveXX` mentions the 65816 except in a doc comment.
