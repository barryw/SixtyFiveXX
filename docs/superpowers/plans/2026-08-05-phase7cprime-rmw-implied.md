# Phase 7c′ — read-modify-writes, the implied forms, and Note 17

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 59 further 65816 opcodes — every read-modify-write, the six accumulator forms, the twelve transfers, `XBA`, the flag instructions and `NOP` — certified per-cycle against 1,180,000 SingleStepTests vectors, taking the core to 212 of 256.

**Architecture:** The 65816 emitter has never emitted a read-modify-write. This phase adds that access class, and with it datasheet Note 17: the RMW middle cycle is a **write** in emulation mode and a **read** in native, decided at run time rather than at table-build time. It is expressed as six conditional slots of which any execution runs four or five, so every micro-op keeps a static entry in `MicroOps.IsWriteCycle`. The other 53 opcodes fetch no operand at all; they keep `Width.None` and test their width flag inside their `Exec` arm.

**Tech Stack:** C# 13, .NET 8 and .NET 10 (both must pass), xUnit, no NuGet dependencies in `src/`.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7c′".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md`. **§9 is the cycle-by-cycle specification the addressing engine was built against.** §12 is phase 7c's, including four still-open gaps. Task 1 of this plan adds **§13**. §3 records two places the most-cited 65816 book is wrong.

**Scope:** `ASL`/`LSR`/`ROL`/`ROR`/`INC`/`DEC` in `dp`, `dp,X`, `abs`, `abs,X` and on the accumulator (30), `TSB`/`TRB` in `dp` and `abs` (4), the twelve transfers, `XBA`, seven flag instructions, `INX`/`INY`/`DEX`/`DEY`, and `NOP`. Explicitly **not** in scope: control flow, the stack, interrupts, `MVN`/`MVP`, `COP`/`WDM`, `WAI`/`STP` — those are the 44 opcodes of phase 7d.

## Global Constraints

- **The five 8-bit cores must not change.** **1,309** of the conformance tests are theirs and must stay at 1,309 passing on both TFMs. Any drift is a defect, not a trade.
- **Baselines measured on this branch's merge-base:** unit **508** with `--filter "Category!=Performance"` (509 unfiltered — the extra one is the throughput gate), conformance **1616**.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on with documentation generation, so **every public member needs an XML doc comment**.
- Both target frameworks must pass. Iterate with `-f net10.0`; run both before declaring a task done.
- **This phase adds no public API.** `PublicSurfaceTests.ExpectedPublicTypes` must be **unchanged**.
- **Vectors:** `SingleStepTests/65816`, `v1/{opcode:x2}.e.json` and `.n.json`, roughly 11 MB per opcode across both modes. This phase pulls about **650 MB**. Never commit a vector file.
- **Running the conformance suite: pass an explicit 600000 ms timeout on the Bash call.** The default is 120 seconds and the suite takes 3–6 minutes per framework; in phase 7c that default silently auto-backgrounded a run and stalled a task with everything uncommitted.
- **Restore probe files with `git checkout --`, never `mv file.bak file`** — the latter preserves an old mtime and defeats MSBuild's staleness detection, which cost a phase-7c task two phantom failures.
- Conventional Commits. Branch `phase7cprime-rmw-implied`, already created, forked from `main` at the phase-7c merge. **Do not push `main` without `[skip ci]`** — a non-skipped push cuts a public nuget.org release.

## Established facts — verified, do not re-derive

- **The addressing engine is certified.** All fifteen modes pass. `dp`, `dp,X`, `abs` and `abs,X` — the four this phase's memory RMWs use — are each certified by 7b and 7c against hundreds of thousands of vectors. Adding an opcode is a table entry plus an `Exec` arm, unless it needs a new access class, which the RMWs do.
- **`_wide` holds the operand width**, resolved once per instruction in `FetchOpcode` from the opcode's declared `Width`. Read-modify-writes fetch an operand, so they declare `Width.M` and `_wide` is available to them. The 53 operand-less opcodes declare `Width.None` and must **not** consult `_wide`.
- **Emulation mode forces `m = 1`, `x = 1`, `XH = YH = $00`, `SH = $01`**, continuously, in `FetchOpcode` under a variant guard. Two consequences this phase depends on: the Note 17 write-form is always 8-bit, and any 16-bit path is native-only.
- **`Flag.M` (0x20) is the same bit as `Flag.U`; `Flag.X` (0x10) is the same bit as `Flag.B`.** Every width test in variant-shared code must read `TVariant.Variant != CpuVariant.W65C816 || ...`, **variant test first**, so it folds to `if (true)` on the five 8-bit cores. Conformance cannot catch a violation: 0 of 10,000 `6502/a5` vectors have bit 5 of P clear. Phase 7b shipped this bug once; `UnusedFlagBitRegressionTests` exists for it.
- **`A8`'s setter preserves A's high byte on the 65816** — the hidden B accumulator — and folds to a plain assignment on the 8-bit cores. `X8`/`Y8` deliberately do **not**: whenever `x` is set, `XH`/`YH` are `$00`.
- **The existing 8-bit RMW micro-ops are `RmwRead`, `RmwModifyWrite` (NMOS), `RmwModifyRead` (CMOS) and `RmwWrite`.** They operate on `_data` and a 16-bit `_addr`; none is bank-aware and none is width-aware, so the 65816 gets its own set rather than reusing them.
- **`MicroOps.IsWriteCycle` is a static `bool[]` indexed by micro-op**, consulted by `IsWriteCycleNext()` on every tick of every core because RDY must never halt a write. Keeping it static is the entire reason for the six-slot shape.
- **`BusPinsTests.EveryMicroOpHasAPinClassification`** fails on any new `MicroOp` that is neither given pins nor declared an internal cycle.
- **The bank-carry exclusion set** in `MicroOpTable.EmitAddressed816` is `DirectPage`, `DirectPageX`, `DirectPageY`, `StackRelative`. `dp` and `dp,X` are in it; `abs` and `abs,X` are not and must carry.
- **The two width tripwires do not catch a uniformly-wrong `Width`**, and `Cpu`'s constructor never calls `Reset()`, so `P == $00` and both width flags read clear by default. **Any test meaning to discriminate `m` from `x` must set them to opposed values explicitly.**
- **The disassembler does not decode the 65816's own addressing modes** and throws `NotSupportedException`. That is phase 7e. Do not add arms.

## Carry-forward from phase 7c — live, and out of scope here

1. `MicroOp.PullP` masks `~Flag.B`, which is also `~Flag.X`. `PLP`/`RTI` would clear the index-width flag on a native 65816. Phase 7d.
2. `ImpliedExec`, `FetchAddrHiX`/`Y`, the branch micro-ops, `JmpAbs`, `BrkPad`, `IntDummy` and every stack micro-op still compute a bare 16-bit `PC` and/or `0x0100 + S8`. Phase 7d.
3. The 65816 IRQ sequence mutates `S` and memory before `Unimplemented816` throws. Phase 7d.
4. Research §12 has four recorded gaps where the sources are silent — the decimal correction algorithm at 8 bits, decimal `V`, invalid BCD digits, and part of `Z`/`C` sourcing. Nothing in this phase touches decimal mode.

**This phase does clear one:** `Sequences.RmwMiddle`'s `Unimplemented816` placeholder, which has stood since 7b.

## File Structure

| File | Responsibility |
| --- | --- |
| `docs/superpowers/research/2026-08-03-65816-reference-sources.md` | Modify (task 1). New §13. |
| `src/SixtyFiveXX/MicroOp.cs` | Modify. Eight new RMW micro-ops and their pin/write/internal classification. |
| `src/SixtyFiveXX/MicroOpTable.cs` | Modify. An `Access.ReadModifyWrite` branch in `EmitAddressed816`; the implied-mode 65816 branch in `Emit816`. |
| `src/SixtyFiveXX/Cpu.cs` | Modify. The eight new micro-op cases. |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Modify. 16-bit ALU helpers and every new `Exec` arm. |
| `src/SixtyFiveXX/Op.cs` | Modify (task 5). `Txy`, `Tyx`, `Tcd`, `Tdc`, `Tcs`, `Tsc`, `Xba`. |
| `src/SixtyFiveXX/Opcodes65C816.cs` | Modify, once per opcode task. 59 new entries. |
| `tests/SixtyFiveXX.Tests/Banked816TestMachine.cs` | Modify (task 2). A bus access log on `BankedBus`, for asserting RMW cycle *direction*. |
| `tests/SixtyFiveXX.Tests/W65C816RmwTests.cs` | **Create** (task 2). Note 17, 16-bit RMW ordering, RMW bank carry. |
| `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs` | **Create** (task 4). Accumulator forms, transfers, `XBA`, index increments. |
| `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` | Modify, once per opcode task. `ExpectedImplementedOpcodes`. |

## The opcode map for this phase

Listed once, here, so every task can be checked against one place. Derived from the 65816 opcode matrix and cross-checked against the 65C02 table already in this repository, where all of these bytes except the four 65816-only transfers and `XBA` already appear with the same mnemonics.

| Group | Bytes |
| --- | --- |
| `ASL` | `$06` dp, `$16` dp,X, `$0E` abs, `$1E` abs,X, `$0A` A |
| `LSR` | `$46` dp, `$56` dp,X, `$4E` abs, `$5E` abs,X, `$4A` A |
| `ROL` | `$26` dp, `$36` dp,X, `$2E` abs, `$3E` abs,X, `$2A` A |
| `ROR` | `$66` dp, `$76` dp,X, `$6E` abs, `$7E` abs,X, `$6A` A |
| `INC` | `$E6` dp, `$F6` dp,X, `$EE` abs, `$FE` abs,X, `$1A` A |
| `DEC` | `$C6` dp, `$D6` dp,X, `$CE` abs, `$DE` abs,X, `$3A` A |
| `TSB` | `$04` dp, `$0C` abs |
| `TRB` | `$14` dp, `$1C` abs |
| Transfers | `$AA` TAX, `$A8` TAY, `$8A` TXA, `$98` TYA, `$9A` TXS, `$BA` TSX, `$9B` TXY, `$BB` TYX, `$5B` TCD, `$7B` TDC, `$1B` TCS, `$3B` TSC |
| `XBA` | `$EB` |
| Flags | `$18` CLC, `$38` SEC, `$58` CLI, `$78` SEI, `$B8` CLV, `$D8` CLD, `$F8` SED |
| Index ±1 | `$E8` INX, `$C8` INY, `$CA` DEX, `$88` DEY |
| `NOP` | `$EA` |

Note `$EB` is `XBA` on the 65816, **not** the 6502's undocumented `SBC` alias — phase 7c deliberately left it out of the `SBC` block for this reason. `$1A`/`$3A` are `INC A`/`DEC A`, which exist on the 65C02 and the 65816 but not the NMOS 6502.

---

### Task 1: Research §13 — the five unsettled questions

**No code.** Phase 7c's equivalent task is why 2,420,000 vectors went green on their first run with no cycle count tuned against a failure. This repeats it.

**Files:**
- Modify: `docs/superpowers/research/2026-08-03-65816-reference-sources.md` (append §13)

**Interfaces:**
- Produces: research §13, cited by section number from tasks 2–6. §13.1 the RMW per-cycle sequences and the middle cycle's true nature, §13.2 the 16-bit write order, §13.3 cycle formulas for all 59 opcodes, §13.4 the transfer width and flag rules, §13.5 `XBA`.

**Sources**, fetched rather than recalled:
- Clark, "65C816 Opcodes", via the GitHub mirror (6502.org 404s non-browser agents): `https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
- WDC W65C816S datasheet: `https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf`, Table 5-7 pp. 36–42, Note 17.

**The honesty rule.** Where a source is silent, **record that it is silent, in those words.** Do not fill a gap from memory, do not infer 65816 behaviour from the 6502 or 65C02 without saying that is what you are doing, and do not write a number you cannot cite. Phase 7c's §12 records four such gaps and each one was useful; the one time that phase guessed, the vectors disagreed.

Two search traps §12 records, both of which cost that phase a revision: **Clark names flags by letter far more often than by name — search `d flag`, not `decimal`** — and the document is form-feed paginated, so a literal phrase can split across a blank line and fail to match.

- [ ] **Step 1: Read §9 and §12 so the new section matches their notation**

§9 gives per-mode blocks: cycle number, VDA/VPA pair, address-bus expression, data-bus contents, and the note gating any conditional cycle. §12 shows how a measured result is recorded versus a cited one. §13 must read alongside both.

- [ ] **Step 2: Settle §13.1 — the RMW per-cycle sequences, and what the middle cycle actually is**

Transcribe Table 5-7's rows for read-modify-write in `dp`, `dp,X`, `abs` and `abs,X`, in §9's format.

**The question that decides code:** in native mode, is the middle cycle a real **read** (VDA asserted, a value on the data bus) or an **internal cycle** (VDA and VPA both 0, value recorded `null`)? Note 17 says emulation "reverts to the NMOS double-write", implying the other case is the CMOS dummy read — but Table 5-7 may show `IO`. It changes the pin string the vectors assert and decides whether `RmwModifyRead816` calls `ReadBus` or `InternalCycle`. Quote the table row.

Record Note 17 verbatim while you are there.

- [ ] **Step 3: Settle §13.2 — the 16-bit write order**

For a 16-bit RMW the reads go low-then-high. State, with the table row as evidence, whether the writes reverse to high-then-low. If Table 5-7 does not show a 16-bit RMW row explicitly, say so and record what it does show.

- [ ] **Step 4: Settle §13.3 — a cycle formula for every one of the 59 opcodes**

Use §5's format and symbols (`m`, `x`, `w` for the direct-page penalty, `p` for a page cross). Include the accumulator forms, the transfers, `XBA`, the flag instructions and `NOP`.

**State in one sentence at the top of §13.3 whether `ASL abs` is `8-2m`.** The six-slot shape assumes 16-bit costs exactly one extra read and one extra write; if the formula disagrees, the shape is wrong and task 2 must change before it is built.

- [ ] **Step 5: Settle §13.4 — the transfer rules, per instruction**

A row per transfer: which flag sizes it, whether it sets `N`/`Z`, and its cycle count. Specifically confirm or refute each of these, with a citation:

- `TAX`, `TAY`, `TSX`, `TXY`, `TYX` are sized by `x`.
- `TXA`, `TYA` are sized by `m`.
- `TCD`, `TDC`, `TCS`, `TSC` are 16-bit regardless of `m` and `x`.
- `TXS` and `TCS` set no flags; the other ten set `N` and `Z`.
- In emulation mode `TXS` and `TCS` force `SH = $01`.

- [ ] **Step 6: Settle §13.5 — `XBA`**

Its cycle count, and whether `N`/`Z` come from the new low byte as an 8-bit result regardless of `m`.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/research/2026-08-03-65816-reference-sources.md
git commit -m "docs: research §13, the facts phase 7c-prime needs before any opcode"
```

**Gate:** `git diff --stat main -- src tests` empty. Every claim carries a named source or an explicit statement that the sources are silent. §13.1's middle-cycle answer and §13.3's `ASL abs` formula are each stated outright.

---

### Task 2: The RMW engine, and `ASL`/`LSR`/`ROL`/`ROR` in four memory modes

16 opcodes, and the phase's only real unknown.

**Files:**
- Modify: `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/Banked816TestMachine.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816RmwTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (153 → 169)

**Interfaces:**
- Consumes: research §13.1, §13.2, §13.3; `_wide`; `EmitAddressed816`.
- Produces: `MicroOp.RmwRead816`, `RmwReadHigh816`, `RmwReadHigh816Carry`, `RmwModifyWrite816`, `RmwModifyRead816`, `RmwWriteHigh816`, `RmwWriteHigh816Carry`, `RmwWrite816`; `private ushort Asl16(ushort)`, `Lsr16`, `Rol16`, `Ror16` in `Cpu.Exec.cs`; a `Log` on `BankedBus`. Tasks 3 and 4 reuse all of them.

**Read research §13 before writing.** Two findings change the code: §13.1 decides whether `RmwModifyRead816` calls `ReadBus` or `InternalCycle`, and §13.2 decides the write order. The sequence below assumes a real read and high-then-low writes; **if §13 says otherwise, follow §13 and say so in your report.**

- [ ] **Step 1: Give `BankedBus` an access log**

RMW correctness is about cycle *direction*, which a value assertion cannot see. In `tests/SixtyFiveXX.Tests/Banked816TestMachine.cs`, add to `BankedBus`:

```csharp
    /// <summary>
    /// Every access in order, for tests that must assert on bus *direction* rather than on the
    /// value left in memory — which is the whole of what datasheet Note 17 is about. Opt-in:
    /// nothing clears it, so a test that does not read it pays only the list allocation.
    /// </summary>
    public readonly List<(int Address, byte Value, bool Write)> Log = [];

    public byte Read(int address)
    {
        var value = this[address];
        Log.Add((address & 0xFFFFFF, value, false));
        return value;
    }

    public void Write(int address, byte value)
    {
        this[address] = value;
        Log.Add((address & 0xFFFFFF, value, true));
    }
```

Replace the existing one-line `Read`/`Write` with these. The indexer stays as it is.

- [ ] **Step 2: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816RmwTests.cs`:

```csharp
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's read-modify-write cycle, which is the one behaviour in this project whose bus
/// direction is decided at run time rather than at table-build time (datasheet Note 17,
/// research document §13.1). These tests assert on the bus log rather than on memory, because
/// the distinguishing fact — whether the middle cycle writes or reads — leaves no trace in the
/// final memory state at all.
/// </summary>
public class W65C816RmwTests
{
    /// <summary>
    /// Note 17: in emulation mode the middle cycle is a WRITE of the unmodified value, the NMOS
    /// double-write. Fails against a core that emits the CMOS dummy read in both modes — the
    /// final memory would be identical, and only the log distinguishes them.
    /// </summary>
    [Fact]
    public void AslDirectPage_EmulationMode_WritesTheOriginalValueBeforeTheResult()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0x21;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;       // emulation: m is forced to 1, so this is an 8-bit RMW
        cpu.State.DP = 0x0000;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write && a.Address == 0x000010);
        Assert.Equal(2, writes.Count);
        Assert.Equal(0x21, writes[0].Value);   // the unmodified value, written back first
        Assert.Equal(0x42, writes[1].Value);   // then the result
    }

    /// <summary>
    /// The native-mode counterpart: one write, not two. Together with the test above this pins
    /// both arms of Note 17, so neither can be deleted unnoticed.
    /// </summary>
    [Fact]
    public void AslDirectPage_NativeMode_DoesNotWriteTheOriginalValueBack()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0x21;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator, so the same one-byte RMW
        cpu.State.DP = 0x0000;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write && a.Address == 0x000010);
        Assert.Single(writes);
        Assert.Equal(0x42, writes[0].Value);
    }

    /// <summary>
    /// A 16-bit read-modify-write reads low-then-high and writes high-then-low (research
    /// document §13.2). Asserting the order, not just the bytes, is the point: a core that
    /// wrote low-then-high would leave identical memory.
    /// </summary>
    [Fact]
    public void AslAbsolute_SixteenBit_WritesTheHighByteFirst()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0E;       // ASL abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // AA = $2000
        ram[0x002000] = 0x34;     // low
        ram[0x002001] = 0x12;     // high -> operand $1234

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit
        cpu.State.DBR = 0x00;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write);
        Assert.Equal(2, writes.Count);
        Assert.Equal(0x002001, writes[0].Address);   // high byte first
        Assert.Equal(0x24, writes[0].Value);         // $1234 << 1 = $2468
        Assert.Equal(0x002000, writes[1].Address);
        Assert.Equal(0x68, writes[1].Value);
    }

    /// <summary>
    /// A 16-bit RMW through a DBR-relative mode carries into the next bank, exactly as a 16-bit
    /// load does (Clark §5.2 Example 2). Zero vector coverage is likely here for the same reason
    /// it was for the loads: it needs m=0 with the effective address landing on $xxFFFF.
    /// </summary>
    [Fact]
    public void AslAbsolute_SixteenBit_CarriesTheHighByteIntoTheNextBank()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0E;       // ASL abs
        ram[0xC001] = 0xFF;
        ram[0xC002] = 0xFF;       // AA = $FFFF
        ram[0x12FFFF] = 0x34;     // low, DBR,AA
        ram[0x130000] = 0x12;     // high, carried into bank $13
        ram[0x120000] = 0x99;     // decoy: where a bank-wrapping read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.DBR = 0x12;

        cpu.Step();

        Assert.Equal(0x68, ram[0x12FFFF]);
        Assert.Equal(0x24, ram[0x130000]);
        Assert.Equal(0x99, ram[0x120000]);   // untouched
    }

    /// <summary>
    /// The symmetric case: direct page is bank-0 confined, so a 16-bit RMW whose low byte sits
    /// at $00FFFF takes its high byte from $000000, not $010000. Fails if DirectPage is dropped
    /// from EmitAddressed816's bank-0 exclusion set for the read-modify-write path.
    /// </summary>
    [Fact]
    public void AslDirectPage_SixteenBit_WrapsTheHighByteWithinBankZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x00;
        ram[0x00FFFF] = 0x34;     // low, 0,D+DO
        ram[0x000000] = 0x12;     // high, wrapped within bank 0
        ram[0x010000] = 0x99;     // decoy

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.DP = 0xFFFF;    // D + DO = $FFFF

        cpu.Step();

        Assert.Equal(0x68, ram[0x00FFFF]);
        Assert.Equal(0x24, ram[0x000000]);
        Assert.Equal(0x99, ram[0x010000]);
    }
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816RmwTests"`
Expected: **FAIL, 5**, with `UndefinedOpcodeException` — `$06` and `$0E` are not in the 65816 table.

- [ ] **Step 4: Add the eight micro-ops**

In `src/SixtyFiveXX/MicroOp.cs`, beside the existing 65816 members, with an XML `<summary>` on each naming the research §13.1 row it comes from:

```csharp
    RmwRead816,
    RmwReadHigh816,
    RmwReadHigh816Carry,
    RmwModifyWrite816,
    RmwModifyRead816,
    RmwWriteHigh816,
    RmwWriteHigh816Carry,
    RmwWrite816,
```

Classify them in `MicroOps`:

- `BuildWriteTable` gains `RmwModifyWrite816`, `RmwWriteHigh816`, `RmwWriteHigh816Carry`, `RmwWrite816`. **RDY must never halt these.**
- `BuildPinsTable` gains all eight in the `BusPins.Vda | BusPins.Mlb` group — the locked cycles of a read-modify-write, which is what `MLB` exists to signal. Extend that group's comment to say the 65816's are there too.
- `BuildInternalCycleTable` gains `RmwModifyRead816` **only if research §13.1 says the native middle cycle is an internal cycle.** If §13.1 says it is a real read, it goes in the pins group above and not here.

`BusPinsTests.EveryMicroOpHasAPinClassification` fails until every one is classified — that is the tripwire working.

- [ ] **Step 5: Implement the eight cases**

In `src/SixtyFiveXX/Cpu.cs`'s `Execute` switch:

```csharp
            case MicroOp.RmwRead816:
                _data = ReadBus(_addr);
                if (!_wide)
                {
                    _mpc++;                       // 8-bit: skip the high-byte read
                    if (!_s.E) _mpc++;            // native: skip the NMOS write-form too
                }
                break;

            case MicroOp.RmwReadHigh816:
                _data16 = (ushort)((ReadBus(HighByteAddressBank0()) << 8) | _data);
                _mpc++;                           // 16-bit is native-only, so always skip the write-form
                break;

            case MicroOp.RmwReadHigh816Carry:
                _data16 = (ushort)((ReadBus(HighByteAddressCarry()) << 8) | _data);
                _mpc++;
                break;

            case MicroOp.RmwModifyWrite816:
                // Datasheet Note 17: in emulation mode the middle cycle writes the UNMODIFIED
                // value back, as NMOS does. Emulation forces m=1, so this is always 8-bit.
                WriteBus(_addr, _data);
                Exec();
                _mpc += 2;                        // skip the read-form and the high-byte write
                break;

            case MicroOp.RmwModifyRead816:
                // Native mode: the CMOS-style dummy read. Same cycle, opposite direction.
                ReadBus(_addr);
                Exec();
                if (!_wide) _mpc++;               // 8-bit: skip the high-byte write
                break;

            case MicroOp.RmwWriteHigh816:
                WriteBus(HighByteAddressBank0(), (byte)(_data16 >> 8));
                break;

            case MicroOp.RmwWriteHigh816Carry:
                WriteBus(HighByteAddressCarry(), (byte)(_data16 >> 8));
                break;

            case MicroOp.RmwWrite816:
                WriteBus(_addr, _wide ? (byte)_data16 : _data);
                break;
```

**If research §13.1 says the native middle cycle is an internal cycle**, `RmwModifyRead816`'s `ReadBus(_addr)` becomes `InternalCycle(_addr)` and everything else is unchanged.

- [ ] **Step 6: Emit the sequence**

In `MicroOpTable.EmitAddressed816`, the access tail becomes a three-way switch. Replace:

```csharp
        if (info.Access == Access.Write)
        {
            ops.Add(MicroOp.ExecWrite816);
            ops.Add(carry ? MicroOp.ExecWriteHigh816Carry : MicroOp.ExecWriteHigh816);
        }
        else
        {
            ops.Add(MicroOp.ReadExec816);
            ops.Add(carry ? MicroOp.ReadExecHigh816Carry : MicroOp.ReadExecHigh816);
        }
```

with:

```csharp
        switch (info.Access)
        {
            case Access.Write:
                ops.Add(MicroOp.ExecWrite816);
                ops.Add(carry ? MicroOp.ExecWriteHigh816Carry : MicroOp.ExecWriteHigh816);
                break;

            // Six slots, of which any one execution runs four (8-bit) or five (16-bit). The rest
            // are skipped by the preceding micro-op, the same conditional-slot idiom
            // DirectPagePenalty uses — which is what keeps every one of them statically
            // classified in MicroOps.IsWriteCycle, consulted on every tick of all six cores
            // because RDY must never halt a write. Datasheet Note 17 decides which of the two
            // middle forms runs, at run time, from E.
            case Access.ReadModifyWrite:
                ops.AddRange([
                    MicroOp.RmwRead816,
                    carry ? MicroOp.RmwReadHigh816Carry : MicroOp.RmwReadHigh816,
                    MicroOp.RmwModifyWrite816,
                    MicroOp.RmwModifyRead816,
                    carry ? MicroOp.RmwWriteHigh816Carry : MicroOp.RmwWriteHigh816,
                    MicroOp.RmwWrite816,
                ]);
                break;

            default:
                ops.Add(MicroOp.ReadExec816);
                ops.Add(carry ? MicroOp.ReadExecHigh816Carry : MicroOp.ReadExecHigh816);
                break;
        }
```

- [ ] **Step 7: Add the 16-bit shift helpers**

In `src/SixtyFiveXX/Cpu.Exec.cs`, beside the existing 8-bit `Asl`/`Lsr`/`Rol`/`Ror`:

```csharp
    /// <summary>16-bit <c>ASL</c>. The 65816 native-mode counterpart of <see cref="Asl"/>.</summary>
    private ushort Asl16(ushort value)
    {
        _s.C = (value & 0x8000) != 0;
        var result = (ushort)(value << 1);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>LSR</c>. See <see cref="Asl16"/>.</summary>
    private ushort Lsr16(ushort value)
    {
        _s.C = (value & 0x0001) != 0;
        var result = (ushort)(value >> 1);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>ROL</c>. See <see cref="Asl16"/>.</summary>
    private ushort Rol16(ushort value)
    {
        var carryIn = _s.C ? 1 : 0;
        _s.C = (value & 0x8000) != 0;
        var result = (ushort)((value << 1) | carryIn);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>ROR</c>. See <see cref="Asl16"/>.</summary>
    private ushort Ror16(ushort value)
    {
        var carryIn = _s.C ? 0x8000 : 0x0000;
        _s.C = (value & 0x0001) != 0;
        var result = (ushort)((value >> 1) | carryIn);
        SetZN16(result);
        return result;
    }
```

- [ ] **Step 8: Widen the four shift `Exec` arms**

Replace the four memory-shift arms in `Cpu.Exec.cs`:

```csharp
            // Shifts and rotates on memory. Width-aware for the 65816: _data carries the operand
            // at 8 bits, _data16 at 16. The variant guard comes first so the five 8-bit cores
            // never load _wide — see the remarks on _wide and Op.Lda's own comment.
            case Op.Asl:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Asl(_data);
                else _data16 = Asl16(_data16);
                break;

            case Op.Lsr:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Lsr(_data);
                else _data16 = Lsr16(_data16);
                break;

            case Op.Rol:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Rol(_data);
                else _data16 = Rol16(_data16);
                break;

            case Op.Ror:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Ror(_data);
                else _data16 = Ror16(_data16);
                break;
```

- [ ] **Step 9: Add the 16 table entries**

In `src/SixtyFiveXX/Opcodes65C816.cs`:

```csharp
        // Read-modify-write shifts. Width.M — the operand comes from memory and is sized by m.
        Set(0x06, "ASL", AddrMode.DirectPage,  Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x16, "ASL", AddrMode.DirectPageX, Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x0E, "ASL", AddrMode.Absolute,    Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x1E, "ASL", AddrMode.AbsoluteX,   Op.Asl, Access.ReadModifyWrite, Width.M);

        Set(0x46, "LSR", AddrMode.DirectPage,  Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x56, "LSR", AddrMode.DirectPageX, Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x4E, "LSR", AddrMode.Absolute,    Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x5E, "LSR", AddrMode.AbsoluteX,   Op.Lsr, Access.ReadModifyWrite, Width.M);

        Set(0x26, "ROL", AddrMode.DirectPage,  Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x36, "ROL", AddrMode.DirectPageX, Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x2E, "ROL", AddrMode.Absolute,    Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x3E, "ROL", AddrMode.AbsoluteX,   Op.Rol, Access.ReadModifyWrite, Width.M);

        Set(0x66, "ROR", AddrMode.DirectPage,  Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x76, "ROR", AddrMode.DirectPageX, Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x6E, "ROR", AddrMode.Absolute,    Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x7E, "ROR", AddrMode.AbsoluteX,   Op.Ror, Access.ReadModifyWrite, Width.M);
```

Update the class `<remarks>` to 169 defined.

- [ ] **Step 10: Run the unit tests**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "Category!=Performance"`
Expected: **PASS, 513** (508 + 5).

- [ ] **Step 11: Prove the Note 17 branch discriminates, by mutation**

Temporarily make `RmwRead816` skip the write-form unconditionally — change `if (!_s.E) _mpc++;` to `_mpc++;`.

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~AslDirectPage_EmulationMode"`
Expected: **FAIL** — one write where two were expected.

Revert with `git checkout -- src/SixtyFiveXX/Cpu.cs` and re-run to confirm it passes. Record the output in your report. Note 17 is this phase's central claim; a test that would pass without it is worth nothing.

- [ ] **Step 12: Raise `ExpectedImplementedOpcodes` to 169, run conformance, both TFMs, commit**

Run: `dotnet test tests/SixtyFiveXX.Conformance` **with an explicit 600000 ms timeout.**
Expected: **PASS, 1648** (1616 + 32). Roughly 180 MB of new vectors. The first 1,309 must not move.

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests tests/SixtyFiveXX.Conformance
git commit -m "feat: 65816 read-modify-write, Note 17's run-time direction, and the four memory shifts"
```

**Gate:** conformance **1648**, unit **513**, both TFMs, 320,000 new vectors green, the mutation experiment recorded.

---

### Task 3: `INC`/`DEC` memory, and `TSB`/`TRB`

12 opcodes on the engine task 2 built.

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/W65C816RmwTests.cs` (append)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (169 → 181)

**Interfaces:**
- Consumes: everything task 2 produced. No new micro-ops, no emitter change.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SixtyFiveXX.Tests/W65C816RmwTests.cs`:

```csharp
    /// <summary>
    /// A 16-bit INC carries across the byte boundary rather than wrapping the low byte, which is
    /// the whole difference from two independent 8-bit increments.
    /// </summary>
    [Fact]
    public void IncAbsolute_SixteenBit_CarriesAcrossTheByteBoundary()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xEE;       // INC abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;
        ram[0x002000] = 0xFF;     // low
        ram[0x002001] = 0x00;     // high -> $00FF

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit
        cpu.State.DBR = 0x00;

        cpu.Step();

        Assert.Equal(0x00, ram[0x002000]);
        Assert.Equal(0x01, ram[0x002001]);   // $0100
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// TSB sets Z from the AND of A and memory over the full operative width, then ORs A into
    /// memory. N and V are left alone — unlike BIT, which takes them from the operand.
    /// </summary>
    [Fact]
    public void TsbAbsolute_SixteenBit_SetsZFromTheFullAndAndLeavesNAndVAlone()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0C;       // TSB abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;
        ram[0x002000] = 0x00;
        ram[0x002001] = 0x00;     // memory $0000

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit
        cpu.State.DBR = 0x00;
        cpu.State.A = 0x8001;
        cpu.State.N = false;
        cpu.State.V = false;

        cpu.Step();

        Assert.True(cpu.State.Z);            // $8001 & $0000 == 0
        Assert.Equal(0x01, ram[0x002000]);   // memory |= A
        Assert.Equal(0x80, ram[0x002001]);
        Assert.False(cpu.State.N);           // untouched
        Assert.False(cpu.State.V);
    }

    /// <summary>
    /// TRB clears the bits A has set. Z again comes from the AND, computed before the clear.
    /// </summary>
    [Fact]
    public void TrbDirectPage_SixteenBit_ClearsTheBitsSetInA()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x14;       // TRB dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0xFF;
        ram[0x000011] = 0xFF;     // memory $FFFF

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.DP = 0x0000;
        cpu.State.A = 0x0F0F;

        cpu.Step();

        Assert.False(cpu.State.Z);           // $0F0F & $FFFF != 0
        Assert.Equal(0xF0, ram[0x000010]);
        Assert.Equal(0xF0, ram[0x000011]);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816RmwTests"`
Expected: **FAIL, 3 of 8**, with `UndefinedOpcodeException` for `$EE`, `$0C` and `$14`.

- [ ] **Step 3: Widen the four `Exec` arms**

```csharp
            // Memory increment and decrement, operating in place.
            case Op.Inc:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { _data = (byte)(_data + 1); SetZN(_data); }
                else { _data16 = (ushort)(_data16 + 1); SetZN16(_data16); }
                break;

            case Op.Dec:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { _data = (byte)(_data - 1); SetZN(_data); }
                else { _data16 = (ushort)(_data16 - 1); SetZN16(_data16); }
                break;

            // Test-and-modify. Z comes from the AND, as for BIT, but N and V are left alone.
            case Op.Trb:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { _s.Z = (A8 & _data) == 0; _data = (byte)(_data & ~A8); }
                else { _s.Z = (_s.A & _data16) == 0; _data16 = (ushort)(_data16 & ~_s.A); }
                break;

            case Op.Tsb:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { _s.Z = (A8 & _data) == 0; _data = (byte)(_data | A8); }
                else { _s.Z = (_s.A & _data16) == 0; _data16 = (ushort)(_data16 | _s.A); }
                break;
```

- [ ] **Step 4: Add the 12 table entries**

```csharp
        Set(0xE6, "INC", AddrMode.DirectPage,  Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xF6, "INC", AddrMode.DirectPageX, Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xEE, "INC", AddrMode.Absolute,    Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xFE, "INC", AddrMode.AbsoluteX,   Op.Inc, Access.ReadModifyWrite, Width.M);

        Set(0xC6, "DEC", AddrMode.DirectPage,  Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xD6, "DEC", AddrMode.DirectPageX, Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xCE, "DEC", AddrMode.Absolute,    Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xDE, "DEC", AddrMode.AbsoluteX,   Op.Dec, Access.ReadModifyWrite, Width.M);

        Set(0x04, "TSB", AddrMode.DirectPage,  Op.Tsb, Access.ReadModifyWrite, Width.M);
        Set(0x0C, "TSB", AddrMode.Absolute,    Op.Tsb, Access.ReadModifyWrite, Width.M);

        Set(0x14, "TRB", AddrMode.DirectPage,  Op.Trb, Access.ReadModifyWrite, Width.M);
        Set(0x1C, "TRB", AddrMode.Absolute,    Op.Trb, Access.ReadModifyWrite, Width.M);
```

Update the class `<remarks>` to 181 defined.

- [ ] **Step 5: Run everything, raise `ExpectedImplementedOpcodes` to 181, both TFMs, commit**

Expected: unit **516** (513 + 3), conformance **1672** (1648 + 24). Roughly 135 MB of new vectors.

```bash
git commit -m "feat: 65816 INC and DEC on memory, and TSB/TRB"
```

**Gate:** conformance **1672**, unit **516**, both TFMs, 240,000 new vectors green.

---

### Task 4: The six accumulator forms

The first opcodes in this phase with no operand. They keep `Width.None` and test `_s.M` in their arms.

**Files:**
- Modify: `src/SixtyFiveXX/MicroOpTable.cs` (the implied 65816 branch), `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (181 → 187)

**Interfaces:**
- Consumes: `Asl16`/`Lsr16`/`Rol16`/`Ror16` from task 2; `MicroOp.ImpliedExec816`.
- Produces: the implied-mode routing in `Emit816` that tasks 5 and 6 reuse unchanged.

**Why `Width.None` and not `Width.M`:** these fetch no operand, and `Width` means *the operand fetched from memory is 16 bits*. Keeping that meaning exact is what lets `W65C816WidthTests` keep asserting set equality — a `Width` is declared if and only if a width-deciding access micro-op is reached. Do not declare a `Width` on any opcode in tasks 4, 5 or 6; the tripwire will fail if you do, and it is right to.

- [ ] **Step 1: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs`:

```csharp
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's implied-mode opcodes — the accumulator forms, the transfers, XBA and the index
/// increments. None of them fetches an operand, so none declares a <c>Width</c>; each tests the
/// flag its own result width depends on.
/// <para>
/// Every test that means to discriminate <c>m</c> from <c>x</c> sets BOTH to opposed values.
/// <c>Cpu</c>'s constructor does not call <c>Reset()</c>, so <c>P == $00</c> and both flags read
/// clear by default — a test that sets only one leaves the two indistinguishable, which is a hole
/// phase 7c found the hard way.
/// </para>
/// </summary>
public class W65C816ImpliedTests
{
    /// <summary>
    /// A 16-bit ASL on the accumulator shifts bit 14 into bit 15 and takes carry from bit 15.
    /// Fails against an 8-bit shift: A's high byte would be untouched and C would come from bit 7.
    /// </summary>
    [Fact]
    public void AslAccumulator_SixteenBit_ShiftsTheFullAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0A;       // ASL A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.XFlag = true;   // opposed, so a width read from x would be visible
        cpu.State.A = 0x4001;

        cpu.Step();

        Assert.Equal(0x8002, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// An 8-bit accumulator operation must not disturb A's high byte — the hidden B accumulator.
    /// </summary>
    [Fact]
    public void AslAccumulator_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0A;       // ASL A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.XFlag = false;  // opposed
        cpu.State.A = 0x1221;

        cpu.Step();

        Assert.Equal(0x1242, cpu.State.A);
    }

    /// <summary>
    /// A 16-bit INC A wraps at sixteen bits, not eight.
    /// </summary>
    [Fact]
    public void IncAccumulator_SixteenBit_WrapsAtSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x1A;       // INC A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = true;   // opposed
        cpu.State.A = 0xFFFF;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.A);
        Assert.True(cpu.State.Z);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: **FAIL, 3**, `UndefinedOpcodeException` for `$0A` and `$1A`.

- [ ] **Step 3: Route implied opcodes through `ImpliedExec816`**

In `MicroOpTable.Emit816`, before the final `EmitAddressed816(ops, info);`:

```csharp
        // Every 65816 implied and accumulator-mode instruction is two cycles: the opcode fetch,
        // then one internal cycle at PBR,PC+1 (research document §9 row 19a, the shape XCE
        // already uses). They fetch no operand, so they declare no Width and never reach a
        // width-deciding micro-op; each arm in Cpu.Exec tests the flag its own result depends on.
        if (info.Mode is AddrMode.Implied or AddrMode.Accumulator)
        {
            ops.Add(MicroOp.ImpliedExec816);
            return;
        }
```

`Op.Xce`'s existing branch already returns before this, so it is unaffected.

`MicroOp.ImpliedExec816` already performs an internal cycle at `PBR,PC` and then calls `Exec()` — verified while this plan was written. **It needs no change**, and every opcode in tasks 4, 5 and 6 gets its two-cycle shape from it for free. If you find yourself editing that micro-op, stop and report: something about the routing is wrong instead.

- [ ] **Step 4: Widen the six accumulator arms**

```csharp
            // The same four shifts on the accumulator. Width comes from m directly rather than
            // from _wide: these fetch no operand, so they declare Width.None (see Width's own
            // remarks). Variant test first, so the five 8-bit cores fold to the code they had.
            case Op.AslA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) A8 = Asl(A8);
                else _s.A = Asl16(_s.A);
                break;

            case Op.LsrA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) A8 = Lsr(A8);
                else _s.A = Lsr16(_s.A);
                break;

            case Op.RolA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) A8 = Rol(A8);
                else _s.A = Rol16(_s.A);
                break;

            case Op.RorA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) A8 = Ror(A8);
                else _s.A = Ror16(_s.A);
                break;

            case Op.IncA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) { A8 = (byte)(A8 + 1); SetZN(A8); }
                else { _s.A = (ushort)(_s.A + 1); SetZN16(_s.A); }
                break;

            case Op.DecA:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) { A8 = (byte)(A8 - 1); SetZN(A8); }
                else { _s.A = (ushort)(_s.A - 1); SetZN16(_s.A); }
                break;
```

- [ ] **Step 5: Add the six table entries**

```csharp
        // Accumulator forms. AddrMode.Accumulator, Access.None, and no Width: they fetch nothing.
        Set(0x0A, "ASL", AddrMode.Accumulator, Op.AslA, Access.None);
        Set(0x4A, "LSR", AddrMode.Accumulator, Op.LsrA, Access.None);
        Set(0x2A, "ROL", AddrMode.Accumulator, Op.RolA, Access.None);
        Set(0x6A, "ROR", AddrMode.Accumulator, Op.RorA, Access.None);
        Set(0x1A, "INC", AddrMode.Accumulator, Op.IncA, Access.None);
        Set(0x3A, "DEC", AddrMode.Accumulator, Op.DecA, Access.None);
```

Update the class `<remarks>` to 187 defined.

- [ ] **Step 6: Run everything, raise `ExpectedImplementedOpcodes` to 187, both TFMs, commit**

Expected: unit **519** (516 + 3), conformance **1684** (1672 + 12).

```bash
git commit -m "feat: 65816 accumulator shifts and INC A/DEC A at both widths"
```

**Gate:** conformance **1684**, unit **519**, both TFMs, 120,000 new vectors green.

---

### Task 5: The twelve transfers, and `XBA`

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs` (append)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (187 → 200)

**Interfaces:**
- Consumes: research §13.4 and §13.5; the implied routing from task 4.
- Produces: `Op.Txy`, `Op.Tyx`, `Op.Tcd`, `Op.Tdc`, `Op.Tcs`, `Op.Tsc`, `Op.Xba`.

**Follow research §13.4 for the width and flag rules.** The arms below implement what the spec expects §13.4 to confirm — `TAX`/`TAY`/`TSX`/`TXY`/`TYX` sized by `x`, `TXA`/`TYA` sized by `m`, the four `TC*`/`T*C` always 16-bit, `TXS`/`TCS` setting no flags. **If §13.4 says otherwise, follow §13.4 and say so in your report.**

- [ ] **Step 1: Write the failing tests**

Append to `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs`:

```csharp
    /// <summary>
    /// TAX is sized by x, not m. Flags are opposed so a width read from the wrong one is visible:
    /// with m=1 and x=0 an m-sized TAX would move only the low byte.
    /// </summary>
    [Fact]
    public void Tax_IsSizedByTheXFlagNotTheMFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAA;       // TAX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.XFlag = false;  // 16-bit index — this is what must govern
        cpu.State.A = 0x1234;
        cpu.State.X = 0x0000;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.X);
    }

    /// <summary>
    /// With an 8-bit index, TAX moves only A's low byte and X's high byte is cleared — XH is $00
    /// whenever x is set.
    /// </summary>
    [Fact]
    public void Tax_EightBitIndex_MovesOnlyTheLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAA;       // TAX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // opposed
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x0034, cpu.State.X);
    }

    /// <summary>
    /// TCD moves all sixteen bits regardless of m and x. Both flags are set to the widths that
    /// would truncate if either governed.
    /// </summary>
    [Fact]
    public void Tcd_IsAlwaysSixteenBit()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x5B;       // TCD

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator — must NOT narrow the transfer
        cpu.State.XFlag = true;   // 8-bit index — likewise
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.DP);
    }

    /// <summary>
    /// TXS sets no flags, on the 65816 as on the 8-bit cores. N and Z are pre-set to values a
    /// flag-setting implementation would overwrite.
    /// </summary>
    [Fact]
    public void Txs_SetsNoFlags()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x9A;       // TXS

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;
        cpu.State.X = 0x0000;     // would set Z if TXS set flags
        cpu.State.Z = false;
        cpu.State.N = true;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.S);
        Assert.False(cpu.State.Z);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// XBA swaps A's two halves and sets N and Z from the new low byte as an 8-bit result, even
    /// with a 16-bit accumulator (research document §13.5).
    /// </summary>
    [Fact]
    public void Xba_SwapsTheHalvesAndSetsFlagsFromTheNewLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xEB;       // XBA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0080;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.Z);    // new low byte is $00
        Assert.False(cpu.State.N);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Expected: **FAIL, 5 of 8**, `UndefinedOpcodeException` for `$AA`, `$5B`, `$9A` and `$EB`.

- [ ] **Step 3: Add the seven `Op` members**

In `src/SixtyFiveXX/Op.cs`, in the 65816 group:

```csharp
    /// <summary>Transfer X to Y, and Y to X. Sized by the <c>x</c> flag. 65816 only.</summary>
    Txy, Tyx,

    /// <summary>
    /// Transfers between the 16-bit accumulator and the direct and stack registers. All four move
    /// all sixteen bits regardless of <c>m</c> and <c>x</c> — the registers they touch have no
    /// narrow form. <c>Tcs</c> sets no flags, as <see cref="Txs"/> does not. 65816 only.
    /// </summary>
    Tcd, Tdc, Tcs, Tsc,

    /// <summary>
    /// Exchange the two halves of the 16-bit accumulator. Flags come from the new low byte as an
    /// 8-bit result regardless of <c>m</c> — research document §13.5. 65816 only.
    /// </summary>
    Xba,
```

- [ ] **Step 4: Widen the existing transfer arms and add the new ones**

Replace the transfer block in `Cpu.Exec.cs`:

```csharp
            // Transfers. Width comes from the DESTINATION register's flag: an index destination
            // is sized by x, an accumulator destination by m. The four TC*/T*C forms move all
            // sixteen bits regardless, and TXS/TCS set no flags. Research document §13.4.
            case Op.Tax:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { X8 = A8; SetZN(X8); }
                else { _s.X = _s.A; SetZN16(_s.X); }
                break;

            case Op.Tay:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { Y8 = A8; SetZN(Y8); }
                else { _s.Y = _s.A; SetZN16(_s.Y); }
                break;

            case Op.Tsx:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { X8 = S8; SetZN(X8); }
                else { _s.X = _s.S; SetZN16(_s.X); }
                break;

            case Op.Txa:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) { A8 = X8; SetZN(A8); }
                else { _s.A = _s.X; SetZN16(_s.A); }
                break;

            case Op.Tya:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M) { A8 = Y8; SetZN(A8); }
                else { _s.A = _s.Y; SetZN16(_s.A); }
                break;

            // TXS takes no flags. On the 65816 it moves all sixteen bits in native mode; S8's
            // setter forces SH to $01 in emulation mode, which is exactly the required behaviour.
            case Op.Txs:
                if (TVariant.Variant != CpuVariant.W65C816) S8 = X8;
                else if (_s.E) S8 = X8;
                else _s.S = _s.X;
                break;

            case Op.Txy:
                if (_s.XFlag) { Y8 = X8; SetZN(Y8); }
                else { _s.Y = _s.X; SetZN16(_s.Y); }
                break;

            case Op.Tyx:
                if (_s.XFlag) { X8 = Y8; SetZN(X8); }
                else { _s.X = _s.Y; SetZN16(_s.X); }
                break;

            // Always sixteen bits. Tcs sets no flags, as Txs does not.
            case Op.Tcd: _s.DP = _s.A; SetZN16(_s.DP); break;
            case Op.Tdc: _s.A = _s.DP; SetZN16(_s.A); break;
            case Op.Tsc: _s.A = _s.S; SetZN16(_s.A); break;

            case Op.Tcs:
                if (_s.E) S8 = A8;
                else _s.S = _s.A;
                break;

            case Op.Xba:
                _s.A = (ushort)((_s.A >> 8) | (_s.A << 8));
                SetZN((byte)_s.A);
                break;
```

`Op.Txy`, `Op.Tyx`, the four `TC*`/`T*C` and `Op.Xba` appear in no 8-bit table, so they need no variant guard — the same exemption `Op.Adc816` has.

- [ ] **Step 5: Add the 13 table entries**

```csharp
        // Transfers and XBA. Implied, no operand, so no Width.
        Set(0xAA, "TAX", AddrMode.Implied, Op.Tax, Access.None);
        Set(0xA8, "TAY", AddrMode.Implied, Op.Tay, Access.None);
        Set(0x8A, "TXA", AddrMode.Implied, Op.Txa, Access.None);
        Set(0x98, "TYA", AddrMode.Implied, Op.Tya, Access.None);
        Set(0x9A, "TXS", AddrMode.Implied, Op.Txs, Access.None);
        Set(0xBA, "TSX", AddrMode.Implied, Op.Tsx, Access.None);
        Set(0x9B, "TXY", AddrMode.Implied, Op.Txy, Access.None);
        Set(0xBB, "TYX", AddrMode.Implied, Op.Tyx, Access.None);
        Set(0x5B, "TCD", AddrMode.Implied, Op.Tcd, Access.None);
        Set(0x7B, "TDC", AddrMode.Implied, Op.Tdc, Access.None);
        Set(0x1B, "TCS", AddrMode.Implied, Op.Tcs, Access.None);
        Set(0x3B, "TSC", AddrMode.Implied, Op.Tsc, Access.None);
        Set(0xEB, "XBA", AddrMode.Implied, Op.Xba, Access.None);
```

Update the class `<remarks>` to 200 defined.

- [ ] **Step 6: Prove the `TAX` width test discriminates, by mutation**

Change `Op.Tax`'s guard from `_s.XFlag` to `_s.M`.

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~Tax_IsSizedByTheXFlag"`
Expected: **FAIL** — `Expected: 4660, Actual: 52` (`$1234` versus `$0034`).

Revert with `git checkout -- src/SixtyFiveXX/Cpu.Exec.cs`, re-run, and record the output. This is the test that pins destination-flag sizing, and phase 7c proved that a width test with the flags not opposed passes against the wrong flag.

- [ ] **Step 7: Run everything, raise `ExpectedImplementedOpcodes` to 200, both TFMs, commit**

Expected: unit **524** (519 + 5), conformance **1710** (1684 + 26). Roughly 145 MB of new vectors.

```bash
git commit -m "feat: 65816 transfers and XBA, sized by the destination register"
```

**Gate:** conformance **1710**, unit **524**, both TFMs, 260,000 new vectors green, the mutation experiment recorded.

---

### Task 6: Flags, the index increments, and `NOP`

12 opcodes, all two-cycle implied instructions on the routing task 4 built.

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/W65C816ImpliedTests.cs` (append)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (200 → 212)

**Interfaces:**
- Consumes: the implied routing from task 4. No new `Op` members — every one of these exists already for the 8-bit cores.

- [ ] **Step 1: Write the failing tests**

```csharp
    /// <summary>
    /// A 16-bit INX wraps at sixteen bits. Flags opposed, so a width read from m would be visible.
    /// </summary>
    [Fact]
    public void Inx_SixteenBitIndex_WrapsAtSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE8;       // INX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // opposed
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.X = 0xFFFF;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.X);
        Assert.True(cpu.State.Z);
    }

    /// <summary>
    /// With an 8-bit index it wraps at eight, and X's high byte stays $00.
    /// </summary>
    [Fact]
    public void Inx_EightBitIndex_WrapsAtEightBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE8;       // INX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // opposed
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.X = 0x00FF;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.X);
        Assert.True(cpu.State.Z);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Expected: **FAIL, 2**, `UndefinedOpcodeException` for `$E8`.

- [ ] **Step 3: Widen the four index-increment arms**

The seven flag instructions and `NOP` need **no** change — they touch no width-dependent register.

```csharp
            // Register increment and decrement. Sized by x on the 65816; the flag instructions
            // and NOP below need no widening at all.
            case Op.Inx:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { X8 = (byte)(X8 + 1); SetZN(X8); }
                else { _s.X = (ushort)(_s.X + 1); SetZN16(_s.X); }
                break;

            case Op.Dex:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { X8 = (byte)(X8 - 1); SetZN(X8); }
                else { _s.X = (ushort)(_s.X - 1); SetZN16(_s.X); }
                break;

            case Op.Iny:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { Y8 = (byte)(Y8 + 1); SetZN(Y8); }
                else { _s.Y = (ushort)(_s.Y + 1); SetZN16(_s.Y); }
                break;

            case Op.Dey:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.XFlag) { Y8 = (byte)(Y8 - 1); SetZN(Y8); }
                else { _s.Y = (ushort)(_s.Y - 1); SetZN16(_s.Y); }
                break;
```

- [ ] **Step 4: Add the 12 table entries**

```csharp
        // Flag instructions, the index increments and NOP. All implied, all two cycles.
        Set(0x18, "CLC", AddrMode.Implied, Op.Clc, Access.None);
        Set(0x38, "SEC", AddrMode.Implied, Op.Sec, Access.None);
        Set(0x58, "CLI", AddrMode.Implied, Op.Cli, Access.None);
        Set(0x78, "SEI", AddrMode.Implied, Op.Sei, Access.None);
        Set(0xB8, "CLV", AddrMode.Implied, Op.Clv, Access.None);
        Set(0xD8, "CLD", AddrMode.Implied, Op.Cld, Access.None);
        Set(0xF8, "SED", AddrMode.Implied, Op.Sed, Access.None);

        Set(0xE8, "INX", AddrMode.Implied, Op.Inx, Access.None);
        Set(0xC8, "INY", AddrMode.Implied, Op.Iny, Access.None);
        Set(0xCA, "DEX", AddrMode.Implied, Op.Dex, Access.None);
        Set(0x88, "DEY", AddrMode.Implied, Op.Dey, Access.None);

        Set(0xEA, "NOP", AddrMode.Implied, Op.Nop, Access.None);
```

Update the class `<remarks>` to 212 defined, 44 undefined, and say the remaining 44 are phase 7d's.

- [ ] **Step 5: Run everything, raise `ExpectedImplementedOpcodes` to 212, both TFMs, commit**

Expected: unit **526** (524 + 2), conformance **1734** (1710 + 24).

```bash
git commit -m "feat: 65816 flag instructions, index increments and NOP"
```

**Gate:** conformance **1734**, unit **526**, both TFMs, 240,000 new vectors green.

---

### Task 7: Whole-branch review and fix wave

**Files:** whatever the review finds, plus `README.md` and the spec's Phase 7c′ Gate section.

- [ ] **Step 1: Produce the branch diff**

```bash
git diff main...HEAD > .superpowers/sdd/p7cprime-review.diff
```

- [ ] **Step 2: Review against this checklist**

Each item is a failure this project has actually had:

- **`Sequences.RmwMiddle`'s `Unimplemented816` is now dead for the 65816** — the 816 never reads it, since `Emit816` routes away before `Sequences` is consulted. Decide whether to leave it (it still guards the `IrqEntry` section 7d will need) or retire it, and record the reasoning either way.
- **Every new micro-op is classified**, and classified *correctly* — `BusPinsTests` proves presence, not correctness. The eight RMW micro-ops should carry `MLB`; check against research §13.1's table rows.
- **`IsWriteCycle` is right for all eight.** RDY must never halt `RmwModifyWrite816`, `RmwWriteHigh816`, `RmwWriteHigh816Carry` or `RmwWrite816`.
- **The bank-carry exclusion set still covers the RMW path.** `dp` and `dp,X` confined, `abs` and `abs,X` carrying.
- **No opcode in tasks 4–6 declares a `Width`**, and `W65C816WidthTests` still asserts set equality.
- **Every new test that discriminates a width sets both flags to opposed values.**
- **Cycle counts derive to research §13.3's formulas** for every one of the 59.
- **No unguarded width test in variant-shared code.** `grep -n "_s\.M\|_s\.XFlag\|_wide" src/SixtyFiveXX/Cpu.Exec.cs` — every hit must sit behind `TVariant.Variant != CpuVariant.W65C816 ||` except arms for operations that appear in no 8-bit table (`Op.Txy`, `Op.Tyx`, `Op.Tcd`, `Op.Tdc`, `Op.Tcs`, `Op.Tsc`, `Op.Xba`).
- **No count in a doc comment that will drift.** Three sites were made count-free during phase 7c for exactly this reason; do not reintroduce one.
- **`PublicSurfaceTests` untouched**, and no vector file or cache directory staged.

- [ ] **Step 3: Fix Critical and Important findings, each as its own commit**

Minor findings are either fixed or recorded in the ledger with the reason for not fixing. Do not silently drop one.

- [ ] **Step 4: Update the README**

The 65816 section gains the new opcode groups. The support-matrix row was deliberately made count-free in phase 7c — leave it that way.

- [ ] **Step 5: Add the Verified paragraph to the spec**

Under §"Phase 7c′" Gate, in the shape 7a, 7b and 7c already use: the measured counts, both TFMs, and any rule with no vector coverage that is pinned only by a unit test.

- [ ] **Step 6: Run the full gate on an idle machine**

```bash
uptime
dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"
dotnet test tests/SixtyFiveXX.Conformance
dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category=Performance"
```

Expected: **526**, **1734**, both TFMs, and a throughput figure above the 50 MHz floor. Pass an explicit 600000 ms timeout on the conformance call. If the throughput gate fails, check `uptime` before believing it.

- [ ] **Step 7: Record the phase in the ledger**

Append a phase-7c′ section to `.superpowers/sdd/progress.md`: per-task commits, what each gate measured, every defect the vectors found that review did not, every defect review found that the vectors could not, and the carry-forward list for 7d.

**Gate:** zero Critical findings. Unit **526**, conformance **1734**, both TFMs, build zero warnings, working tree clean.

---

## Carry-forward to phase 7d

- The four items listed under "Carry-forward from phase 7c" above are all still live: `PullP`'s `~Flag.B` mask, the micro-ops computing a bare 16-bit `PC` or `0x0100 + S8`, and the 65816 IRQ mutating `S` before it throws. 7d is where all three must be resolved, because 7d is the phase that implements the stack and interrupts.
- 7d is the last phase and its gate is **all 512 vector files** — the full 5,120,000, not just its own 44 opcodes'.
- `Harte816Tests.ExpectedImplementedOpcodes` reaches 256, at which point the derived-versus-declared check becomes a check that nothing is missing at all.
- Research §12's four gaps remain open. 7d's RMW-free decimal paths do not touch them, but `BRK`/`COP` in decimal mode may.
