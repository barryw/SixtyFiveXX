# Phase 7b — the 65816 foundations

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `W65C816` core that executes `LDA` and `STA` in all fifteen 65816 addressing modes, plus `XCE`, `REP` and `SEP`, in both emulation and native mode, certified per-cycle against 640,000 SingleStepTests vectors including the full pin string.

**Architecture:** The 65816 gets its own opcode table and its own micro-op emitter on the **same** engine, the same `MicroOp[]`, and the same tick loop — the arrangement §5.4 of the architecture spec committed to. Phase 7a already widened `CpuState` and added `IBus.Internal`. What is new here is 24-bit effective-address formation, the two conditional cycles (16-bit access, direct-page penalty), the indexing cycle's three-way condition, per-micro-op pin classification, and a conformance harness for a vector set with a different repository, file naming and JSON shape.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7b".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md`. **§9 is mandatory reading and is the cycle-by-cycle specification this entire phase is built against** — it transcribes WDC Table 5-7 for every mode here, giving each cycle an address, a VDA/VPA pair, and the note that gates it. §5 gives Clark's exact cycle-count formula per opcode. §3 records two places the most-cited 65816 book is wrong.

**Scope:** `LDA` (15 opcodes), `STA` (14 opcodes), `XCE`, `REP`, `SEP` — 32 opcodes. Every other opcode stays `Op.Undefined` and throws. That is deliberate: `LDA`/`STA` between them exercise **every** 65816 addressing mode, so the whole addressing engine is forced into existence and certified before ~220 further opcodes are built on it.

## Global Constraints

- **The five 8-bit cores must not change.** Their suites are the regression gate for every task: **453 unit** and **1,309 conformance**, both `net8.0` and `net10.0`, zero failures. Any drift is a defect, not a trade.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on with documentation generation: **every public member needs an XML doc comment**.
- Both target frameworks must pass.
- `PublicSurfaceTests` asserts an exact set of public types. This phase adds exactly one — `SixtyFiveXX.Variants.W65C816Variant` — so that list gains exactly one entry. Adding more means something leaked.
- **The 65816 vector set is a different repository**: `SingleStepTests/65816`, files `v1/{opcode:x2}.e.json` and `v1/{opcode:x2}.n.json`. ~2.9 GB for the full set; this phase touches 32 opcodes, so ~180 MB. `HarteCache` cannot express this — it has no `.e`/`.n` concept — so the 65816 harness is a sibling, not a modification.
- Conventional Commits. Branch `phase7b-65816-foundations`, already created. **Do not push `main` without `[skip ci]`** — a non-skipped push to `main` cuts a public nuget.org release.

## Established facts — verified, do not re-derive

- **Implied instructions do not read on cycle 2.** Research §9, Table 5-7 row 19a: `XCE`'s second cycle is `VDA=0 VPA=0` at `PBR,PC+1` — an internal cycle. `MicroOp.ImpliedExec` performs a real read and is therefore **wrong for the 65816**. This is the first opcode implemented and the first place a reused 8-bit micro-op would fail.
- **Internal cycles drive `PBR,PC+1`** in every mode except `(sr,S),Y`'s second one, which drives `0,S+SO+1`. Research §9.
- **The indexing cycle is not the 6502's page-cross rule.** It is taken on a page cross, **or on a write, or when `x = 0`** — datasheet Note 4, corroborated by Clark's `6-m-x+x*p` for `$BD`. Research §3.2. `[dp],Y` and `long,X` have **no** indexing cycle at all.
- **The direct-page penalty is `DL != $00`**, not `D != 0`. Research §7.
- **Direct-page and stack-relative accesses are in bank 0**; `abs`/`abs,X`/`abs,Y` and `(dp)`/`(dp),Y`/`(dp,X)` data go through `DBR`; `[dp]`, `[dp],Y`, `long` and `long,X` take the bank from the pointer or operand itself. Research §9.
- **Emulation mode forces `m=1`, `x=1`, `XH=YH=$00`, `SH=$01`**, and only `XCE` changes `e`. Research §7.
- **`Flag.X` shares bit 4 with `Flag.B`, and `Flag.M` shares bit 5 with `Flag.U`.** Established and certified in phase 7a.
- **`Cpu` already has `A8`/`X8`/`Y8`/`S8`** — private byte shims for the 8-bit cores. They are named with the `8` suffix precisely so 65816 code cannot assign a 16-bit value into them by accident. **Do not add 16-bit shims named `A`/`X`/`Y`/`S`**; use `_s.A` etc. directly, or add explicitly-named 16-bit helpers.

## Carry-forward hazards from phase 7a — each is a real trap

These came out of phase 7a's final review. Every one of them is live in this phase.

1. **`MicroOp.PullP` masks `~Flag.B`, which is now also `~Flag.X`.** `PLP` and `RTI` would clear the index-width flag on a native-mode 65816. Not in this phase's opcode slice, but do not reuse that micro-op for the 816 without gating it.
2. **`CpuState.E` defaults to `false`, meaning *native*. Hardware resets into *emulation*.** `CpuStateTests.NewRegisters_DefaultToZero` pins the default and is **not** a licence to assume native. The 65816's `Reset()` must set `E = true` explicitly, along with `m=1`, `x=1`, `XH=YH=$00`, `SH=$01`.
3. **The `S8` shim setter zeroes the high byte of S.** Emulation mode requires `SH = $01`. Never route an 816 stack operation through `S8`.
4. **`state.M` reads `true` and `state.XFlag` reads `false` on every 8-bit core**, because `Flag.M` shares the always-set U bit and `Flag.X` shares the always-clear B bit. Derive width from `E` first: in emulation mode both widths are 8-bit regardless of what the raw bits say.
5. **`CpuState.ToString()` omits the 65816 tail when `E` is false and the banks are zero**, so a native-mode 816 can print identically to a 6502. Cosmetic, but misleading in a debugger.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SixtyFiveXX/Variants/W65C816Variant.cs` | Create: the variant struct. The one new public type. |
| `src/SixtyFiveXX/Opcodes65C816.cs` | Create: the 65816 opcode descriptor table |
| `src/SixtyFiveXX/AddrMode.cs` | Modify: the eight 65816-only addressing modes |
| `src/SixtyFiveXX/Op.cs` | Modify: `Xce`, `Rep`, `Sep` |
| `src/SixtyFiveXX/MicroOp.cs` | Modify: the 65816 micro-ops, and their pin classification |
| `src/SixtyFiveXX/MicroOpTable.cs` | Modify: wire the variant; add `Emit816` as a separate emission path |
| `src/SixtyFiveXX/Cpu.cs` | Modify: pin readback; route `Internal` cycles; 65816 reset; the 65816 micro-op implementations. Planned as a separate `Cpu.816.cs` kept out of `Cpu.cs`; built into `Cpu.cs` itself instead — see `Cpu.Execute`'s `MicroOp` switch, which every variant already shares |
| `tests/SixtyFiveXX.Conformance/Harte816Cache.cs` | Create: fetch/cache from the 65816 repository, `.e`/`.n` aware |
| `tests/SixtyFiveXX.Conformance/Harte816Case.cs` | Create: the different JSON shape |
| `tests/SixtyFiveXX.Conformance/Harte816Bus.cs` | Create: records reads, writes **and internal cycles** |
| `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` | Create: the gate |
| `tests/SixtyFiveXX.Tests/W65C816StateTests.cs` | Create: mode transitions and width invariants |

## Task sequence

Each task ends green. The conformance gate arrives in Task 3 and every task after it adds opcodes to a suite that already runs.

| # | Deliverable | Gate |
| --- | --- | --- |
| 1 | `W65C816Variant`, opcode table skeleton, `Emit816`, 65816 reset semantics | Unit: variant resolves, reset enters emulation mode with the right invariants, unimplemented opcodes throw |
| 2 | `BusPins` classification and internal readback | Unit: every micro-op has a classification; the 8-bit cores' suites unchanged |
| 3 | The 65816 conformance harness | It loads, replays and reports a vector; `XCE`'s 20,000 vectors green |
| 4 | `REP`/`SEP` and the width invariants | Their 40,000 vectors green |
| 5 | Direct-page family: `dp`, `dp,X`, `(dp)`, `(dp),Y`, `(dp,X)`, `[dp]`, `[dp],Y` | `LDA`/`STA` for those modes |
| 6 | Absolute, long and stack-relative family: `abs`, `abs,X`, `abs,Y`, `long`, `long,X`, `sr,S`, `(sr,S),Y`, immediate | The remaining `LDA`/`STA` vectors — all 32 opcodes green |
| 7 | README and spec status | Docs accurate; full suite green |

---

### Task 1: The variant, the table, and 65816 reset

**Files:**
- Create: `src/SixtyFiveXX/Variants/W65C816Variant.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `src/SixtyFiveXX/AddrMode.cs`, `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816StateTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/PublicSurfaceTests.cs` (exactly one entry)

**Interfaces produced** — later tasks depend on these names:

```csharp
public readonly struct W65C816Variant : ICpuVariant
{
    public static CpuVariant Variant => CpuVariant.W65C816;
}
```

New `AddrMode` members: `DirectPage`, `DirectPageX`, `DirectPageIndirect`, `DirectPageIndirectY`, `DirectPageIndexedIndirectX`, `DirectPageIndirectLong`, `DirectPageIndirectLongY`, `AbsoluteLong`, `AbsoluteLongX`, `StackRelative`, `StackRelativeIndirectY`.

Existing `Absolute`, `AbsoluteX`, `AbsoluteY` and `Immediate` are reused — the 65816's versions differ in *bank* and *width*, which `Emit816` supplies, not in operand shape.

New `Op` members: `Xce`, `Rep`, `Sep`.

- [ ] **Step 1: Write the reset test first.** This is a genuine red-green: nothing satisfies it yet.

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

public class W65C816StateTests
{
    /// <summary>
    /// Hardware resets the 65816 into EMULATION mode, not native. CpuState.E defaults to
    /// false, which is native, so the reset sequence must set it explicitly — and with it
    /// the invariants emulation mode forces: m and x set, XH and YH cleared, SH = $01.
    /// </summary>
    [Fact]
    public void Reset_EntersEmulationModeWithItsInvariants()
    {
        var ram = new byte[0x10000];
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0xC0;

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.X = 0x1234;
        cpu.State.Y = 0x5678;
        cpu.State.S = 0x0000;

        cpu.Reset();
        cpu.Step();

        Assert.True(cpu.State.E);
        Assert.True(cpu.State.M);
        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x0034, cpu.State.X);       // XH forced to $00
        Assert.Equal(0x0078, cpu.State.Y);
        Assert.Equal(0x01, cpu.State.S >> 8);    // SH forced to $01
        Assert.Equal(0xC000, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);       // reset clears the program bank
        Assert.Equal(0x00, cpu.State.DBR);
        Assert.Equal(0x0000, cpu.State.DP);
    }

    [Fact]
    public void UnimplementedOpcode_Throws()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0xEA;                      // NOP — not in phase 7b's slice

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;

        Assert.Throws<UndefinedOpcodeException>(() => cpu.Step());
    }
}
```

- [ ] **Step 2: Run it. Expect a compile failure** — `W65C816Variant` does not exist. That is the red.

- [ ] **Step 3: Create the variant struct**, following the shape of `src/SixtyFiveXX/Variants/Mos6502Variant.cs` exactly. Read that file first and match its doc-comment style.

- [ ] **Step 4: Add the `AddrMode` and `Op` members** listed under Interfaces above, each with an XML doc comment naming what it addresses and which bank it uses. Take the bank rules from research §9 — do not infer them.

- [ ] **Step 5: Create `Opcodes65C816.cs`** with a 256-entry table. All entries `Op.Undefined` except the 32 in this phase's slice. Follow the structure of `Opcodes65C02.cs` — read it first. The 32:

```
LDA: A9 imm, A5 dp, B5 dp,X, AD abs, BD abs,X, B9 abs,Y, A1 (dp,X), B1 (dp),Y,
     B2 (dp), A7 [dp], B7 [dp],Y, AF long, BF long,X, A3 sr,S, B3 (sr,S),Y
STA: 85 dp, 95 dp,X, 8D abs, 9D abs,X, 99 abs,Y, 81 (dp,X), 91 (dp),Y,
     92 (dp), 87 [dp], 97 [dp],Y, 8F long, 9F long,X, 83 sr,S, 93 (sr,S),Y
     (no immediate form)
XCE FB, REP C2, SEP E2
```

- [ ] **Step 6: Wire the variant into `MicroOpTable`.** Both `OpcodeTableFor` and the sequence selection need a `W65C816` arm. **The `Sequences` record does not stretch to the 65816** — see the spec. Give it a separate `Emit816` path, called from `Emit` before the NMOS/CMOS logic. In this task `Emit816` may emit nothing for most modes; Tasks 4-6 fill it in.

- [ ] **Step 7: Implement 65816 reset.** `Cpu.Reset()` currently sets `I`, clears the NMI latch, and enters the reset sequence. For the 65816 it must additionally set `E`, `M`, `XFlag`, force `XH`/`YH` to `$00`, force `SH` to `$01`, and clear `DBR`, `PBR` and `DP`. Guard it with `if (TVariant.Variant == CpuVariant.W65C816)` so it folds away for every other core — the same technique `ReadBus` uses for the 6510's port.

- [ ] **Step 8: Add `"SixtyFiveXX.Variants.W65C816Variant"` to `ExpectedPublicTypes`** in `tests/SixtyFiveXX.Conformance/PublicSurfaceTests.cs`. Exactly one entry. This is the deliberate edit that test exists to force.

- [ ] **Step 9: Run the new tests.** Expect PASS, 2 tests, both TFMs.

- [ ] **Step 10: Run the regression gate.** `dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"` → **455** (453 + 2). Then `dotnet test tests/SixtyFiveXX.Conformance` → **1309**, unchanged. A change here means the variant wiring leaked into another core.

- [ ] **Step 11: Commit.**

```bash
git add -A
git commit -m "feat: add the 65816 variant and its reset semantics"
```

---

### Task 2: Pin classification

**Files:** Modify `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/Cpu.cs`

**Interfaces produced:**

```csharp
[Flags]
internal enum BusPins : byte { None = 0, Vda = 1, Vpa = 2, Vpb = 4, Mlb = 8 }
```

plus `internal BusPins LastPins` and `internal int LastAddress` on `Cpu`, and `MicroOps.PinsFor(MicroOp)`.

**These flags mean ASSERTED.** `VPB` and `MLB` are active-low pins, so Table 5-7 prints `1` when they are *inactive*. The conformance vectors' pin string is the other way round: `v` and `l` mean active. Match the vectors — invert those two columns when reading the datasheet, and do not invert `VDA`/`VPA`.

- [ ] **Step 1: Write the test that every micro-op is classified.** A table with a silent default is the failure mode here — `MicroOpTable.SequencesFor` once defaulted an unmapped variant to NMOS, and this avoids the same shape.

```csharp
[Fact]
public void EveryMicroOpHasAPinClassification()
{
    foreach (var op in Enum.GetValues<MicroOp>())
    {
        if (op == MicroOp.End) continue;
        var pins = MicroOps.PinsFor(op);
        Assert.True(pins != BusPins.None || MicroOps.IsInternalCycle(op),
            $"{op} has no pin classification and is not an internal cycle.");
    }
}
```

- [ ] **Step 2: Run it. Expect a compile failure** — none of these members exist.

- [ ] **Step 3: Add `BusPins`, `MicroOps.PinsFor` and `MicroOps.IsInternalCycle`,** built the way `MicroOps.IsWriteCycle` already is — a static array built once, not a switch on the hot path. Read `MicroOp.cs`'s existing `BuildWriteTable` and match it.

  Classification rules, from research §9: an opcode fetch is `Vda | Vpa`; a read at PC (operand fetch) is `Vpa`; a read or write at an effective address, pointer or stack is `Vda`; an internal cycle is `None`. `Vpb` is asserted only on vector pulls, `Mlb` only on read-modify-write cycles — neither occurs in this phase's slice, but classify them correctly now rather than leaving a wrong value for phase 7d to trip over.

- [ ] **Step 4: Record the pins and address in `Cpu.Tick`.** Set `LastPins` and `LastAddress` on every cycle, including RDY-halted cycles. Keep both `internal` — the conformance project has `InternalsVisibleTo`, so this costs no public API.

- [ ] **Step 5: Run the full regression gate.** Unit → **456**; conformance → **1309** unchanged. This task touches `Tick`, which every core runs, so the conformance run is mandatory here, not optional.

- [ ] **Step 6: Commit.**

---

### Task 3: The conformance harness, and the first opcode

**Files:** Create `Harte816Cache.cs`, `Harte816Case.cs`, `Harte816Bus.cs`, `Harte816Tests.cs` in `tests/SixtyFiveXX.Conformance/`. Modify `src/SixtyFiveXX/Cpu.cs` (the 65816 micro-op implementations landed there, not in a separate `Cpu.816.cs` — see the File Structure table above) and `MicroOpTable.cs`.

Read `HarteCache.cs`, `HarteCase.cs`, `HarteBus.cs` and `HarteTests.cs` first and follow their structure — the download/cache/offline-override behaviour should be recognisably the same, and `SIXTYFIVEXX_HARTE_DIR` must keep working.

**Differences that make this a sibling rather than an edit**, all from research §2.3:

- Base URL `https://raw.githubusercontent.com/SingleStepTests/65816/main`, path `v1/{opcode:x2}.{e|n}.json`.
- State carries `dbr`, `pbr`, `d`, `e`; `a`/`x`/`y`/`s`/`d` are 16-bit; RAM addresses are 24-bit.
- Each cycle is `[address, value, pinstring]` where the value is **`null` on internal cycles** and the pin string is eight characters, `d p v r e m x l`, each either its letter or `-`.

- [ ] **Step 1: Write the harness**, deserialising the new shape. `value` must be a nullable type — a `byte` will fail on every internal cycle.

- [ ] **Step 2: `Harte816Bus` records three kinds of cycle**, not two: read, write, and internal. Implement `IBus.Internal` to record without touching memory. Assert per cycle: address, value, direction, whether it accessed memory at all, and the full eight-character pin string built from `LastPins` plus `E`/`M`/`XFlag` sampled from `State`.

- [ ] **Step 3: Implement `XCE` and the implied-mode 65816 micro-op.** Per research §9 row 19a: two cycles, the second **internal** at `PBR,PC+1`. Do **not** reuse `MicroOp.ImpliedExec`, which reads. `XCE` swaps `c` and `e`, and when the result is `e = 1` it must apply the emulation invariants (`m=1`, `x=1`, `XH=YH=$00`, `SH=$01`); when `e = 0` it applies none of them.

- [ ] **Step 4: Run `XCE`'s vectors** — `$FB`, both `.e` and `.n`, 20,000 cases. Expect green. This is the first real gate and it certifies the harness as much as the opcode.

- [ ] **Step 5: Run the regression gate.** Unit and 1309 conformance unchanged.

- [ ] **Step 6: Commit.**

---

### Task 4: REP and SEP

Three cycles always. Note 1 is explicit about the third: **`VPA` low, address bus `PC+1`**. Research §9.

`REP` clears the P bits set in the operand; `SEP` sets them. **When `e = 1`, `m` and `x` stay 1 regardless of the operand** — research §7. When `x` becomes 1, `XH` and `YH` are forced to `$00` immediately.

- [ ] **Step 1: Write unit tests for the width transitions**, including the emulation-mode clamp and the `XH`/`YH` forcing. Then implement, then run `$C2` and `$E2`'s 40,000 vectors. Then the regression gate. Then commit.

---

### Task 5: The direct-page family

Seven modes: `dp`, `dp,X`, `(dp)`, `(dp),Y`, `(dp,X)`, `[dp]`, `[dp],Y`. Cycle sequences are in research §9, one block per mode — implement directly against those rows.

The shared machinery this task establishes, which Task 6 reuses:

- **The direct-page penalty**, `DL != $00`: an internal cycle at `PBR,PC+1`, emitted as a slot the operand fetch skips by advancing `_mpc` when `DL == $00`. This is the idiom `ReadPageCrossCmosArith` already uses; do not invent a new mechanism.
- **The 16-bit access**: a second micro-op, skipped by the low-byte micro-op ending the instruction when the width flag selects 8 bits.
- **Bank-0 confinement** for the direct page and its pointers, with the emulation-mode page-wrap when `E == 1 && DL == $00`, keeping `DH` — research §7. Not `D == 0`.
- **The indexing cycle** for `(dp),Y`: page cross, **or write, or `x = 0`**. `[dp],Y` has none.

- [ ] **Step 1: Implement mode by mode, running each mode's `LDA` and `STA` vectors as you go** rather than all seven then debugging. Commit per mode or per small group. Run the 8-bit regression gate before the final commit.

---

### Task 6: The absolute, long and stack-relative family

`abs`, `abs,X`, `abs,Y`, `long`, `long,X`, `sr,S`, `(sr,S),Y`, and immediate. Research §9 again.

Specifics that differ from anything in Task 5:

- `abs`/`abs,X`/`abs,Y` take their bank from **`DBR`** and **carry into the next bank** — research §7.
- `long`/`long,X` are 24-bit calculations and have **no indexing cycle**.
- `sr,S` and `(sr,S),Y` are bank 0, have **no direct-page penalty**, and never wrap at a bank boundary even in emulation mode — they are "new" modes. `(sr,S),Y` has a second internal cycle at `0,S+SO+1`.
- Immediate is **two or three bytes** depending on the width flag — Note 1's "add 1 byte for immediate only". `LDA #` is the only immediate opcode in this slice; `STA` has none.

- [ ] **Step 1: Implement, running each mode's vectors as you go.** At the end of this task **all 32 opcodes must be green across both `.e` and `.n` — 640,000 vectors.** Then the 8-bit regression gate. Then commit.

---

### Task 7: Documentation

- [ ] **Step 1: Update the README status table.** The 65816 row becomes something accurate about what is executable — 15 addressing modes via `LDA`/`STA`, plus `XCE`/`REP`/`SEP` — and explicitly not a complete core.
- [ ] **Step 2: Note the new vector set in the conformance section**, including that it is a different repository and adds ~2.9 GB for the full set.
- [ ] **Step 3: Mark 7b complete in the spec's phase table.**
- [ ] **Step 4: Full suite, both TFMs. Commit.**

---

## Done when

- 32 opcodes × 20,000 vectors = **640,000** green, both `.e` and `.n`, with the full pin string asserted.
- The 8-bit cores are untouched: **453+ unit** and **1,309 conformance** still green on both TFMs.
- `PublicSurfaceTests.ExpectedPublicTypes` has gained exactly one entry.
- Every opcode outside the slice still throws `UndefinedOpcodeException`.
- No cycle count in the implementation was arrived at by running vectors until they passed. Each traces to a row in research §9.
