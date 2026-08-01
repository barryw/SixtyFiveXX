# SixtyFiveXX Phase 2a — Undocumented NMOS Opcodes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all 105 undocumented NMOS 6502 opcodes so the core passes Tom Harte's complete 256-opcode vector set and Klaus Dormann's functional test.

**Architecture:** The existing engine already carries every addressing mode and the full documented instruction set, certified against 1,510,000 per-cycle vectors. Undocumented opcodes are added the same way documented ones were: descriptor rows in `Opcodes6502.Table`, which `MicroOpTable` expands into cycle sequences, plus operation cases in `Exec()`. Most reuse existing micro-op sequences unchanged. Three groups need engine work: the unstable stores (whose target address depends on its own high byte), the JAM opcodes (which never complete an instruction), and multi-byte NOPs (which need descriptor rows only).

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit 2.9.3 (plain asserts), System.Text.Json.

## Global Constraints

- Target framework `net10.0`. `Nullable` enabled. Warnings as errors.
- `src/SixtyFiveXX` has **zero** NuGet dependencies. Test projects may take them.
- Licence MIT. All original work. Only permissively-licensed implementations may be consulted, and only for behaviour, never text.
- No FluentAssertions anywhere. Plain `Assert.*`.
- Every public member of `src/SixtyFiveXX` needs an XML doc comment — `GenerateDocumentationFile` is on with warnings as errors, so a missing one is a build failure.
- XML doc `cref` references to types that do not exist yet are CS1574 → build errors. Use plain `<c>...</c>`.
- A `public` method cannot take `internal` parameter types (CS0051). Such a test method must be declared `internal`; xUnit still discovers and runs it.
- **Scratch fields are declared on first use.** `Cpu.cs` never pre-declares a field for a later task, and no `#pragma warning disable` may exist anywhere.
- **The core invariant:** one `Tick()` = one clock cycle = at most one bus access.
- **The 1,510,000 documented-opcode vectors must stay green after every task.** They are the regression net for all of this.
- Harte vector data is never committed; it is cached under the gitignored `tests/SixtyFiveXX.Conformance/.harte-cache/`.

## Established facts — do not re-derive

These were determined empirically from the vectors before this plan was written.

- **The ANE/LXA magic constant is `0xEE`.** Verified against four independent vectors across `$8B` and `$AB`. For `$8B` with `A=$E4, X=$E2, imm=$23`: `(0xE4 | 0xEE) & 0xE2 & 0x23 = 0x22`, which is the vector's expected `A`.
- **JAM opcodes produce exactly this bus pattern:** cycle 1 fetches the opcode (PC increments once), cycle 2 is a dummy read at the new PC, then `$FFFF`, `$FFFE`, `$FFFE`, and `$FFFF` repeating forever. Final PC is the initial PC **+1**. Harte's vectors record 11 cycles and stop; the CPU never reaches an instruction boundary.
- **Harte ships vector files for all 256 opcodes**, including all 12 JAM opcodes. All confirmed HTTP 200.
- **Klaus Dormann's `6502_functional_test.bin` is a prebuilt 65,536-byte image**, start PC `$0400`, success trap `jmp *` at **`$3469`** (verified from the published `.lst`). Any other self-loop address is a failure whose address identifies the failing sub-test. `6502_decimal_test.bin` and `6502_interrupt_test.bin` are **not** prebuilt — do not attempt to fetch them.

## The 105 opcodes

| Group | Count | Opcodes |
| --- | --- | --- |
| Multi-byte NOPs | 27 | `1A 3A 5A 7A DA FA` (implied), `80 82 89 C2 E2` (imm), `04 44 64` (zp), `14 34 54 74 D4 F4` (zp,X), `0C` (abs), `1C 3C 5C 7C DC FC` (abs,X) |
| SLO | 7 | `03 07 0F 13 17 1B 1F` |
| RLA | 7 | `23 27 2F 33 37 3B 3F` |
| SRE | 7 | `43 47 4F 53 57 5B 5F` |
| RRA | 7 | `63 67 6F 73 77 7B 7F` |
| DCP | 7 | `C3 C7 CF D3 D7 DB DF` |
| ISC | 7 | `E3 E7 EF F3 F7 FB FF` |
| SAX | 4 | `83 87 8F 97` |
| LAX | 6 | `A3 A7 AF B3 B7 BF` |
| Immediate oddballs | 6 | `0B 2B` (ANC), `4B` (ALR), `6B` (ARR), `CB` (SBX), `EB` (SBC) |
| Unstable | 8 | `8B` (ANE), `AB` (LXA), `93 9F` (SHA), `9B` (TAS), `9C` (SHY), `9E` (SHX), `BB` (LAS) |
| JAM | 12 | `02 12 22 32 42 52 62 72 92 B2 D2 F2` |

Total: 27+7+7+7+7+7+7+4+6+6+8+12 = **105**.

## File Structure

| File | Change |
| --- | --- |
| `src/SixtyFiveXX/Op.cs` | Add operation members for the new instruction classes |
| `src/SixtyFiveXX/Opcodes6502.cs` | Add 105 descriptor rows |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Add operation cases and the magic constant |
| `src/SixtyFiveXX/MicroOp.cs` | Add `JamHold` and the unstable-store micro-ops |
| `src/SixtyFiveXX/MicroOpTable.cs` | Emit sequences for JAM and unstable stores |
| `src/SixtyFiveXX/Cpu.cs` | `Execute` cases for the new micro-ops; `IsJammed` |
| `tests/SixtyFiveXX.Tests/UndocumentedNopTests.cs` | Group 1 |
| `tests/SixtyFiveXX.Tests/CombinationOpTests.cs` | Groups 2-3 |
| `tests/SixtyFiveXX.Tests/LaxSaxTests.cs` | Group 4 |
| `tests/SixtyFiveXX.Tests/ImmediateOddballTests.cs` | Group 5 |
| `tests/SixtyFiveXX.Tests/UnstableOpTests.cs` | Group 6 |
| `tests/SixtyFiveXX.Tests/JamTests.cs` | Group 7 |
| `tests/SixtyFiveXX.Conformance/Harte6502Tests.cs` | Widen to all 256 opcodes; JAM-aware driver |
| `tests/SixtyFiveXX.Conformance/KlausFunctionalTests.cs` | Klaus runner |
| `tests/SixtyFiveXX.Conformance/KlausCache.cs` | Download and cache the test binary |

---

### Task 1: Multi-byte NOPs

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`
- Test: `tests/SixtyFiveXX.Tests/UndocumentedNopTests.cs`

**Interfaces:**
- Consumes: `OpcodeInfo(string Mnemonic, AddrMode Mode, Op Operation, Access Access)`, `Opcodes6502.Table`, existing `Op.Nop`.
- Produces: `Op.NopRead` — a NOP that still performs its addressing mode's memory read. 27 new table rows.

These are the easiest 27 opcodes and they exercise the descriptor table end to end. The implied forms behave exactly like `$EA`. The rest *read* their operand and discard it — which means they still cost the cycles their addressing mode implies, and `1C 3C 5C 7C DC FC` still take the page-cross penalty. Reusing `Access.Read` gets all of that for free; only the operation differs, and it does nothing.

`Op.Nop` cannot be reused for the reading forms: `ImmExec`/`ReadExec` call `Exec()` after the read, and a distinct member documents that the read is deliberate rather than accidental.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/UndocumentedNopTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class UndocumentedNopTests
{
    [Theory]
    [InlineData(0x1A)] [InlineData(0x3A)] [InlineData(0x5A)]
    [InlineData(0x7A)] [InlineData(0xDA)] [InlineData(0xFA)]
    public void ImpliedNops_TakeTwoCyclesAndAdvancePcByOne(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode);

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x0201, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x80)] [InlineData(0x82)] [InlineData(0x89)]
    [InlineData(0xC2)] [InlineData(0xE2)]
    public void ImmediateNops_TakeTwoCyclesAndConsumeTheOperand(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x55);

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x04)] [InlineData(0x44)] [InlineData(0x64)]
    public void ZeroPageNops_TakeThreeCyclesAndReadTheAddress(byte opcode)
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, opcode, 0x42);
        ram[0x0042] = 0x7F;

        var cycles = cpu.Step();

        Assert.Equal(3, cycles);
        Assert.Equal(0x0202, cpu.State.PC);
        Assert.Contains(log, a => a.Address == 0x0042 && !a.IsWrite);
    }

    [Theory]
    [InlineData(0x14)] [InlineData(0x34)] [InlineData(0x54)]
    [InlineData(0x74)] [InlineData(0xD4)] [InlineData(0xF4)]
    public void ZeroPageXNops_TakeFourCycles(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x42);
        cpu.State.X = 0x10;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void AbsoluteNop_TakesFourCycles()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x0C, 0x00, 0x30);

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0203, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x1C)] [InlineData(0x3C)] [InlineData(0x5C)]
    [InlineData(0x7C)] [InlineData(0xDC)] [InlineData(0xFC)]
    public void AbsoluteXNops_TakeFourCyclesWithoutAPageCross(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x00, 0x30);
        cpu.State.X = 0x10;

        Assert.Equal(4, cpu.Step());
    }

    [Theory]
    [InlineData(0x1C)] [InlineData(0x3C)] [InlineData(0x5C)]
    [InlineData(0x7C)] [InlineData(0xDC)] [InlineData(0xFC)]
    public void AbsoluteXNops_TakeFiveCyclesAcrossAPage(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0xF0, 0x30);
        cpu.State.X = 0x20;

        Assert.Equal(5, cpu.Step());
    }

    [Fact]
    public void UndocumentedNops_DoNotDisturbRegistersOrFlags()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x04, 0x42);
        cpu.State.A = 0x11;
        cpu.State.X = 0x22;
        cpu.State.Y = 0x33;
        cpu.State.P = Flag.U | Flag.C | Flag.N;

        cpu.Step();

        Assert.Equal(0x11, cpu.State.A);
        Assert.Equal(0x22, cpu.State.X);
        Assert.Equal(0x33, cpu.State.Y);
        Assert.Equal(Flag.U | Flag.C | Flag.N, cpu.State.P);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter UndocumentedNopTests -v q`
Expected: FAIL — `UndefinedOpcodeException: Undefined opcode $1A at $0200.`

- [ ] **Step 3: Add `Op.NopRead` to `src/SixtyFiveXX/Op.cs`**

Insert immediately after the existing `Nop` member in the control-flow group:

```csharp
    /// <summary>
    /// An undocumented NOP that still performs its addressing mode's read and discards
    /// the result. Distinct from <see cref="Nop"/> so the discarded read is deliberate.
    /// </summary>
    NopRead,
```

- [ ] **Step 4: Add the 27 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

Insert inside `BuildTable`, immediately before the `return t;`:

```csharp
        // ---- Undocumented: multi-byte NOPs -------------------------------------
        // The implied forms match $EA exactly. The rest read their operand and throw
        // it away, so they cost their addressing mode's cycles — including the
        // page-cross penalty on the absolute,X forms.
        foreach (var op in new[] { 0x1A, 0x3A, 0x5A, 0x7A, 0xDA, 0xFA })
            Set(op, "NOP", AddrMode.Implied, Op.Nop, Access.None);

        foreach (var op in new[] { 0x80, 0x82, 0x89, 0xC2, 0xE2 })
            Set(op, "NOP", AddrMode.Immediate, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x04, 0x44, 0x64 })
            Set(op, "NOP", AddrMode.ZeroPage, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x14, 0x34, 0x54, 0x74, 0xD4, 0xF4 })
            Set(op, "NOP", AddrMode.ZeroPageX, Op.NopRead, Access.Read);

        Set(0x0C, "NOP", AddrMode.Absolute, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x1C, 0x3C, 0x5C, 0x7C, 0xDC, 0xFC })
            Set(op, "NOP", AddrMode.AbsoluteX, Op.NopRead, Access.Read);
```

- [ ] **Step 5: Add the `Exec` case in `src/SixtyFiveXX/Cpu.Exec.cs`**

Insert next to the existing `case Op.Nop:`:

```csharp
            case Op.NopRead: break;   // the read already happened; the value is discarded
```

- [ ] **Step 6: Update the legal-opcode count assertion**

`tests/SixtyFiveXX.Tests/Opcodes6502Tests.cs` asserts exactly 151 defined opcodes. That number now rises. Change `Table_DefinesExactlyOneHundredFiftyOneLegalOpcodes` to assert **178** (151 + 27) and rename it to `Table_DefinesTheExpectedNumberOfOpcodes`. Update its body's expected value and any message text.

Also update `tests/SixtyFiveXX.Conformance/Harte6502Tests.cs`'s `Coverage_IsReportedHonestly`, which asserts `151`, to assert **178** for now. Task 7 replaces this assertion with 256.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "UndocumentedNopTests|Opcodes6502Tests" -v q`
Expected: PASS.

- [ ] **Step 8: Verify no regression in the certified opcodes**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS — the previously certified opcodes must be unaffected. This runs from cache in roughly 25 s.

- [ ] **Step 9: Commit**

```bash
git add src/SixtyFiveXX/Op.cs src/SixtyFiveXX/Opcodes6502.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/UndocumentedNopTests.cs tests/SixtyFiveXX.Tests/Opcodes6502Tests.cs \
        tests/SixtyFiveXX.Conformance/Harte6502Tests.cs
git commit -m "feat: add the 27 undocumented multi-byte NOP opcodes"
```

---

### Task 2: SLO, RLA, SRE and RRA — the shift-and-combine read-modify-writes

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`
- Test: `tests/SixtyFiveXX.Tests/CombinationOpTests.cs`

**Interfaces:**
- Consumes: existing private helpers `byte Asl(byte)`, `byte Lsr(byte)`, `byte Rol(byte)`, `byte Ror(byte)`, `void Adc(byte)`, `void SetZN(byte)`; `Access.ReadModifyWrite`.
- Produces: `Op.Slo`, `Op.Rla`, `Op.Sre`, `Op.Rra`; 28 descriptor rows.

Each is a documented RMW shift followed by a documented ALU operation on the result, sharing one operand. They need **no** new micro-ops: `Access.ReadModifyWrite` already produces read → dummy-write-original → write-modified, and the existing shift and ALU helpers compose directly.

`RRA` is the interesting one: its `ADC` half honours decimal mode, so `RRA` in decimal mode inherits the whole NMOS flag leak already implemented in `Adc`.

The addressing set for all four is identical: `(zp,X)`, `zp`, `abs`, `(zp),Y`, `zp,X`, `abs,Y`, `abs,X` — and all indexed forms are unconditionally 7 or 8 cycles because RMW always pays the fixup.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/CombinationOpTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class CombinationOpTests
{
    [Fact]
    public void Slo_ShiftsMemoryLeftThenOrsIntoAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x07, 0x10);   // SLO $10
        ram[0x0010] = 0x81;
        cpu.State.A = 0x02;

        var cycles = cpu.Step();

        Assert.Equal(0x02, ram[0x0010]);   // $81 << 1 = $02, carry out
        Assert.Equal(0x02, cpu.State.A);   // $02 | $02
        Assert.True(cpu.State.C);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Rla_RotatesMemoryLeftThroughCarryThenAnds()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x27, 0x10);   // RLA $10
        ram[0x0010] = 0x80;
        cpu.State.A = 0xFF;
        cpu.State.C = true;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // ($80 << 1) | carry-in
        Assert.Equal(0x01, cpu.State.A);
        Assert.True(cpu.State.C);          // carry out from bit 7
    }

    [Fact]
    public void Sre_ShiftsMemoryRightThenEors()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x47, 0x10);   // SRE $10
        ram[0x0010] = 0x03;
        cpu.State.A = 0xFF;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // $03 >> 1
        Assert.Equal(0xFE, cpu.State.A);   // $FF ^ $01
        Assert.True(cpu.State.C);          // bit 0 was set
    }

    [Fact]
    public void Rra_RotatesMemoryRightThenAddsWithCarry()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x67, 0x10);   // RRA $10
        ram[0x0010] = 0x02;
        cpu.State.A = 0x10;
        cpu.State.C = false;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // $02 >> 1, no carry in
        Assert.Equal(0x11, cpu.State.A);   // $10 + $01 + 0
        Assert.False(cpu.State.C);
    }

    [Fact]
    public void Rra_HonoursDecimalModeInItsAddHalf()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x67, 0x10);   // RRA $10
        ram[0x0010] = 0x02;                                       // >> 1 = $01
        cpu.State.A = 0x09;
        cpu.State.C = false;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x10, cpu.State.A);   // BCD 9 + 1 = 10
    }

    [Theory]
    [InlineData(0x03, 8)]  // SLO (zp,X)
    [InlineData(0x07, 5)]  // SLO zp
    [InlineData(0x0F, 6)]  // SLO abs
    [InlineData(0x13, 8)]  // SLO (zp),Y  — always pays the fixup
    [InlineData(0x17, 6)]  // SLO zp,X
    [InlineData(0x1B, 7)]  // SLO abs,Y   — always pays the fixup
    [InlineData(0x1F, 7)]  // SLO abs,X   — always pays the fixup
    public void SloAddressingModes_TakeTheDocumentedCycles(byte opcode, int expected)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, opcode, 0x10, 0x30);
        ram[0x0010] = 0x00;
        ram[0x0011] = 0x30;

        Assert.Equal(expected, cpu.Step());
    }

    [Fact]
    public void Dcp_DecrementsMemoryThenComparesAgainstAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xC7, 0x10);   // DCP $10
        ram[0x0010] = 0x43;
        cpu.State.A = 0x42;

        cpu.Step();

        Assert.Equal(0x42, ram[0x0010]);
        Assert.True(cpu.State.Z);          // A == decremented memory
        Assert.True(cpu.State.C);
        Assert.Equal(0x42, cpu.State.A);   // A is never modified
    }

    [Fact]
    public void Isc_IncrementsMemoryThenSubtractsFromAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xE7, 0x10);   // ISC $10
        ram[0x0010] = 0x04;
        cpu.State.A = 0x10;
        cpu.State.C = true;                                       // no borrow

        cpu.Step();

        Assert.Equal(0x05, ram[0x0010]);
        Assert.Equal(0x0B, cpu.State.A);   // $10 - $05
        Assert.True(cpu.State.C);
    }

    [Fact]
    public void Isc_HonoursDecimalModeInItsSubtractHalf()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xE7, 0x10);   // ISC $10
        ram[0x0010] = 0x04;                                       // +1 = $05
        cpu.State.A = 0x10;
        cpu.State.C = true;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x05, cpu.State.A);   // BCD 10 - 5 = 5
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter CombinationOpTests -v q`
Expected: FAIL — `UndefinedOpcodeException: Undefined opcode $07 at $0200.`

- [ ] **Step 3: Add the operation members to `src/SixtyFiveXX/Op.cs`**

Insert after the shift group:

```csharp
    // Undocumented combination read-modify-writes: a shift or increment on memory,
    // followed by an ALU operation against the accumulator, sharing one operand.
    Slo, Rla, Sre, Rra, Dcp, Isc,
```

- [ ] **Step 4: Add the 42 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

Insert before the `return t;`:

```csharp
        // ---- Undocumented: combination read-modify-writes -----------------------
        // All six share one addressing set. Every indexed form pays the page-cross
        // fixup unconditionally, because read-modify-write always does.
        void SetCombo(string mnemonic, Op operation, int zpX, int zp, int abs,
                      int zpYIndirect, int zeroPageX, int absY, int absX)
        {
            Set(zpX,         mnemonic, AddrMode.IndexedIndirect, operation, Access.ReadModifyWrite);
            Set(zp,          mnemonic, AddrMode.ZeroPage,        operation, Access.ReadModifyWrite);
            Set(abs,         mnemonic, AddrMode.Absolute,        operation, Access.ReadModifyWrite);
            Set(zpYIndirect, mnemonic, AddrMode.IndirectIndexed, operation, Access.ReadModifyWrite);
            Set(zeroPageX,   mnemonic, AddrMode.ZeroPageX,       operation, Access.ReadModifyWrite);
            Set(absY,        mnemonic, AddrMode.AbsoluteY,       operation, Access.ReadModifyWrite);
            Set(absX,        mnemonic, AddrMode.AbsoluteX,       operation, Access.ReadModifyWrite);
        }

        SetCombo("SLO", Op.Slo, 0x03, 0x07, 0x0F, 0x13, 0x17, 0x1B, 0x1F);
        SetCombo("RLA", Op.Rla, 0x23, 0x27, 0x2F, 0x33, 0x37, 0x3B, 0x3F);
        SetCombo("SRE", Op.Sre, 0x43, 0x47, 0x4F, 0x53, 0x57, 0x5B, 0x5F);
        SetCombo("RRA", Op.Rra, 0x63, 0x67, 0x6F, 0x73, 0x77, 0x7B, 0x7F);
        SetCombo("DCP", Op.Dcp, 0xC3, 0xC7, 0xCF, 0xD3, 0xD7, 0xDB, 0xDF);
        SetCombo("ISC", Op.Isc, 0xE3, 0xE7, 0xEF, 0xF3, 0xF7, 0xFB, 0xFF);
```

- [ ] **Step 5: Add the `Exec` cases to `src/SixtyFiveXX/Cpu.Exec.cs`**

Insert before the `default:` arm. Each composes two existing helpers on the same operand, which is exactly what the silicon does:

```csharp
            // Undocumented combination read-modify-writes. Each performs a documented
            // memory operation and then a documented ALU operation on the result.
            // Rra and Isc inherit decimal-mode behaviour from Adc and Sbc.
            case Op.Slo: _data = Asl(_data); _s.A |= _data; SetZN(_s.A); break;
            case Op.Rla: _data = Rol(_data); _s.A &= _data; SetZN(_s.A); break;
            case Op.Sre: _data = Lsr(_data); _s.A ^= _data; SetZN(_s.A); break;
            case Op.Rra: _data = Ror(_data); Adc(_data); break;
            case Op.Dcp: _data = (byte)(_data - 1); Compare(_s.A); break;
            case Op.Isc: _data = (byte)(_data + 1); Sbc(_data); break;
```

Note `Compare` reads `_data` from the field, so `Op.Dcp` must decrement `_data` before calling it — which the line above does.

- [ ] **Step 6: Update the opcode-count assertions**

`tests/SixtyFiveXX.Tests/Opcodes6502Tests.cs` and `tests/SixtyFiveXX.Conformance/Harte6502Tests.cs` now expect **220** (178 + 42).

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "CombinationOpTests|Opcodes6502Tests" -v q`
Expected: PASS.

- [ ] **Step 8: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: add SLO, RLA, SRE, RRA, DCP and ISC combination opcodes"
```

---

### Task 3: LAX and SAX

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`
- Test: `tests/SixtyFiveXX.Tests/LaxSaxTests.cs`

**Interfaces:**
- Consumes: `SetZN(byte)`, `Access.Read`, `Access.Write`.
- Produces: `Op.Lax`, `Op.Sax`; 10 descriptor rows.

`LAX` loads both A and X from the same read. `SAX` stores `A & X` and — unusually for a store — touches no flags. Note `$97` is `SAX zp,Y` and `$B7` is `LAX zp,Y`: the zero-page-Y indexing that the documented set only uses for `LDX`/`STX`.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/LaxSaxTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class LaxSaxTests
{
    [Fact]
    public void Lax_LoadsBothAccumulatorAndXFromMemory()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA7, 0x10);   // LAX $10
        ram[0x0010] = 0x9C;

        var cycles = cpu.Step();

        Assert.Equal(0x9C, cpu.State.A);
        Assert.Equal(0x9C, cpu.State.X);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Lax_SetsZeroWhenLoadingZero()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA7, 0x10);
        ram[0x0010] = 0x00;
        cpu.State.A = 0xFF;

        cpu.Step();

        Assert.Equal(0x00, cpu.State.A);
        Assert.Equal(0x00, cpu.State.X);
        Assert.True(cpu.State.Z);
    }

    [Fact]
    public void LaxZeroPageY_IndexesByY()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB7, 0x80);   // LAX $80,Y
        cpu.State.Y = 0x04;
        ram[0x0084] = 0x2B;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x2B, cpu.State.A);
        Assert.Equal(0x2B, cpu.State.X);
    }

    [Fact]
    public void LaxAbsoluteY_PaysThePageCrossPenalty()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBF, 0xFF, 0x20);   // LAX $20FF,Y
        cpu.State.Y = 0x01;
        ram[0x2100] = 0x3C;

        Assert.Equal(5, cpu.Step());
        Assert.Equal(0x3C, cpu.State.A);
    }

    [Fact]
    public void Sax_StoresTheAndOfAccumulatorAndX()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x87, 0x10);   // SAX $10
        cpu.State.A = 0xF0;
        cpu.State.X = 0x3C;

        var cycles = cpu.Step();

        Assert.Equal(0x30, ram[0x0010]);   // $F0 & $3C
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Sax_DoesNotTouchAnyFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x87, 0x10);
        cpu.State.A = 0x00;
        cpu.State.X = 0x00;                 // result is zero
        cpu.State.P = Flag.U;

        cpu.Step();

        Assert.Equal(Flag.U, cpu.State.P);   // Z must NOT be set
    }

    [Fact]
    public void SaxZeroPageY_IndexesByY()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x97, 0x80);   // SAX $80,Y
        cpu.State.Y = 0x04;
        cpu.State.A = 0xFF;
        cpu.State.X = 0x0F;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0F, ram[0x0084]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter LaxSaxTests -v q`
Expected: FAIL — `UndefinedOpcodeException: Undefined opcode $A7 at $0200.`

- [ ] **Step 3: Add the operation members to `src/SixtyFiveXX/Op.cs`**

```csharp
    /// <summary>Undocumented. Loads both the accumulator and X from one read.</summary>
    Lax,

    /// <summary>Undocumented. Stores the bitwise AND of the accumulator and X. Sets no flags.</summary>
    Sax,
```

- [ ] **Step 4: Add the 10 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

```csharp
        // ---- Undocumented: LAX and SAX -----------------------------------------
        Set(0xA3, "LAX", AddrMode.IndexedIndirect, Op.Lax, Access.Read);
        Set(0xA7, "LAX", AddrMode.ZeroPage,        Op.Lax, Access.Read);
        Set(0xAF, "LAX", AddrMode.Absolute,        Op.Lax, Access.Read);
        Set(0xB3, "LAX", AddrMode.IndirectIndexed, Op.Lax, Access.Read);
        Set(0xB7, "LAX", AddrMode.ZeroPageY,       Op.Lax, Access.Read);
        Set(0xBF, "LAX", AddrMode.AbsoluteY,       Op.Lax, Access.Read);

        Set(0x83, "SAX", AddrMode.IndexedIndirect, Op.Sax, Access.Write);
        Set(0x87, "SAX", AddrMode.ZeroPage,        Op.Sax, Access.Write);
        Set(0x8F, "SAX", AddrMode.Absolute,        Op.Sax, Access.Write);
        Set(0x97, "SAX", AddrMode.ZeroPageY,       Op.Sax, Access.Write);
```

- [ ] **Step 5: Add the `Exec` cases to `src/SixtyFiveXX/Cpu.Exec.cs`**

```csharp
            // Undocumented. LAX loads both registers from one read; SAX stores the
            // AND of A and X and is the only store on the part that sets no flags.
            case Op.Lax: _s.A = _data; _s.X = _data; SetZN(_data); break;
            case Op.Sax: _data = (byte)(_s.A & _s.X); break;
```

- [ ] **Step 6: Update the opcode-count assertions to 230** (220 + 10), in both files named in Task 2 Step 6.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "LaxSaxTests|Opcodes6502Tests" -v q`
Expected: PASS.

- [ ] **Step 8: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: add LAX and SAX opcodes"
```

---

### Task 4: The immediate oddballs — ANC, ALR, ARR, SBX and SBC $EB

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`
- Test: `tests/SixtyFiveXX.Tests/ImmediateOddballTests.cs`

**Interfaces:**
- Consumes: `SetZN(byte)`, `Lsr(byte)`, existing `Op.Sbc`.
- Produces: `Op.Anc`, `Op.Alr`, `Op.Arr`, `Op.Sbx`; 6 descriptor rows.

Six immediate-mode instructions with unusual flag behaviour:

- **ANC** (`$0B`, `$2B`) — `A &= imm`, then `C` is copied from bit 7 of the result. Two opcodes, identical behaviour.
- **ALR** (`$4B`) — `A &= imm`, then `LSR A`. Carry comes from bit 0 *before* the shift.
- **ARR** (`$6B`) — `A &= imm`, then a rotate-right whose flags are unlike any documented instruction: `C` is bit 6 of the result and `V` is bit 6 XOR bit 5. Decimal mode changes it further; see Step 5.
- **SBX** (`$CB`) — `X = (A & X) - imm`, with `C` set when no borrow occurred. **Never affected by decimal mode.**
- **SBC** (`$EB`) — a plain duplicate of `$E9`.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/ImmediateOddballTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class ImmediateOddballTests
{
    [Theory]
    [InlineData(0x0B)]
    [InlineData(0x2B)]
    public void Anc_AndsThenCopiesBitSevenIntoCarry(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0xFF);
        cpu.State.A = 0x80;

        var cycles = cpu.Step();

        Assert.Equal(0x80, cpu.State.A);
        Assert.True(cpu.State.N);
        Assert.True(cpu.State.C);      // carry mirrors bit 7
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Anc_ClearsCarryWhenBitSevenIsClear()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x0B, 0x7F);
        cpu.State.A = 0xFF;
        cpu.State.C = true;

        cpu.Step();

        Assert.Equal(0x7F, cpu.State.A);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.C);
    }

    [Fact]
    public void Alr_AndsThenShiftsRight()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x4B, 0xFF);   // ALR #$FF
        cpu.State.A = 0x03;

        var cycles = cpu.Step();

        Assert.Equal(0x01, cpu.State.A);   // ($03 & $FF) >> 1
        Assert.True(cpu.State.C);          // bit 0 before the shift
        Assert.False(cpu.State.N);         // LSR always clears N
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Arr_TakesCarryFromBitSixAndOverflowFromBitsSixAndFive()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6B, 0xFF);   // ARR #$FF
        cpu.State.A = 0xFF;
        cpu.State.C = false;

        var cycles = cpu.Step();

        Assert.Equal(0x7F, cpu.State.A);   // ($FF & $FF) >> 1, carry-in 0 into bit 7
        Assert.True(cpu.State.C);          // bit 6 of the result is set
        Assert.False(cpu.State.V);         // bit 6 XOR bit 5 = 1 XOR 1 = 0
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Arr_SetsOverflowWhenBitsSixAndFiveDiffer()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6B, 0xFF);
        cpu.State.A = 0x40;                // result $20: bit 6 clear, bit 5 set
        cpu.State.C = false;

        cpu.Step();

        Assert.Equal(0x20, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.V);
    }

    [Fact]
    public void Sbx_SubtractsImmediateFromAAndXWritingToX()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x05);   // SBX #$05
        cpu.State.A = 0xFF;
        cpu.State.X = 0x0F;

        var cycles = cpu.Step();

        Assert.Equal(0x0A, cpu.State.X);   // ($FF & $0F) - $05
        Assert.Equal(0xFF, cpu.State.A);   // A is untouched
        Assert.True(cpu.State.C);          // no borrow
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Sbx_ClearsCarryOnBorrow()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x10);
        cpu.State.A = 0xFF;
        cpu.State.X = 0x05;

        cpu.Step();

        Assert.Equal(0xF5, cpu.State.X);   // $05 - $10 wraps
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    [Fact]
    public void Sbx_IgnoresDecimalMode()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x01);
        cpu.State.A = 0xFF;
        cpu.State.X = 0x10;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x0F, cpu.State.X);   // binary, not BCD
    }

    [Fact]
    public void SbcEb_BehavesIdenticallyToSbcE9()
    {
        var (a, _) = TestMachine.Flat(0x0200, 0xE9, 0x10);
        a.State.A = 0x50; a.State.C = true;
        a.Step();

        var (b, _) = TestMachine.Flat(0x0200, 0xEB, 0x10);
        b.State.A = 0x50; b.State.C = true;
        var cycles = b.Step();

        Assert.Equal(a.State.A, b.State.A);
        Assert.Equal(a.State.P, b.State.P);
        Assert.Equal(2, cycles);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter ImmediateOddballTests -v q`
Expected: FAIL — `UndefinedOpcodeException: Undefined opcode $0B at $0200.`

- [ ] **Step 3: Add the operation members to `src/SixtyFiveXX/Op.cs`**

```csharp
    // Undocumented immediate-mode instructions with flag behaviour unlike any
    // documented opcode.
    Anc, Alr, Arr, Sbx,
```

- [ ] **Step 4: Add the 6 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

```csharp
        // ---- Undocumented: immediate oddballs ----------------------------------
        Set(0x0B, "ANC", AddrMode.Immediate, Op.Anc, Access.Read);
        Set(0x2B, "ANC", AddrMode.Immediate, Op.Anc, Access.Read);
        Set(0x4B, "ALR", AddrMode.Immediate, Op.Alr, Access.Read);
        Set(0x6B, "ARR", AddrMode.Immediate, Op.Arr, Access.Read);
        Set(0xCB, "SBX", AddrMode.Immediate, Op.Sbx, Access.Read);
        Set(0xEB, "SBC", AddrMode.Immediate, Op.Sbc, Access.Read);   // duplicate of $E9
```

- [ ] **Step 5: Add the `Exec` cases to `src/SixtyFiveXX/Cpu.Exec.cs`**

```csharp
            // Undocumented immediate-mode instructions.
            case Op.Anc:
                _s.A &= _data;
                SetZN(_s.A);
                _s.C = _s.N;                    // carry mirrors bit 7 of the result
                break;

            case Op.Alr:
                _s.A &= _data;
                _s.A = Lsr(_s.A);               // Lsr sets C from bit 0 and Z/N from the result
                break;

            case Op.Arr:
                Arr(_data);
                break;

            case Op.Sbx:
            {
                // X = (A & X) - imm, always binary, never affected by decimal mode.
                var result = (_s.A & _s.X) - _data;
                _s.C = result >= 0;
                _s.X = (byte)result;
                SetZN(_s.X);
                break;
            }
```

And add the `Arr` helper next to the other ALU helpers:

```csharp
    /// <summary>
    /// Undocumented ARR: AND with the operand, then a rotate-right whose flags do not
    /// match any documented instruction. Carry comes from bit 6 of the result and
    /// overflow from bit 6 XOR bit 5.
    /// </summary>
    private void Arr(byte value)
    {
        var anded = (byte)(_s.A & value);

        if (!_s.D)
        {
            var result = (byte)((anded >> 1) | (_s.C ? 0x80 : 0x00));
            _s.A = result;
            SetZN(result);
            _s.C = (result & 0x40) != 0;
            _s.V = (((result >> 6) ^ (result >> 5)) & 0x01) != 0;
            return;
        }

        // Decimal mode. N comes from the carry that was shifted in, Z from the shifted
        // result, and V from a comparison of the pre-shift and post-shift bit 6. The
        // accumulator then gets the BCD nibble corrections applied independently.
        var shifted = (byte)((anded >> 1) | (_s.C ? 0x80 : 0x00));
        _s.N = _s.C;
        _s.Z = shifted == 0;
        _s.V = ((shifted ^ anded) & 0x40) != 0;

        var lo = anded & 0x0F;
        var hi = anded & 0xF0;
        var adjusted = shifted;

        if (lo + (lo & 0x01) > 0x05) adjusted = (byte)((adjusted & 0xF0) | ((adjusted + 0x06) & 0x0F));

        if (hi + (hi & 0x10) > 0x50)
        {
            adjusted = (byte)((adjusted + 0x60) & 0xFF);
            _s.C = true;
        }
        else
        {
            _s.C = false;
        }

        _s.A = adjusted;
    }
```

The decimal branch of `ARR` is the least-documented behaviour on the part. Do not hand-verify it — the Harte vectors for `$6B` are the authority, and Task 7 runs 10,000 of them. If they fail, read the first differing vector and correct the arithmetic here rather than adjusting anything else.

- [ ] **Step 6: Update the opcode-count assertions to 236** (230 + 6).

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "ImmediateOddballTests|Opcodes6502Tests" -v q`
Expected: PASS. The tests here cover binary-mode ARR only; decimal ARR is gated by Harte in Task 7.

- [ ] **Step 8: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: add ANC, ALR, ARR, SBX and the duplicate SBC opcode"
```

---

### Task 5: The unstable opcodes — ANE, LXA, SHA, SHX, SHY, TAS and LAS

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`
- Test: `tests/SixtyFiveXX.Tests/UnstableOpTests.cs`

**Interfaces:**
- Consumes: `_addr`, `_data`, `_pageCross`, `SetZN(byte)`.
- Produces: `Op.Ane`, `Op.Lxa`, `Op.Sha`, `Op.Shx`, `Op.Shy`, `Op.Tas`, `Op.Las`; `MicroOp.UnstableStoreFixup`; the `AneMagic` constant; 8 descriptor rows.

These are genuinely analogue on real silicon — their behaviour depends on chip temperature and the decay of internal buses. They are modelled here as the deterministic values Harte's vectors encode, which is the behaviour the overwhelming majority of real chips exhibit.

**ANE (`$8B`) and LXA (`$AB`)** mix in a "magic" constant produced by an unstable internal bus:
- `ANE`: `A = (A | magic) & X & imm`
- `LXA`: `A = X = (A | magic) & imm`

**The magic constant is `0xEE`.** This was determined empirically from the vectors, not guessed — see the Established Facts section. Declare it as a named constant with that provenance in its comment.

**SHA/SHX/SHY/TAS** store a value ANDed with *the high byte of their own target address plus one*. That is a genuine engine change: no existing micro-op gives `Exec()` a value that depends on the address it is about to write to. They also behave differently across a page boundary — when indexing crosses a page, the value written is also used as the address's high byte.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/UnstableOpTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class UnstableOpTests
{
    [Fact]
    public void Ane_MixesTheMagicConstantIntoTheAccumulator()
    {
        // (A | $EE) & X & imm — verified against Harte vector "8b 1".
        var (cpu, _) = TestMachine.Flat(0x0200, 0x8B, 0x23);
        cpu.State.A = 0xE4;
        cpu.State.X = 0xE2;

        var cycles = cpu.Step();

        Assert.Equal(0x22, cpu.State.A);
        Assert.Equal(0xE2, cpu.State.X);   // X is unchanged
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Lxa_LoadsBothRegistersThroughTheMagicConstant()
    {
        // (A | $EE) & imm — verified against Harte vector "ab 1".
        var (cpu, _) = TestMachine.Flat(0x0200, 0xAB, 0xE4);
        cpu.State.A = 0xAE;
        cpu.State.X = 0x8D;

        var cycles = cpu.Step();

        Assert.Equal(0xE4, cpu.State.A);
        Assert.Equal(0xE4, cpu.State.X);
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Las_AndsMemoryWithTheStackPointerIntoAllThreeRegisters()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBB, 0x00, 0x30);   // LAS $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.S = 0xF0;
        ram[0x3010] = 0x3C;

        var cycles = cpu.Step();

        var expected = (byte)(0x3C & 0xF0);
        Assert.Equal(expected, cpu.State.A);
        Assert.Equal(expected, cpu.State.X);
        Assert.Equal(expected, cpu.State.S);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void Sha_StoresAccumulatorAndXAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9F, 0x00, 0x30);   // SHA $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.A = 0xFF;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);   // $FF & $FF & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Shx_StoresXAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9E, 0x00, 0x30);   // SHX $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);   // $FF & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Shy_StoresYAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9C, 0x00, 0x30);   // SHY $3000,X
        cpu.State.X = 0x10;
        cpu.State.Y = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Tas_SetsStackPointerToAAndXThenStoresWithTheHighByte()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9B, 0x00, 0x30);   // TAS $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.A = 0xFF;
        cpu.State.X = 0xF0;

        var cycles = cpu.Step();

        Assert.Equal(0xF0, cpu.State.S);   // S = A & X
        Assert.Equal(0x30, ram[0x3010]);   // S & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void UnstableStores_DoNotAffectFlags()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x9E, 0x00, 0x30);
        cpu.State.Y = 0x10;
        cpu.State.X = 0x00;
        cpu.State.P = Flag.U;

        cpu.Step();

        Assert.Equal(Flag.U, cpu.State.P);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter UnstableOpTests -v q`
Expected: FAIL — `UndefinedOpcodeException: Undefined opcode $8B at $0200.`

- [ ] **Step 3: Add the operation members to `src/SixtyFiveXX/Op.cs`**

```csharp
    // Undocumented and genuinely unstable on real silicon. Modelled as the
    // deterministic values the SingleStepTests vectors encode.
    Ane, Lxa, Las, Sha, Shx, Shy, Tas,
```

- [ ] **Step 4: Add the micro-op to `src/SixtyFiveXX/MicroOp.cs`**

Insert next to `DummyReadFixup`:

```csharp
    /// <summary>
    /// Dummy read at addr, then the unstable-store address correction: on a page cross
    /// the stored value's high-byte AND is folded into the address itself. Used only by
    /// SHA, SHX, SHY and TAS.
    /// </summary>
    UnstableStoreFixup,
```

- [ ] **Step 5: Emit the sequence in `src/SixtyFiveXX/MicroOpTable.cs`**

Add this case to `Emit`, placed before the general `EmitAddressing`/`EmitAccess` path:

```csharp
        // The unstable stores form their address like a normal indexed write, but the
        // fixup cycle also folds the stored value into the address's high byte on a
        // page cross. Each addressing mode builds its own prefix; note that $93 is
        // (zp),Y and so fetches a pointer first, while the rest are absolute-indexed.
        if (info.Operation is Op.Sha or Op.Shx or Op.Shy or Op.Tas)
        {
            if (info.Mode == AddrMode.IndirectIndexed)
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.PtrReadLo, MicroOp.PtrReadHiY]);
            else if (info.Mode == AddrMode.AbsoluteX)
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiX]);
            else
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiY]);

            ops.AddRange([MicroOp.UnstableStoreFixup, MicroOp.ExecWrite]);
            return;
        }
```

**`ops` is the shared list holding every opcode's sequence built so far.** Only ever
append to it. Calling `ops.Clear()` — or anything else that removes elements — destroys
every previously emitted opcode and corrupts the `Entry` index for all of them, which
would fail as thousands of unrelated vector mismatches rather than as an obvious error.

- [ ] **Step 6: Add the micro-op case to `src/SixtyFiveXX/Cpu.Execute`**

Insert before the `default:` arm:

```csharp
            case MicroOp.UnstableStoreFixup:
                _bus.Read(_addr);
                // The value these instructions store is ANDed with the target's high
                // byte plus one. On a page cross the AND result also becomes the high
                // byte, so the write lands somewhere other than the nominal address.
                _storeHigh = (byte)(((_addr >> 8) & 0xFF) + 1);
                if (_pageCross)
                {
                    _addr = (_addr & 0x00FF) | (UnstableStoreValue() << 8);
                }
                break;
```

Add the two members it needs. `_storeHigh` is a field because `Exec()` reads it a cycle later:

```csharp
    /// <summary>High byte of an unstable store's target address, plus one.</summary>
    private byte _storeHigh;

    /// <summary>The value an unstable store will write, before the address fold-in.</summary>
    private byte UnstableStoreValue() => _op switch
    {
        Op.Sha => (byte)(_s.A & _s.X & _storeHigh),
        Op.Shx => (byte)(_s.X & _storeHigh),
        Op.Shy => (byte)(_s.Y & _storeHigh),
        Op.Tas => (byte)(_s.A & _s.X & _storeHigh),
        _ => throw new InvalidOperationException($"{_op} is not an unstable store."),
    };
```

- [ ] **Step 7: Add the `Exec` cases to `src/SixtyFiveXX/Cpu.Exec.cs`**

```csharp
            // Undocumented and unstable. The magic constant $EE was determined
            // empirically from the SingleStepTests vectors, not chosen: for $8B with
            // A=$E4, X=$E2, imm=$23, only ($E4 | $EE) & $E2 & $23 yields the expected $22.
            case Op.Ane: _s.A = (byte)((_s.A | AneMagic) & _s.X & _data); SetZN(_s.A); break;
            case Op.Lxa: _s.A = _s.X = (byte)((_s.A | AneMagic) & _data); SetZN(_s.A); break;

            case Op.Las:
                _s.A = _s.X = _s.S = (byte)(_data & _s.S);
                SetZN(_s.A);
                break;

            // The unstable stores set no flags. UnstableStoreFixup has already computed
            // the high byte these AND against.
            case Op.Sha: _data = (byte)(_s.A & _s.X & _storeHigh); break;
            case Op.Shx: _data = (byte)(_s.X & _storeHigh); break;
            case Op.Shy: _data = (byte)(_s.Y & _storeHigh); break;
            case Op.Tas:
                _s.S = (byte)(_s.A & _s.X);
                _data = (byte)(_s.S & _storeHigh);
                break;
```

And the constant, next to the other private members in `Cpu.Exec.cs`:

```csharp
    /// <summary>
    /// The "magic" constant ANE and LXA mix into the accumulator. On real silicon this
    /// is the decaying value of an internal bus and varies by chip and temperature;
    /// $EE is what the SingleStepTests vectors encode and what most parts produce.
    /// </summary>
    private const byte AneMagic = 0xEE;
```

- [ ] **Step 8: Add the 8 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

```csharp
        // ---- Undocumented: unstable -------------------------------------------
        Set(0x8B, "ANE", AddrMode.Immediate,       Op.Ane, Access.Read);
        Set(0xAB, "LXA", AddrMode.Immediate,       Op.Lxa, Access.Read);
        Set(0xBB, "LAS", AddrMode.AbsoluteY,       Op.Las, Access.Read);
        Set(0x93, "SHA", AddrMode.IndirectIndexed, Op.Sha, Access.Write);
        Set(0x9F, "SHA", AddrMode.AbsoluteY,       Op.Sha, Access.Write);
        Set(0x9B, "TAS", AddrMode.AbsoluteY,       Op.Tas, Access.Write);
        Set(0x9C, "SHY", AddrMode.AbsoluteX,       Op.Shy, Access.Write);
        Set(0x9E, "SHX", AddrMode.AbsoluteY,       Op.Shx, Access.Write);
```

- [ ] **Step 9: Update the opcode-count assertions to 244** (236 + 8).

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "UnstableOpTests|Opcodes6502Tests" -v q`
Expected: PASS.

- [ ] **Step 11: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: add the unstable ANE, LXA, LAS, SHA, SHX, SHY and TAS opcodes"
```

---

### Task 6: The JAM opcodes and the `IsJammed` state

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes6502.cs`, `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`
- Test: `tests/SixtyFiveXX.Tests/JamTests.cs`

**Interfaces:**
- Consumes: `_bus`, `_s.PC`, `_mpc`.
- Produces: `Op.Jam`; `MicroOp.JamHold`; `public bool IsJammed { get; }`; 12 descriptor rows.

A JAM opcode halts the processor until reset. It never reaches an instruction boundary, which makes it the first instruction that breaks an assumption the whole API rests on: `Step()` runs *to* the next boundary, so on a JAM it would spin forever.

The bus behaviour, taken from the vectors: cycle 1 fetches the opcode and increments PC once; cycle 2 is a dummy read at the new PC; then the address bus goes `$FFFF`, `$FFFE`, `$FFFE`, and `$FFFF` repeating for as long as the clock runs.

`Step()` and `RunUntil` must therefore return once jammed rather than spin. `Run(cycles)` keeps ticking, because a real clock keeps running and the bus keeps being driven — that is what the vectors record.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/JamTests.cs`:

```csharp
using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class JamTests
{
    [Theory]
    [InlineData(0x02)] [InlineData(0x12)] [InlineData(0x22)] [InlineData(0x32)]
    [InlineData(0x42)] [InlineData(0x52)] [InlineData(0x62)] [InlineData(0x72)]
    [InlineData(0x92)] [InlineData(0xB2)] [InlineData(0xD2)] [InlineData(0xF2)]
    public void JamOpcodes_HaltTheProcessor(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode);

        cpu.Run(20);

        Assert.True(cpu.IsJammed);
        Assert.Equal(0x0201, cpu.State.PC);   // PC advanced past the opcode only
    }

    [Fact]
    public void Jam_ProducesTheDocumentedBusPattern()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0x02);

        cpu.Run(11);

        Assert.Equal(11, log.Count);
        Assert.All(log, a => Assert.False(a.IsWrite));
        Assert.Equal(0x0200, log[0].Address);   // opcode fetch
        Assert.Equal(0x0201, log[1].Address);   // dummy read at PC
        Assert.Equal(0xFFFF, log[2].Address);
        Assert.Equal(0xFFFE, log[3].Address);
        Assert.Equal(0xFFFE, log[4].Address);
        Assert.Equal(0xFFFF, log[5].Address);
        Assert.Equal(0xFFFF, log[10].Address);
    }

    [Fact]
    public void Step_ReturnsInsteadOfSpinningOnAJam()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x02);

        var cycles = cpu.Step();

        Assert.True(cpu.IsJammed);
        Assert.True(cycles > 0, "Step must consume cycles before returning.");
    }

    [Fact]
    public void RunUntil_StopsWhenTheProcessorJams()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x02);

        var cycles = cpu.RunUntil(c => c.State.A == 0xFF, maxCycles: 1000);

        Assert.True(cpu.IsJammed);
        Assert.True(cycles < 1000, $"Expected an early stop on jam, ran {cycles} cycles.");
    }

    [Fact]
    public void Reset_ClearsTheJammedState()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x02);
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0x80;

        cpu.Run(10);
        Assert.True(cpu.IsJammed);

        cpu.Reset();
        cpu.Step();

        Assert.False(cpu.IsJammed);
        Assert.Equal(0x8000, cpu.State.PC);
    }

    [Fact]
    public void UnjammedCpu_ReportsNotJammed()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        cpu.Step();

        Assert.False(cpu.IsJammed);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter JamTests -v q`
Expected: FAIL — compile error, `IsJammed` does not exist.

- [ ] **Step 3: Add `Op.Jam` to `src/SixtyFiveXX/Op.cs`**

```csharp
    /// <summary>Undocumented. Halts the processor until reset.</summary>
    Jam,
```

- [ ] **Step 4: Add `MicroOp.JamHold` to `src/SixtyFiveXX/MicroOp.cs`**

```csharp
    /// <summary>
    /// Drives the address bus while jammed and never advances. The sequence position is
    /// held, so this micro-op repeats for as long as the clock runs.
    /// </summary>
    JamHold,
```

- [ ] **Step 5: Emit the sequence in `src/SixtyFiveXX/MicroOpTable.cs`**

Add to `Emit`, before the addressing-mode dispatch:

```csharp
        if (info.Operation == Op.Jam)
        {
            // Cycle 2 is a dummy read at PC; every cycle after that is held by JamHold,
            // which never advances the sequence position.
            ops.AddRange([MicroOp.ImpliedDummy, MicroOp.JamHold]);
            return;
        }
```

- [ ] **Step 6: Add the micro-op case and `IsJammed` to `src/SixtyFiveXX/Cpu.cs`**

The `Execute` case — note it deliberately does **not** advance `_mpc`, which is what makes it repeat:

```csharp
            case MicroOp.JamHold:
                // The address bus cycles $FFFF, $FFFE, $FFFE, then $FFFF forever.
                _bus.Read(_jamPhase switch
                {
                    0 => 0xFFFF,
                    1 => 0xFFFE,
                    2 => 0xFFFE,
                    _ => 0xFFFF,
                });
                if (_jamPhase < 3) _jamPhase++;
                _mpc--;             // hold position: this micro-op repeats forever
                break;
```

The two fields and the property. `_jammed` is explicit state rather than something
inferred from the micro-op table, so nothing has to reason about table positions to
answer the question:

```csharp
    /// <summary>Position within the jammed address-bus pattern.</summary>
    private int _jamPhase;

    /// <summary>Set by <c>JamHold</c>; cleared only by <see cref="Reset"/>.</summary>
    private bool _jammed;

    /// <summary>
    /// True once a JAM opcode has halted the processor. Only <see cref="Reset"/> clears
    /// it. A jammed core keeps driving the address bus if ticked, exactly as the silicon
    /// does, but never executes another instruction.
    /// </summary>
    public bool IsJammed => _jammed;
```

Set `_jammed = true;` as the first statement of the `JamHold` case, and add
`_jammed = false;` and `_jamPhase = 0;` to `Reset()`.

- [ ] **Step 7: Make `Step` and `RunUntil` jam-aware in `src/SixtyFiveXX/Cpu.cs`**

`Step`'s loop must not spin forever:

```csharp
    public long Step()
    {
        var before = _cycles;
        do
        {
            Tick();
        }
        while (_mpc >= 0 && !_jammed);

        return _cycles - before;
    }
```

`RunUntil` must break out too — add `if (_jammed) break;` immediately after its `Step()` call, before the predicate is evaluated. Update both XML doc comments to state that they return early when the processor jams.

`Run(long)` is deliberately left alone: a real clock keeps running and the bus keeps being driven, which is what the vectors record.

- [ ] **Step 8: Add the 12 descriptor rows to `src/SixtyFiveXX/Opcodes6502.cs`**

```csharp
        // ---- Undocumented: JAM ------------------------------------------------
        // These halt the processor until reset.
        foreach (var op in new[] { 0x02, 0x12, 0x22, 0x32, 0x42, 0x52,
                                   0x62, 0x72, 0x92, 0xB2, 0xD2, 0xF2 })
            Set(op, "JAM", AddrMode.Implied, Op.Jam, Access.None);
```

- [ ] **Step 9: Update the opcode-count assertions to 256** (244 + 12). Every opcode is now defined, so `Table_MarksUndocumentedOpcodesAsUndefined` in `tests/SixtyFiveXX.Tests/Opcodes6502Tests.cs` is now false — delete that test and replace it with:

```csharp
    [Fact]
    public void Table_DefinesEveryOpcode()
    {
        for (var opcode = 0; opcode < 256; opcode++)
        {
            Assert.True(Opcodes6502.Table[opcode].Operation != Op.Undefined,
                $"Opcode ${opcode:X2} is still undefined.");
        }
    }
```

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "JamTests|Opcodes6502Tests" -v q`
Expected: PASS.

- [ ] **Step 11: Verify no regression**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v q`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: add the twelve JAM opcodes and the IsJammed state"
```

---

### Task 7: Widen the Harte gate to all 256 opcodes

**Files:**
- Modify: `tests/SixtyFiveXX.Conformance/Harte6502Tests.cs`

**Interfaces:**
- Consumes: `Opcodes6502.Table`, `Cpu<HarteBus>`, `IsJammed`, `HarteCache.Load`.
- Produces: a 256-case theory.

**This is the gate for the whole plan.** 2,560,000 vectors, each checking registers, named RAM bytes, and exact per-cycle bus activity. Any defect in Tasks 1-6 surfaces here.

The runner needs one change beyond widening the opcode list: a JAM opcode never reaches an instruction boundary, so `Step()` cannot drive it. For those twelve, tick exactly as many times as the vector records cycles.

- [ ] **Step 1: Widen `LegalOpcodes` and rename it**

Replace the `LegalOpcodes` property with:

```csharp
    /// <summary>Every opcode. All 256 are implemented as of Phase 2a.</summary>
    public static TheoryData<byte> AllOpcodes
    {
        get
        {
            var data = new TheoryData<byte>();
            for (var opcode = 0; opcode < 256; opcode++) data.Add((byte)opcode);
            return data;
        }
    }
```

Update the `[MemberData(nameof(LegalOpcodes))]` attribute to `nameof(AllOpcodes)`.

- [ ] **Step 2: Make the driver JAM-aware**

Replace the `cpu.Step();` line in the vector loop with:

```csharp
            // A JAM opcode never reaches an instruction boundary, so Step() cannot drive
            // it. Tick exactly as many cycles as the vector records instead.
            if (Opcodes6502.Table[opcode].Operation == Op.Jam)
            {
                for (var i = 0; i < test.Cycles.Length; i++) cpu.Tick();
            }
            else
            {
                cpu.Step();
            }
```

A jammed CPU stays jammed, so it cannot drive the next vector. Add this as the last
statement of the vector loop body, after all three assertions:

```csharp
            // A jammed core never executes again. Replace it so the next vector starts
            // from a working CPU. Only the twelve JAM opcodes ever take this path.
            if (cpu.IsJammed) cpu = new Cpu<HarteBus>(new HarteBus(ram, log));
```

`cpu` is currently declared with `var` outside the loop; change it to an explicit
`Cpu<HarteBus> cpu = new(new HarteBus(ram, log));` so it can be reassigned.

- [ ] **Step 3: Update the coverage assertion**

```csharp
    [Fact]
    public void Coverage_IsReportedHonestly()
    {
        var implemented = Opcodes6502.Table.Count(e => e.Operation != Op.Undefined);

        output.WriteLine($"Phase 2a runs all 256 opcodes ({256 * 10_000:N0} vectors).");
        output.WriteLine($"{implemented} of 256 opcodes are implemented.");

        Assert.Equal(256, implemented);
    }
```

- [ ] **Step 4: Run the full conformance suite**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release -v n`

This downloads the remaining 105 vector files (roughly 400 MB) and executes 2,560,000 vectors. It takes several minutes on a cold cache. **Background processes do not survive a subagent turn boundary — poll with foreground calls and do not end your turn until it exits.**

Expected: 257 passed (256 opcodes + the coverage report).

**When a vector fails, read the first differing cycle, not the register diff.** A wrong address explains a wrong result far more often than the reverse. Fix the core; never adjust a vector or weaken an assertion. Decimal-mode `ARR` (`$6B`) and the unstable stores (`$93 $9B $9C $9E $9F`) are the most likely to need correction — that is expected and is why they are gated here.

- [ ] **Step 5: Commit**

```bash
git add tests/SixtyFiveXX.Conformance/Harte6502Tests.cs
git commit -m "test: widen the Harte conformance gate to all 256 opcodes"
```

---

### Task 8: The Klaus Dormann functional test

**Files:**
- Create: `tests/SixtyFiveXX.Conformance/KlausCache.cs`, `tests/SixtyFiveXX.Conformance/KlausFunctionalTests.cs`

**Interfaces:**
- Consumes: `Cpu<FlatBus>`, `FlatBus`, `IsJammed`.
- Produces: `KlausCache.Load(string name) → byte[]`; one xUnit fact.

A second, independent gate. Where Harte checks each instruction in isolation, Klaus's test runs a real 6502 program of tens of millions of cycles that exercises instruction *interactions* — flag propagation across sequences, address-boundary behaviour, and decimal arithmetic in context.

The mechanism, verified from the published listing: the binary is a full 64 KB image, execution starts at `$0400`, and the program signals completion by branching to itself (`jmp *`). Reaching `$3469` means every sub-test passed. A self-loop at any other address is a failure, and that address identifies which sub-test failed.

- [ ] **Step 1: Write `tests/SixtyFiveXX.Conformance/KlausCache.cs`**

```csharp
namespace SixtyFiveXX.Conformance;

/// <summary>
/// Downloads and caches Klaus Dormann's prebuilt 6502 test binaries from
/// <c>github.com/Klaus2m5/6502_65C02_functional_tests</c>.
/// </summary>
/// <remarks>
/// The binaries are GPL-licensed test programs. They are executed, never linked or
/// derived from, so their licence does not reach this project's source. They are
/// fetched rather than committed for the same reason as the Harte vectors.
/// </remarks>
public static class KlausCache
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/Klaus2m5/6502_65C02_functional_tests/master/bin_files";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Where the binaries are cached.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SIXTYFIVEXX_KLAUS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".klaus-cache");

    /// <summary>Loads a 64 KB test image, downloading and caching it on first use.</summary>
    /// <param name="name">File name, for example <c>6502_functional_test.bin</c>.</param>
    public static byte[] Load(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Root, name));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Download($"{BaseUrl}/{name}", path);
        }

        var image = File.ReadAllBytes(path);
        if (image.Length != 0x10000)
            throw new InvalidOperationException($"{name} is {image.Length} bytes; expected 65536.");

        return image;
    }

    private static void Download(string url, string destination)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var temp = destination + ".partial";
            using (var file = File.Create(temp))
            {
                response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            }
            File.Move(temp, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not fetch {url}. Either allow network access, or clone " +
                $"https://github.com/Klaus2m5/6502_65C02_functional_tests and point " +
                $"SIXTYFIVEXX_KLAUS_DIR at its bin_files directory.", ex);
        }
    }
}
```

- [ ] **Step 2: Add `.klaus-cache/` to `.gitignore`**

```gitignore
tests/SixtyFiveXX.Conformance/.klaus-cache/
```

- [ ] **Step 3: Write `tests/SixtyFiveXX.Conformance/KlausFunctionalTests.cs`**

```csharp
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs Klaus Dormann's 6502 functional test to completion. Where the SingleStepTests
/// vectors check each instruction in isolation, this exercises interactions across a
/// real program of tens of millions of cycles.
/// </summary>
public class KlausFunctionalTests(ITestOutputHelper output)
{
    /// <summary>Entry point of the test program within the 64 KB image.</summary>
    private const ushort StartAddress = 0x0400;

    /// <summary>
    /// The address of the success trap. Verified from the published listing:
    /// <c>3469 : 4c6934  jmp *  ;test passed, no errors</c>.
    /// </summary>
    private const ushort SuccessAddress = 0x3469;

    /// <summary>Generous ceiling; a passing run completes in roughly 96 million cycles.</summary>
    private const long CycleCeiling = 500_000_000;

    [Fact]
    public void FunctionalTest_RunsToTheSuccessTrap()
    {
        var ram = KlausCache.Load("6502_functional_test.bin");
        var cpu = new Cpu<FlatBus>(new FlatBus(ram));
        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        // Both success and failure are signalled by a branch to self, so the test is
        // over the moment an instruction leaves PC where it started.
        ushort previous = 0xFFFF;
        while (cpu.Cycles < CycleCeiling)
        {
            previous = cpu.State.PC;
            cpu.Step();

            if (cpu.State.PC == previous) break;
            if (cpu.IsJammed) break;
        }

        output.WriteLine($"Trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles.");

        Assert.False(cpu.IsJammed,
            $"The processor jammed at ${cpu.State.PC:X4} — an opcode was decoded wrongly.");

        Assert.True(cpu.Cycles < CycleCeiling,
            $"Test did not terminate within {CycleCeiling:N0} cycles; last PC ${cpu.State.PC:X4}.");

        Assert.True(cpu.State.PC == SuccessAddress,
            $"Trapped at ${cpu.State.PC:X4}, expected the success trap at ${SuccessAddress:X4}. " +
            $"The trap address identifies the failing sub-test — look it up in " +
            $"6502_functional_test.lst.");
    }
}
```

- [ ] **Step 4: Run it**

Run: `dotnet test tests/SixtyFiveXX.Conformance -c Release --filter KlausFunctionalTests -v n`
Expected: PASS, with the output line reporting a trap at `$3469`.

If it traps elsewhere, that address is the diagnosis: open `6502_functional_test.lst` from the same repository, find the address, and the surrounding source names the sub-test that failed. Fix the core, not the test.

- [ ] **Step 5: Add the Klaus stage to `.woodpecker.yml`**

The conformance stage now covers both suites, so no new stage is needed — but confirm the existing `conformance` step still runs the whole project rather than a filter. Read `.woodpecker.yml` and verify its conformance step is `dotnet test tests/SixtyFiveXX.Conformance -c Release` with no `--filter`. If a filter is present, remove it.

- [ ] **Step 6: Run everything**

Run: `dotnet test -c Release --filter "Category!=Performance" -v q`
Expected: PASS — unit tests, 2,560,000 Harte vectors, and the Klaus functional test.

- [ ] **Step 7: Commit**

```bash
git add tests/SixtyFiveXX.Conformance .gitignore .woodpecker.yml
git commit -m "test: add the Klaus Dormann functional test as a second conformance gate"
```

---

## Phase 2a complete

The core now implements every one of the 256 NMOS 6502 opcodes, certified against
2,560,000 per-cycle vectors and Klaus Dormann's functional test.

**Phase 2b** — interrupts (IRQ and NMI with correct sampling, edge latching, and BRK
hijacking), RDY, and SO — gets its own plan. Its gates are custom cycle-level tests plus
Klaus's interrupt test, which is not distributed prebuilt and will need assembling from
source.

## Self-review notes

Checked against `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md`:

- **Spec §5.4, `Mos6502` row** — "All 105 undocumented opcodes, including the unstable
  `ANE`/`LXA`/`TAS`/`SHA`/`SHX`/`SHY` magic-constant behaviour" — Tasks 1-6 deliver all
  105; the count is verified by the table in this plan (27+42+10+6+8+12 = 105) and
  enforced by the 256-opcode assertion in Task 6 Step 9.
- **Spec §7, Harte row** — Task 7 widens the gate to all 256.
- **Spec §7, Klaus row** — Task 8. Note the spec also names Bruce Clark's decimal test;
  it is **not** distributed as a prebuilt binary and is deferred rather than silently
  dropped. Harte's `$69`/`$6D`/`$E9`/`$ED` and the other twelve ADC/SBC opcodes already
  cover decimal mode across roughly 80,000 vectors with the D flag set, per-cycle exact.
- **Spec §10, phase 2 row** — interrupts, RDY and SO are explicitly deferred to Phase 2b,
  as flagged at the top of this document.
- **Global constraints** — no new NuGet dependencies in `src/SixtyFiveXX`; no pragma; no
  pre-declared fields (each of `_storeHigh`, `_jamPhase` and `_jammed` is introduced by
  the task that first reads it).

Type consistency verified: `Op`, `AddrMode`, `Access`, `OpcodeInfo`, `MicroOp`,
`MicroOpTable`, `Cpu<TBus>`, `TestMachine.Flat`/`Logged`, `BusAccess`, `HarteCache`,
`HarteBus` are named identically to their Phase 1 definitions throughout.

One deliberate cross-task edit: the opcode-count assertion in
`tests/SixtyFiveXX.Tests/Opcodes6502Tests.cs` and
`tests/SixtyFiveXX.Conformance/Harte6502Tests.cs` is updated by every task from 151 up to
256. That is intentional — it makes each task's contribution to coverage explicit and
fails loudly if a task adds the wrong number of rows.
