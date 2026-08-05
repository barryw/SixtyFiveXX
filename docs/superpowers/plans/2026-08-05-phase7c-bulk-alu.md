# Phase 7c — the bulk ALU, and the operand-width mechanism

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 121 further 65816 opcodes — the six full-mode ALU operations, `BIT`, and the remaining loads, stores and compares — certified per-cycle against 2,420,000 SingleStepTests vectors in both emulation and native mode, on top of a width mechanism that lets an opcode take its operand width from `x` instead of `m`.

**Architecture:** Phase 7b built and certified the whole 65816 addressing engine against `LDA`/`STA`, which between them use every one of the fifteen modes. This phase adds operations, not addressing: 120 of the 121 opcodes reuse a mode 7b already proved, and the one exception (`dp,Y`) is a two-line variation on `dp,X`. The single structural change is that the width test stops being the hard-coded `_s.M` of 7b and becomes a per-opcode `Width` resolved once per instruction into a `_wide` field.

**Tech Stack:** C# 13, .NET 8 and .NET 10 (both must pass), xUnit, no NuGet dependencies in `src/`.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7c".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md`. **§9 is the cycle-by-cycle specification the addressing engine was built against and is mandatory reading.** §5 gives Clark's cycle formulas for 7b's slice; task 1 of this plan extends both with a new §10. §3 records two places the most-cited 65816 book is wrong.

**Scope:** `ORA`, `AND`, `EOR`, `ADC`, `CMP`, `SBC` (15 modes each), `BIT` (5), `LDX` (5), `LDY` (5), `STX` (3), `STY` (3), `STZ` (4), `CPX` (3), `CPY` (3) — 121 opcodes, taking the 65816 from 32 defined opcodes to 153. Explicitly **not** in scope: control flow, stack instructions, read-modify-writes, shifts, transfers, accumulator and implied forms. Those are phases 7c′ and 7d.

## Global Constraints

- **The five 8-bit cores must not change.** Their suites are the regression gate for every task: **1,309** of the conformance tests are theirs, and they must stay at 1,309 passing on both TFMs. Any drift is a defect, not a trade.
- **Baselines measured on this branch's merge-base, both verified before this plan was written:** unit **488** with `--filter "Category!=Performance"` (489 unfiltered — the extra one is the throughput gate), conformance **1374**. Every task below states the exact expected numbers after it.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on with documentation generation, so **every public member needs an XML doc comment**.
- Both target frameworks must pass. Run `-f net10.0` while iterating; run both before declaring a task done.
- **This phase adds no public API.** `OpcodeInfo`, `Width`, `AddrMode`, `Op` and `MicroOp` are all `internal`. `PublicSurfaceTests.ExpectedPublicTypes` must be **unchanged**. If it needs an edit, something leaked.
- **Vectors:** `SingleStepTests/65816`, files `v1/{opcode:x2}.e.json` and `v1/{opcode:x2}.n.json`, roughly 11 MB per opcode across the two modes. This phase pulls about **1.4 GB**. `Harte816Cache` already handles fetching, caching and the `SIXTYFIVEXX_HARTE_DIR` override; the cache directory is gitignored. **Never commit a vector file.**
- Conventional Commits. Branch `phase7c-bulk-alu`, forked from `main` at the phase-7b merge. **Do not push `main` without `[skip ci]`** — a non-skipped push to `main` cuts a public nuget.org release.
- The throughput gate (`Category=Performance`) is **contention-sensitive by design** and is excluded from CI. Do not chase a failure on a loaded machine; check `uptime` first. If it is used to compare anything, the comparison must interleave run-for-run against a rebuilt baseline, never against a figure recorded earlier — this is phase 7a's recorded lesson and it has already produced one confident wrong number in this project.

## Established facts — verified, do not re-derive

- **The addressing engine is done and certified.** All fifteen modes pass 640,000 vectors. Adding an opcode means adding a table entry, an `Exec` arm, and nothing else — unless it needs `dp,Y`, which only `LDX` and `STX` do.
- **The indexing-cycle skip is already selected at table-build time from `info.Access`**, not from `info.Operation`. This was a task-5 code-review fix in 7b, made specifically so that this phase's ALU operations on `(dp),Y`, `abs,X` and `abs,Y` would be classified correctly. There is no `_op ==` comparison anywhere in the addressing path; do not add one.
- **`Access.Read` selects the skip-capable indexing micro-op, `Access.Write` the unconditional one.** Every ALU operation in this phase is `Access.Read`; `STX`/`STY`/`STZ` are `Access.Write`.
- **`_data` holds the 8-bit operand; `_data16` holds the 16-bit one.** `ReadExec816` fills `_data` and, when the width is 8, calls `Exec()` and ends the instruction. `ReadExecHigh816` combines both bytes into `_data16` and calls `Exec()`. So an `Exec` arm reads `_data` on the 8-bit path and `_data16` on the 16-bit path — exactly as `Op.Lda` already does.
- **`Flag.M` (0x20) is the same bit as `Flag.U`, and `Flag.X` (0x10) is the same bit as `Flag.B`.** This is why every width test in variant-shared code must be guarded by `TVariant.Variant != CpuVariant.W65C816 ||` first. Clearing bit 5 of `P` on a 6502 through the public `State` property must not send it down a 16-bit path. See `UnusedFlagBitRegressionTests`; this was a real code-review finding in 7b, and conformance could not see it (0 of 10,000 `6502/a5` vectors have bit 5 clear).
- **`A8`'s setter currently assigns the whole 16-bit field**, so writing through it zeroes A's high byte. On the 65816 that high byte is the "hidden B accumulator" and must survive an 8-bit operation. `Op.Lda` works around this today with a hand-rolled `(_s.A & 0xFF00) | _data`. Task 2 moves the fix into the setter, where every caller gets it.
- **`X8`/`Y8`'s clobbering setters are correct and must NOT be given the same treatment.** There is no hidden high byte for the index registers: whenever `x` is set, `XH` and `YH` are `$00` — a continuously held invariant this core already enforces in `FetchOpcode`, `Op.Xce`, `Op.Rep` and `Op.Sep`, validated by 40,000 `REP`/`SEP` vectors. A symmetric "fix" to `X8` would be wrong.
- **Emulation mode forces `m=1`, `x=1`, `XH=YH=$00`, `SH=$01`, continuously**, in `FetchOpcode` under a `TVariant.Variant == CpuVariant.W65C816 && _s.E` guard. Consequence for this phase: **no `.e` vector can ever exercise a 16-bit path.** Every 16-bit behaviour here is certified by `.n` vectors alone.
- **`Harte816Tests` derives its theory data from the resolved micro-op table**, so a new opcode is picked up automatically. The only manual edit per task is `ExpectedImplementedOpcodes`.
- **`BusPinsTests.EveryMicroOpHasAPinClassification` fails on any new `MicroOp` member that is neither given pins nor declared an internal cycle.** Task 7 adds one member and must classify it.
- **The disassembler does not decode the 65816's own addressing modes** — `Disassembler.Decode` throws `NotSupportedException` for them, which is phase 7e's work. Do not add arms for them here; adding one would be scope creep into a phase with its own 64tass round-trip gate.

## Carry-forward hazards from phase 7b — each is live, none is in scope

Recorded so nobody "fixes" one mid-phase and lands an unattributable change:

1. **`MicroOp.PullP` masks `~Flag.B`, which is also `~Flag.X`.** `PLP`/`RTI` would clear the index-width flag on a native 65816. No opcode in this phase reaches it.
2. **`ImpliedExec`, `FetchAddrHiX`/`Y`, the branch micro-ops, `JmpAbs`, `BrkPad`, `IntDummy` and every stack micro-op still compute a bare 16-bit `PC` and/or `0x0100 + S8`.** No opcode in this phase reaches any of them.
3. **`Sequences.RmwMiddle` is `Unimplemented816`.** This phase adds no read-modify-write, so it stays unreached.
4. **A 65816 IRQ mutates `S` and memory before `Unimplemented816` throws.** Phase 7d.
5. **The RDY halt path drives `_addr`, which is the right address for only a minority of read micro-ops.** A documented limitation with a `ponytail:` comment and an upgrade path. Unchanged in scope; it gets no wider here.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SixtyFiveXX/Width.cs` | **Create.** The three-member `Width` enum. Its own file, matching `Access.cs`, `AddrMode.cs`, `Op.cs` and `CpuVariant.cs`. |
| `src/SixtyFiveXX/OpcodeInfo.cs` | Modify. A fifth positional parameter, defaulted, so no existing call site changes. |
| `src/SixtyFiveXX/Cpu.cs` | Modify. `A8`'s setter; the `_wide` field; the assignment in `FetchOpcode`; the three width-deciding micro-ops; task 7's new `DirectPageIndexY`. |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Modify. Width-aware `Exec` arms and the 16-bit ALU helpers. |
| `src/SixtyFiveXX/Opcodes65C816.cs` | Modify. `Width` on the existing 32 entries, then 121 new ones. |
| `src/SixtyFiveXX/MicroOpTable.cs` | Modify. `Emit816` gains early returns; `EmitLdaSta816` is renamed to `EmitAddressed816` and takes every addressed opcode. |
| `src/SixtyFiveXX/MicroOp.cs` | Modify (task 7 only). `DirectPageIndexY`, and its entry in `BuildInternalCycleTable`. |
| `src/SixtyFiveXX/AddrMode.cs` | Modify (task 7 only). `DirectPageY`. |
| `tests/SixtyFiveXX.Tests/W65C816WidthTests.cs` | **Create** (task 2). The table-integrity tripwire, then the per-task discrimination tests for width behaviour. |
| `tests/SixtyFiveXX.Tests/W65C816AluTests.cs` | **Create** (task 3). 16-bit logic, arithmetic, compare and `BIT` discrimination tests. |
| `tests/SixtyFiveXX.Tests/W65C816IndexModeTests.cs` | **Create** (task 7). `dp,Y` wrapping and bank confinement. |
| `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` | Modify, once per task. `ExpectedImplementedOpcodes` and its doc comment. |
| `docs/superpowers/research/2026-08-03-65816-reference-sources.md` | Modify (task 1). New §10. |

---

### Task 1: Research §10 — the four unsettled questions

**No code.** This task exists because 7b transcribed WDC Table 5-7 into §9 *before* writing any code and got 280,000 vectors green on the first run, with no cycle count tuned against a failure. This repeats that, for the facts this phase needs and §9 does not have.

**Files:**
- Modify: `docs/superpowers/research/2026-08-03-65816-reference-sources.md` (append §10, before the closing material)

**Interfaces:**
- Produces: research §10, which tasks 5 and 7 cite by section number. Specifically §10.1 (16-bit decimal algorithm and flags), §10.2 (the `Op` member decision), §10.3 (Table 5-7's `Direct,Y` row in §9's format), §10.4 (x-width immediate cycle and byte counts), §10.5 (Clark's cycle formula for every opcode in this phase, including whether `ADC`/`SBC` pay a decimal-mode cycle).

**Sources.** Both are fetched, not remembered:
- Clark, "65C816 Opcodes" — use the GitHub mirror, because 6502.org 404s non-browser agents: `https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
- WDC W65C816S datasheet: `https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf`, Table 5-7 on pp. 36–42.

**The honesty rule for this task.** If a source is silent on a question, **record that it is silent**. Do not fill the gap from memory, do not infer it from the 6502 or 65C02, and do not write a number you cannot cite. A recorded gap is a useful research output; a guessed number that later disagrees with 20,000 vectors costs a debugging session to unpick. §3 of this same document exists because the most-cited book is wrong twice.

- [ ] **Step 1: Read the existing §9 and §5 so the new section matches their format**

Read `docs/superpowers/research/2026-08-03-65816-reference-sources.md` §5 (cycle formulas, a table) and §9 (per-cycle bus sequences, one block per mode with cycle number, VDA/VPA pair, address expression and data-bus contents). §10 must be readable alongside them without a change of notation.

- [ ] **Step 2: Settle §10.1 — 16-bit decimal `ADC` and `SBC`**

Fetch Clark and find what it says about decimal mode with `m = 0`. Record, with a verbatim quotation and a section reference for each:

- the correction algorithm for four BCD digits (nibble-wise, or `$60`/`$06`-style as the CMOS `SBC` in this codebase uses, or something else);
- which of `N`, `V`, `Z` and `C` are taken from the corrected result and which from the binary intermediate;
- whether any of them is documented as invalid or undefined in decimal mode.

Where Clark is silent, say so explicitly and in those words. This is the question most likely to end with a recorded gap, and that is an acceptable outcome — task 5 then derives the algorithm from the vectors and writes the derivation back into §10.1 as a measured result, labelled as measured rather than cited.

- [ ] **Step 3: Settle §10.2 — the `Op` member decision**

The rule is pre-committed by the spec and is not open for re-litigation mid-run:

> Reuse `Op.AdcCmos`/`Op.SbcCmos` **only** if the sources state the 65816's decimal behaviour is identical to the 65C02's. Any divergence, **or any silence**, gets its own `Op.Adc816`/`Op.Sbc816` members.

Record the verdict and the sentence it rests on. Note in the section that this codebase already carries `Op.AdcCmos`/`Op.SbcCmos` separately from `Op.Adc`/`Op.Sbc` for exactly this reason, and that phase 2a's ledger records the separation as necessary rather than tidy.

- [ ] **Step 4: Settle §10.3 — Table 5-7's `Direct,Y` row**

Transcribe it in §9's format — cycle number, VDA/VPA pair, address-bus expression, data-bus contents, and the note gating any conditional cycle. It should mirror §9's "Direct,X — row 16a" block with `Y` substituted; confirm from the table that it does rather than assuming it, and note the row number.

- [ ] **Step 5: Settle §10.4 — the x-width immediates**

Confirm from Clark §6.5 that `LDX #`, `LDY #`, `CPX #` and `CPY #` are `3-x` cycles and `3-x` bytes, mirroring `LDA #`'s `3-m`. Quote the formulas.

- [ ] **Step 6: Settle §10.5 — a cycle formula for every opcode in this phase**

Tabulate Clark's formula for each of `ORA`, `AND`, `EOR`, `ADC`, `CMP`, `SBC` (all fifteen modes), `BIT`, `LDX`, `LDY`, `STX`, `STY`, `STZ`, `CPX`, `CPY`. Use §5's table format and its symbols (`m`, `x`, `w`, `p`).

**One question this table answers by itself and which task 5's shape depends on:** does `ADC`'s formula carry a decimal-mode term? The 65C02 in this codebase spends an extra cycle in decimal mode, which is why `MicroOp.BcdExtra` and `Op.AdcCmos` exist. If the 65816's formula has no such term, `ADC` uses the ordinary read tail and task 5 needs no new micro-op. If it does have one, task 5 must add a conditionally skipped slot. State the answer in one sentence at the top of §10.5 so task 5 cannot miss it.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/research/2026-08-03-65816-reference-sources.md
git commit -m "docs: research §10, the facts phase 7c needs before any opcode is written"
```

**Gate:** No code changed — `git diff --stat main -- src tests` must be empty. Every claim in §10 carries a named source or is explicitly labelled as a recorded gap. The `Op` member verdict in §10.2 is stated outright, not left open.

---

### Task 2: `Width`, `_wide`, and the `LDA`/`STA` retrofit

The riskiest change in the phase, shipped alone against a gate that already exists. **Its deliverable is that nothing changes.**

**Files:**
- Create: `src/SixtyFiveXX/Width.cs`
- Modify: `src/SixtyFiveXX/OpcodeInfo.cs`
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (annotate the existing 32 entries)
- Modify: `src/SixtyFiveXX/Cpu.cs` (`A8`'s setter, the `_wide` field, `FetchOpcode`, three micro-ops)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (`Op.Lda`, `Op.Sta`)
- Test: `tests/SixtyFiveXX.Tests/W65C816WidthTests.cs` (create)

**Interfaces:**
- Produces: `internal enum Width : byte { None, M, X }`; `OpcodeInfo`'s fifth parameter `Width Width = Width.None`; `private bool _wide` on `Cpu<TBus, TVariant>`, true exactly when the current instruction's operand is 16 bits. Tasks 3–7 set `Width` on every entry they add and never test `_s.M` or `_s.XFlag` directly in an access path.

- [ ] **Step 1: Write the failing test**

Create `tests/SixtyFiveXX.Tests/W65C816WidthTests.cs`:

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The operand-width mechanism: which flag an opcode's width comes from, and the table
/// integrity that keeps the two in step as opcodes are added.
/// </summary>
public class W65C816WidthTests
{
    /// <summary>
    /// The tripwire for every remaining task in this phase. An opcode whose sequence contains
    /// one of the three width-deciding micro-ops MUST declare a <c>Width</c>, or it silently
    /// takes the 8-bit path for every operand regardless of <c>m</c> and <c>x</c>; an opcode
    /// that declares one but never reaches a deciding micro-op is dead data that will mislead
    /// the next reader. Asserted as set equality rather than one direction, so both mistakes
    /// fail here — in a sub-second unit run — instead of inside a 20,000-vector file.
    /// </summary>
    [Fact]
    public void WidthIsDeclaredExactlyForOpcodesThatDecideAnOperandWidth()
    {
        var table = MicroOpTable.For<W65C816Variant>();

        for (var opcode = 0; opcode < 256; opcode++)
        {
            var decides = false;
            for (var i = table.Entry[opcode]; table.Ops[i] != MicroOp.End; i++)
            {
                if (table.Ops[i] is MicroOp.ReadExec816 or MicroOp.ExecWrite816 or MicroOp.ImmExec816)
                    decides = true;
            }

            var declared = table.Info[opcode].Width != Width.None;

            Assert.True(decides == declared,
                $"${opcode:X2} {table.Info[opcode].Mnemonic}: reaches a width-deciding micro-op = " +
                $"{decides}, declares a Width = {declared}. These must agree.");
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail to build**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816WidthTests"`
Expected: **build error**, `CS0246: The type or namespace name 'Width' could not be found`.

- [ ] **Step 3: Add the `Width` enum**

Create `src/SixtyFiveXX/Width.cs`:

```csharp
namespace SixtyFiveXX;

/// <summary>
/// Which status flag decides an instruction's operand width on the 65816.
/// </summary>
/// <remarks>
/// The 65816 sizes an access from one of two independent flags: <c>m</c> for anything that
/// moves through the accumulator, <c>x</c> for anything that moves through an index register.
/// Which one applies is a fixed property of the instruction, so it is resolved once per
/// instruction in <c>Cpu.FetchOpcode</c> rather than tested by each access micro-op.
/// <para>
/// <see cref="None"/> is a real value, not a placeholder: <c>XCE</c> has no operand at all, and
/// <c>REP</c>/<c>SEP</c> take a fixed 8-bit one whose width no flag can change (datasheet Note
/// 1). Most of phase 7d's control-flow and stack instructions will be <see cref="None"/> too.
/// An opcode carrying <see cref="None"/> that nevertheless reaches a width-deciding micro-op is
/// a table bug; <c>W65C816WidthTests</c> is where that is caught.
/// </para>
/// <para>
/// The five 8-bit cores never set this — their tables predate it and take the parameter's
/// default — and never read it: <c>_wide</c> is assigned only under a compile-time variant
/// guard, so for them it is permanently <see langword="false"/>.
/// </para>
/// </remarks>
internal enum Width : byte
{
    /// <summary>No flag-dependent operand width. The default.</summary>
    None,

    /// <summary>Width comes from the <c>m</c> flag: the accumulator and memory operations.</summary>
    M,

    /// <summary>Width comes from the <c>x</c> flag: the index-register operations.</summary>
    X,
}
```

- [ ] **Step 4: Add the parameter to `OpcodeInfo`**

In `src/SixtyFiveXX/OpcodeInfo.cs`, replace the record declaration and add the parameter doc:

```csharp
/// <param name="Mnemonic">Three-letter assembler mnemonic, upper case.</param>
/// <param name="Mode">How the effective address is formed.</param>
/// <param name="Operation">What is done once the address is formed.</param>
/// <param name="Access">Whether the effective address is read, written, or both.</param>
/// <param name="Width">
/// Which flag decides the operand width on the 65816. Defaults to <see cref="SixtyFiveXX.Width.None"/>,
/// which is what every 8-bit core's table wants and why this is a defaulted parameter rather than a
/// required one — the five 8-bit opcode tables are left entirely untouched by its addition.
/// </param>
internal readonly record struct OpcodeInfo(
    string Mnemonic, AddrMode Mode, Op Operation, Access Access, Width Width = Width.None)
{
    /// <summary>An opcode this variant does not implement.</summary>
    public static readonly OpcodeInfo Undefined =
        new("???", AddrMode.Undefined, Op.Undefined, Access.None);
}
```

- [ ] **Step 5: Run the test again — it should now build and fail on the assertion**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816WidthTests"`
Expected: **FAIL**, with a message naming an `LDA` or `STA` opcode — `reaches a width-deciding micro-op = True, declares a Width = False`.

- [ ] **Step 6: Annotate the existing 32 entries**

In `src/SixtyFiveXX/Opcodes65C816.cs`, widen the local `Set` helper and pass `Width.M` for every `LDA` and `STA` form. `XCE`, `REP` and `SEP` keep the default.

```csharp
        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access,
                 Width width = Width.None) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access, width);
```

Then append `, Width.M` to each of the 29 `LDA`/`STA` lines. Leave the three mode-control lines alone — `XCE` has no operand, and `REP`/`SEP`'s operand is a fixed byte no flag resizes.

- [ ] **Step 7: Run the test — it should pass**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816WidthTests"`
Expected: **PASS**, 1 test.

- [ ] **Step 8: Make `A8`'s setter preserve the hidden B accumulator**

This is the root-cause fix for a trap every remaining task would otherwise hit separately. On the 65816 in 8-bit mode, an operation on A must leave A's high byte alone — that byte is the hidden B accumulator. `A8`'s setter assigns the whole 16-bit field, so `A8 = value` zeroes it. `Op.Lda` works around this today with a hand-rolled expression; every ALU arm in task 3 and every arithmetic helper in task 5 would need the same workaround. Fixing the setter fixes all of them at once, and folds to the existing code for the five 8-bit cores.

In `src/SixtyFiveXX/Cpu.cs`, replace the `A8` property (currently `private byte A8 { get => (byte)_s.A; set => _s.A = value; }`) with:

```csharp
    private byte A8
    {
        get => (byte)_s.A;
        set => _s.A = TVariant.Variant == CpuVariant.W65C816
            ? (ushort)((_s.A & 0xFF00) | value)
            : value;
    }
```

Add this paragraph to `A8`'s existing `<remarks>`, after the paragraph that begins "The setters assign the whole 16-bit field":

```
    /// <para>
    /// <see cref="A8"/>'s setter is the exception to that: on the 65816 it preserves A's high
    /// byte, which is the hidden B accumulator that <c>XBA</c> exchanges with. An 8-bit
    /// operation on A must not disturb it. The <c>TVariant.Variant</c> test is a compile-time
    /// constant per closed generic type, so for the five 8-bit cores this folds back to a plain
    /// <c>_s.A = value</c> and costs nothing — and it is byte-for-byte equivalent for them in
    /// any case, since no 8-bit-core opcode or test ever puts anything in A's high byte.
    /// <see cref="X8"/> and <see cref="Y8"/> deliberately do NOT get this treatment: there is no
    /// hidden high byte for the index registers, and whenever <c>x</c> is set their high bytes
    /// are $00 by a continuously held invariant this core enforces in <c>FetchOpcode</c>,
    /// <see cref="Op.Xce"/>, <see cref="Op.Rep"/> and <see cref="Op.Sep"/>.
    /// </para>
```

- [ ] **Step 9: Retire `Op.Lda`'s hand-rolled workaround**

In `src/SixtyFiveXX/Cpu.Exec.cs`, the 8-bit arm of `Op.Lda` can now go through the shim. Replace:

```csharp
            case Op.Lda:
                if (TVariant.Variant != CpuVariant.W65C816 || _s.M)
                { _s.A = (ushort)((_s.A & 0xFF00) | _data); SetZN(_data); }
                else { _s.A = _data16; SetZN16(_data16); }
                break;
```

with:

```csharp
            case Op.Lda:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 = _data; SetZN(_data); }
                else { _s.A = _data16; SetZN16(_data16); }
                break;
```

Keep the long comment block above it — it explains why the variant guard is load-bearing, which is still true — but update its final paragraph so it no longer describes an inline `& 0xFF00` that has moved into `A8`.

- [ ] **Step 10: Add `_wide` and assign it in `FetchOpcode`**

In `src/SixtyFiveXX/Cpu.cs`, beside `_data16`:

```csharp
    /// <summary>
    /// True when the instruction now executing takes a 16-bit operand. Resolved once, in
    /// <see cref="FetchOpcode"/>, from the opcode's <see cref="Width"/> and the matching status
    /// flag; the width-deciding micro-ops read it rather than testing <c>m</c> or <c>x</c>
    /// themselves.
    /// </summary>
    /// <remarks>
    /// <b>Latched at fetch, not sampled per cycle.</b> Nothing in phase 7c can change <c>m</c> or
    /// <c>x</c> part-way through an instruction, so the distinction is unobservable there — but
    /// it becomes observable in phase 7d, when <c>PLP</c> and <c>RTI</c> can rewrite <c>P</c>
    /// mid-sequence. Latching is deliberate and is what a decoder that resolves width once
    /// actually does: an instruction already committed to a 16-bit access does not become an
    /// 8-bit one halfway through. Do not "fix" this into a live read of <c>_s.M</c>.
    /// <para>
    /// Assigned only under a compile-time variant guard, so for the five 8-bit cores the
    /// assignment is never emitted and this stays <see langword="false"/> for the lifetime of the
    /// core. Every read of it in variant-shared code must still sit behind
    /// <c>TVariant.Variant != CpuVariant.W65C816 ||</c> — see <see cref="Op.Lda"/>'s arm — so the
    /// field is never loaded on an 8-bit core's hot path.
    /// </para>
    /// </remarks>
    private bool _wide;
```

In `FetchOpcode`, immediately after `_op = info.Operation;` and `_opcode = opcode;` and **before** `_mpc` is set:

```csharp
        // Resolve this instruction's operand width once, here, rather than per access cycle.
        // The guard is a compile-time constant per closed generic type, so the five 8-bit cores
        // emit nothing at all and _wide stays false for them — which matters because Flag.M and
        // Flag.X alias Flag.U and Flag.B, so reading _s.M on a 6502 reads its always-set unused
        // bit. See the remarks on _wide.
        if (TVariant.Variant == CpuVariant.W65C816)
            _wide = info.Width switch
            {
                Width.M => !_s.M,
                Width.X => !_s.XFlag,
                _ => false,
            };
```

- [ ] **Step 11: Swap the three deciding micro-ops**

In `src/SixtyFiveXX/Cpu.cs`'s `Execute` switch, replace `_s.M` with `!_wide` in exactly three places. These cases are emitted only by `Emit816`, so they need no variant guard.

```csharp
            case MicroOp.ReadExec816:
                _data = ReadBus(_addr);
                if (!_wide) { Exec(); EndInstruction(); }
                break;
```

```csharp
            case MicroOp.ExecWrite816:
                Exec();
                if (!_wide) { WriteBus(_addr, _data); EndInstruction(); }
                else WriteBus(_addr, (byte)_data16);
                break;
```

```csharp
            case MicroOp.ImmExec816:
                _data = ReadBus(PcAddress());
                _s.PC++;
                if (!_wide) { Exec(); EndInstruction(); }
                break;
```

Leave `ReadExecHigh816`, `ReadExecHigh816Carry`, `ExecWriteHigh816`, `ExecWriteHigh816Carry` and `ImmExecHigh816` alone. They carry no width test — they run only because the deciding micro-op did not end the instruction.

- [ ] **Step 12: Swap `Op.Sta`**

```csharp
            case Op.Sta:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = A8;
                else _data16 = _s.A;
                break;
```

- [ ] **Step 13: Verify no `_s.M` width test survives in an access path**

Run: `grep -n "_s\.M" src/SixtyFiveXX/*.cs`
Expected: hits only in `Op.Xce`, `Op.Rep`, `Op.Sep` (which *write* `m`) and `FetchOpcode`'s emulation-mode forcing. **No hit inside a `MicroOp` case, and none in `Op.Lda`/`Op.Sta`.** If any remains, it is a site this step missed.

- [ ] **Step 14: Run the whole unit suite**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "Category!=Performance"`
Expected: **PASS, 489** (488 + the one new test).

- [ ] **Step 15: Run the conformance suite — the real gate**

Run: `dotnet test tests/SixtyFiveXX.Conformance -f net10.0`
Expected: **PASS, 1374 — unchanged.** This is 640,000 `LDA`/`STA` vectors plus the 1,309 eight-bit-core tests, and *not one number may move*. A change here means the retrofit altered behaviour, which is the one thing this task must not do.

- [ ] **Step 16: Run both target frameworks**

Run: `dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"` then `dotnet test tests/SixtyFiveXX.Conformance`
Expected: 489 and 1374 on **both** `net8.0` and `net10.0`.

- [ ] **Step 17: Commit**

```bash
git add src/SixtyFiveXX/Width.cs src/SixtyFiveXX/OpcodeInfo.cs src/SixtyFiveXX/Opcodes65C816.cs \
        src/SixtyFiveXX/Cpu.cs src/SixtyFiveXX/Cpu.Exec.cs tests/SixtyFiveXX.Tests/W65C816WidthTests.cs
git commit -m "feat: resolve 65816 operand width per opcode instead of hard-coding the m flag"
```

**Gate:** conformance **1374, unchanged**; unit **489**; both TFMs; build with zero warnings.

---

### Task 3: `AND`, `ORA`, `EOR` — 45 opcodes

The first bulk task, and the one that proves the premise: an operation reusing a certified addressing mode costs a table entry and an `Exec` arm.

**Files:**
- Modify: `src/SixtyFiveXX/MicroOpTable.cs` (`Emit816` restructure, `EmitLdaSta816` renamed)
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (45 entries)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (three arms)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (`ExpectedImplementedOpcodes` 32 → 77)
- Test: `tests/SixtyFiveXX.Tests/W65C816AluTests.cs` (create)

**Interfaces:**
- Consumes: `_wide` and `Width` from task 2.
- Produces: `MicroOpTable.EmitAddressed816(List<MicroOp> ops, OpcodeInfo info)` — the renamed general 65816 addressing emitter that tasks 4–7 route through unchanged.

- [ ] **Step 1: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816AluTests.cs`:

```csharp
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Discrimination tests for the 65816's arithmetic and logic at both operand widths. The
/// SingleStepTests vectors cover these operations exhaustively; these exist so a width or
/// flag-source mistake fails legibly in a sub-second unit run rather than as one diff among
/// 900,000 per-cycle comparisons.
/// </summary>
public class W65C816AluTests
{
    /// <summary>
    /// A's high byte is the hidden B accumulator. An 8-bit operation must leave it alone —
    /// which <c>A8</c>'s setter now guarantees for every caller (task 2). Fails against a
    /// setter that assigns the whole 16-bit field: A would read $000F, not $120F.
    /// </summary>
    [Fact]
    public void And_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x29;       // AND #
        ram[0xC001] = 0x0F;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.A = 0x12FF;     // B = $12, A = $FF

        cpu.Step();

        Assert.Equal(0x120F, cpu.State.A);
    }

    /// <summary>
    /// With a 16-bit accumulator, N comes from bit 15, not bit 7. Fails against an arm that
    /// calls <c>SetZN</c> instead of <c>SetZN16</c>: $8000's low byte is $00, so N would be
    /// clear and Z would be set.
    /// </summary>
    [Fact]
    public void Ora_SixteenBitMode_TakesNAndZFromTheFullSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x09;       // ORA #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x80;       // operand $8000

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0000;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816AluTests"`
Expected: **FAIL, 2 tests**, with `UndefinedOpcodeException` — `$29` and `$09` are not in the 65816 table yet.

- [ ] **Step 3: Restructure `Emit816` so every addressed opcode routes through one emitter**

In `src/SixtyFiveXX/MicroOpTable.cs`, replace the body of `Emit816`:

```csharp
    private static void Emit816(List<MicroOp> ops, OpcodeInfo info)
    {
        if (info.Operation == Op.Xce)
        {
            ops.Add(MicroOp.ImpliedExec816);
            return;
        }

        if (info.Operation is Op.Rep or Op.Sep)
        {
            ops.AddRange([MicroOp.RepSepOperand, MicroOp.RepSepExec]);
            return;
        }

        // Everything else on the 65816 forms an effective address and then reads or writes it.
        // Routed by mode and access rather than by an ever-growing list of operations: the
        // emitter's own `default:` throw is the tripwire for a mode with no sequence, and
        // keeping every addressed opcode on one path is what makes it a real tripwire rather
        // than one that only fires for operations somebody remembered to list here.
        EmitAddressed816(ops, info);
    }
```

The sequential `if`s it replaces had no `return`s, which was harmless while three operations were mutually exclusive and is not once every addressed opcode falls through to the last branch.

- [ ] **Step 4: Rename `EmitLdaSta816` to `EmitAddressed816`**

Rename the method, and update its `<summary>` — it is no longer `LDA`/`STA`-specific. Change the `default:` arm's message from `"{info.Mnemonic}: {info.Mode} has no LDA/STA sequence."` to:

```csharp
            default:
                throw new InvalidOperationException(
                    $"{info.Mnemonic}: {info.Mode} has no 65816 addressing sequence.");
```

Update the two `<see cref="..."/>` references to the old name in `Emit816`'s remarks. Nothing else about the method changes: the mode `switch`, the bank-carry exclusion set and the read/write access tail are all correct as they stand, because they are driven by `info.Mode` and `info.Access` and never by `info.Operation`.

- [ ] **Step 5: Add the 45 table entries**

In `src/SixtyFiveXX/Opcodes65C816.cs`, after the `STA` block:

```csharp
        // The three logical operations, in every addressing form the 65816 has. Each reuses an
        // addressing sequence phase 7b certified against LDA/STA — the operation changes, the
        // cycles do not. Width.M for all of them: they move through the accumulator.
        Set(0x09, "ORA", AddrMode.Immediate,                  Op.Ora, Access.Read, Width.M);
        Set(0x05, "ORA", AddrMode.DirectPage,                 Op.Ora, Access.Read, Width.M);
        Set(0x15, "ORA", AddrMode.DirectPageX,                Op.Ora, Access.Read, Width.M);
        Set(0x0D, "ORA", AddrMode.Absolute,                   Op.Ora, Access.Read, Width.M);
        Set(0x1D, "ORA", AddrMode.AbsoluteX,                  Op.Ora, Access.Read, Width.M);
        Set(0x19, "ORA", AddrMode.AbsoluteY,                  Op.Ora, Access.Read, Width.M);
        Set(0x01, "ORA", AddrMode.DirectPageIndexedIndirectX, Op.Ora, Access.Read, Width.M);
        Set(0x11, "ORA", AddrMode.DirectPageIndirectY,        Op.Ora, Access.Read, Width.M);
        Set(0x12, "ORA", AddrMode.DirectPageIndirect,         Op.Ora, Access.Read, Width.M);
        Set(0x07, "ORA", AddrMode.DirectPageIndirectLong,     Op.Ora, Access.Read, Width.M);
        Set(0x17, "ORA", AddrMode.DirectPageIndirectLongY,    Op.Ora, Access.Read, Width.M);
        Set(0x0F, "ORA", AddrMode.AbsoluteLong,               Op.Ora, Access.Read, Width.M);
        Set(0x1F, "ORA", AddrMode.AbsoluteLongX,              Op.Ora, Access.Read, Width.M);
        Set(0x03, "ORA", AddrMode.StackRelative,              Op.Ora, Access.Read, Width.M);
        Set(0x13, "ORA", AddrMode.StackRelativeIndirectY,     Op.Ora, Access.Read, Width.M);

        Set(0x29, "AND", AddrMode.Immediate,                  Op.And, Access.Read, Width.M);
        Set(0x25, "AND", AddrMode.DirectPage,                 Op.And, Access.Read, Width.M);
        Set(0x35, "AND", AddrMode.DirectPageX,                Op.And, Access.Read, Width.M);
        Set(0x2D, "AND", AddrMode.Absolute,                   Op.And, Access.Read, Width.M);
        Set(0x3D, "AND", AddrMode.AbsoluteX,                  Op.And, Access.Read, Width.M);
        Set(0x39, "AND", AddrMode.AbsoluteY,                  Op.And, Access.Read, Width.M);
        Set(0x21, "AND", AddrMode.DirectPageIndexedIndirectX, Op.And, Access.Read, Width.M);
        Set(0x31, "AND", AddrMode.DirectPageIndirectY,        Op.And, Access.Read, Width.M);
        Set(0x32, "AND", AddrMode.DirectPageIndirect,         Op.And, Access.Read, Width.M);
        Set(0x27, "AND", AddrMode.DirectPageIndirectLong,     Op.And, Access.Read, Width.M);
        Set(0x37, "AND", AddrMode.DirectPageIndirectLongY,    Op.And, Access.Read, Width.M);
        Set(0x2F, "AND", AddrMode.AbsoluteLong,               Op.And, Access.Read, Width.M);
        Set(0x3F, "AND", AddrMode.AbsoluteLongX,              Op.And, Access.Read, Width.M);
        Set(0x23, "AND", AddrMode.StackRelative,              Op.And, Access.Read, Width.M);
        Set(0x33, "AND", AddrMode.StackRelativeIndirectY,     Op.And, Access.Read, Width.M);

        Set(0x49, "EOR", AddrMode.Immediate,                  Op.Eor, Access.Read, Width.M);
        Set(0x45, "EOR", AddrMode.DirectPage,                 Op.Eor, Access.Read, Width.M);
        Set(0x55, "EOR", AddrMode.DirectPageX,                Op.Eor, Access.Read, Width.M);
        Set(0x4D, "EOR", AddrMode.Absolute,                   Op.Eor, Access.Read, Width.M);
        Set(0x5D, "EOR", AddrMode.AbsoluteX,                  Op.Eor, Access.Read, Width.M);
        Set(0x59, "EOR", AddrMode.AbsoluteY,                  Op.Eor, Access.Read, Width.M);
        Set(0x41, "EOR", AddrMode.DirectPageIndexedIndirectX, Op.Eor, Access.Read, Width.M);
        Set(0x51, "EOR", AddrMode.DirectPageIndirectY,        Op.Eor, Access.Read, Width.M);
        Set(0x52, "EOR", AddrMode.DirectPageIndirect,         Op.Eor, Access.Read, Width.M);
        Set(0x47, "EOR", AddrMode.DirectPageIndirectLong,     Op.Eor, Access.Read, Width.M);
        Set(0x57, "EOR", AddrMode.DirectPageIndirectLongY,    Op.Eor, Access.Read, Width.M);
        Set(0x4F, "EOR", AddrMode.AbsoluteLong,               Op.Eor, Access.Read, Width.M);
        Set(0x5F, "EOR", AddrMode.AbsoluteLongX,              Op.Eor, Access.Read, Width.M);
        Set(0x43, "EOR", AddrMode.StackRelative,              Op.Eor, Access.Read, Width.M);
        Set(0x53, "EOR", AddrMode.StackRelativeIndirectY,     Op.Eor, Access.Read, Width.M);
```

Update the class's `<remarks>`: it currently says thirty-two opcodes are defined and 224 are undefined. It is now 77 and 179.

- [ ] **Step 6: Make the three `Exec` arms width-aware**

In `src/SixtyFiveXX/Cpu.Exec.cs`, replace the three logic arms:

```csharp
            // Logic. Width-aware for the 65816, the same shape Op.Lda uses: the 8-bit path
            // writes through A8, which preserves A's high byte (the hidden B accumulator) on the
            // 816 and folds to a plain assignment on the five 8-bit cores; the 16-bit path
            // operates on the full accumulator and takes N from bit 15. The variant guard comes
            // first so those five cores never load _wide at all — see the remarks on _wide, and
            // Op.Lda's own comment for why the guard is load-bearing rather than defensive.
            case Op.And:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 &= _data; SetZN(A8); }
                else { _s.A &= _data16; SetZN16(_s.A); }
                break;

            case Op.Ora:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 |= _data; SetZN(A8); }
                else { _s.A |= _data16; SetZN16(_s.A); }
                break;

            case Op.Eor:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 ^= _data; SetZN(A8); }
                else { _s.A ^= _data16; SetZN16(_s.A); }
                break;
```

`A8 &= _data` reads through the getter and writes through the setter, so the B-preserving behaviour task 2 added applies. On the five 8-bit cores the whole expression is unchanged from today.

- [ ] **Step 7: Run the unit tests**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "Category!=Performance"`
Expected: **PASS, 491** (489 + 2). `W65C816WidthTests` must still pass — it is the tripwire that every one of the 45 new entries declares a `Width`.

- [ ] **Step 8: Raise `ExpectedImplementedOpcodes`**

In `tests/SixtyFiveXX.Conformance/Harte816Tests.cs`, change `32` to `77` and extend the `<summary>` with one sentence: task 3 adds `ORA`, `AND` and `EOR` in all fifteen addressing forms each — 32 + 45 = 77.

- [ ] **Step 9: Run the conformance suite**

Run: `dotnet test tests/SixtyFiveXX.Conformance -f net10.0`
Expected: **PASS, 1464** (1374 + 90 — 45 opcodes × 2 modes). This downloads roughly 510 MB of vectors on first run and will take considerably longer than the 2m36s baseline. The first 1,309 must be unchanged.

- [ ] **Step 10: Run both target frameworks, then commit**

```bash
git add src/SixtyFiveXX/MicroOpTable.cs src/SixtyFiveXX/Opcodes65C816.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/W65C816AluTests.cs tests/SixtyFiveXX.Conformance/Harte816Tests.cs
git commit -m "feat: 65816 ORA, AND and EOR in all fifteen addressing modes"
```

**Gate:** conformance **1464**, unit **491**, both TFMs, 900,000 new vectors green, the pre-existing 1,309 unchanged.

---

### Task 4: `CMP`, `CPX`, `CPY` — 21 opcodes

The first opcodes whose width comes from `x`. Sequenced before the arithmetic task deliberately: `CPX`/`CPY` use only modes 7b certified and an operation already implemented for 8 bits, so a fault in `_wide`'s `X` path surfaces here rather than entangled with 16-bit BCD.

**Files:**
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (21 entries)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (three arms, one new helper)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (77 → 98)
- Test: `tests/SixtyFiveXX.Tests/W65C816AluTests.cs` (append)

**Interfaces:**
- Consumes: `EmitAddressed816` (task 3), `_wide` (task 2).
- Produces: `private void Compare16(ushort register)` in `Cpu.Exec.cs`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SixtyFiveXX.Tests/W65C816AluTests.cs`:

```csharp
    /// <summary>
    /// The discriminating test for the whole width mechanism: <c>CPX</c>'s width must come from
    /// <c>x</c>, not <c>m</c>. Set up so the two answers differ in PC and cycle count rather than
    /// in flags — an 8-bit CPX would compare $34 against X's low byte $34 and set Z just the same,
    /// but would consume one operand byte instead of two and leave PC at $C002.
    /// </summary>
    [Fact]
    public void Cpx_TakesItsWidthFromTheXFlagNotTheMFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE0;       // CPX #
        ram[0xC001] = 0x34;
        ram[0xC002] = 0x12;       // operand $1234 when x = 0

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator — the flag a wrong implementation would read
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.X = 0x1234;

        cpu.Step();

        Assert.Equal(0xC003, cpu.State.PC);
        Assert.True(cpu.State.Z);
        Assert.True(cpu.State.C);
    }

    /// <summary>
    /// A 16-bit compare's carry is the absence of a 17-bit borrow, and N comes from bit 15.
    /// Fails against a <c>Compare</c> that narrows the difference to 8 bits: $1000 - $2000 has a
    /// low byte of $00, so Z would be set and C would be computed from the wrong subtraction.
    /// </summary>
    [Fact]
    public void Cmp_SixteenBit_TakesCarryAndNFromTheFullDifference()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xC9;       // CMP #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // operand $2000

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x1000;

        cpu.Step();

        Assert.False(cpu.State.C);
        Assert.False(cpu.State.Z);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// With an 8-bit index, only the low byte participates. X's high byte is $00 by the
    /// continuously held invariant whenever x is set, so this pins the narrowing on the operand
    /// side: a 16-bit compare against a 1-byte operand would read the following instruction byte
    /// as the operand's high half and clear Z.
    /// </summary>
    [Fact]
    public void Cpx_EightBitIndex_ComparesOnlyTheLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE0;       // CPX #
        ram[0xC001] = 0x34;
        ram[0xC002] = 0x99;       // decoy: would become the high half of a 16-bit operand

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.X = 0x0034;

        cpu.Step();

        Assert.Equal(0xC002, cpu.State.PC);
        Assert.True(cpu.State.Z);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816AluTests"`
Expected: **FAIL, 3 of 5**, with `UndefinedOpcodeException` for `$E0` and `$C9`.

- [ ] **Step 3: Add the 21 table entries**

```csharp
        // Compare against the accumulator: fifteen forms, Width.M.
        Set(0xC9, "CMP", AddrMode.Immediate,                  Op.Cmp, Access.Read, Width.M);
        Set(0xC5, "CMP", AddrMode.DirectPage,                 Op.Cmp, Access.Read, Width.M);
        Set(0xD5, "CMP", AddrMode.DirectPageX,                Op.Cmp, Access.Read, Width.M);
        Set(0xCD, "CMP", AddrMode.Absolute,                   Op.Cmp, Access.Read, Width.M);
        Set(0xDD, "CMP", AddrMode.AbsoluteX,                  Op.Cmp, Access.Read, Width.M);
        Set(0xD9, "CMP", AddrMode.AbsoluteY,                  Op.Cmp, Access.Read, Width.M);
        Set(0xC1, "CMP", AddrMode.DirectPageIndexedIndirectX, Op.Cmp, Access.Read, Width.M);
        Set(0xD1, "CMP", AddrMode.DirectPageIndirectY,        Op.Cmp, Access.Read, Width.M);
        Set(0xD2, "CMP", AddrMode.DirectPageIndirect,         Op.Cmp, Access.Read, Width.M);
        Set(0xC7, "CMP", AddrMode.DirectPageIndirectLong,     Op.Cmp, Access.Read, Width.M);
        Set(0xD7, "CMP", AddrMode.DirectPageIndirectLongY,    Op.Cmp, Access.Read, Width.M);
        Set(0xCF, "CMP", AddrMode.AbsoluteLong,               Op.Cmp, Access.Read, Width.M);
        Set(0xDF, "CMP", AddrMode.AbsoluteLongX,              Op.Cmp, Access.Read, Width.M);
        Set(0xC3, "CMP", AddrMode.StackRelative,              Op.Cmp, Access.Read, Width.M);
        Set(0xD3, "CMP", AddrMode.StackRelativeIndirectY,     Op.Cmp, Access.Read, Width.M);

        // Compare against an index register: three forms each, and Width.X — the first opcodes
        // on this core whose operand width comes from x rather than m.
        Set(0xE0, "CPX", AddrMode.Immediate,  Op.Cpx, Access.Read, Width.X);
        Set(0xE4, "CPX", AddrMode.DirectPage, Op.Cpx, Access.Read, Width.X);
        Set(0xEC, "CPX", AddrMode.Absolute,   Op.Cpx, Access.Read, Width.X);

        Set(0xC0, "CPY", AddrMode.Immediate,  Op.Cpy, Access.Read, Width.X);
        Set(0xC4, "CPY", AddrMode.DirectPage, Op.Cpy, Access.Read, Width.X);
        Set(0xCC, "CPY", AddrMode.Absolute,   Op.Cpy, Access.Read, Width.X);
```

Update the class `<remarks>` count: 98 defined, 158 undefined.

- [ ] **Step 4: Add `Compare16` and make the three arms width-aware**

In `src/SixtyFiveXX/Cpu.Exec.cs`, beside `Compare`:

```csharp
    /// <summary>
    /// Compares a 16-bit register against <c>_data16</c>, setting C, Z and N — the 65816
    /// native-mode counterpart of <see cref="Compare"/>. C is the absence of a borrow out of
    /// bit 16, so the subtraction is performed in <c>int</c> and tested before narrowing.
    /// </summary>
    private void Compare16(ushort register)
    {
        var result = register - _data16;
        _s.C = result >= 0;
        SetZN16((ushort)result);
    }
```

Replace the three compare arms:

```csharp
            // Compares. CMP is sized by m; CPX and CPY by x. Which flag each one reads is not
            // decided here — _wide already holds the answer, resolved once at fetch from the
            // opcode's declared Width (Cpu.FetchOpcode). See the remarks on _wide.
            case Op.Cmp:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(A8);
                else Compare16(_s.A);
                break;

            case Op.Cpx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(X8);
                else Compare16(_s.X);
                break;

            case Op.Cpy:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(Y8);
                else Compare16(_s.Y);
                break;
```

- [ ] **Step 5: Run the unit tests**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "Category!=Performance"`
Expected: **PASS, 494** (491 + 3).

- [ ] **Step 6: Prove the width test discriminates, by mutation**

Temporarily change `FetchOpcode`'s width resolution so `Width.X` reads `!_s.M` instead of `!_s.XFlag`:

```csharp
                Width.X => !_s.M,     // deliberately wrong, for this step only
```

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~Cpx_TakesItsWidthFromTheXFlag"`
Expected: **FAIL**, `Assert.Equal() Failure: Expected: 49155, Actual: 49154` — PC $C003 versus $C002.

**Revert the mutation** and re-run to confirm it passes again. Record the failure output in the task report; a test for a width mechanism that would pass with the wrong flag wired in is worth nothing, and this is the phase's central mechanism.

- [ ] **Step 7: Raise `ExpectedImplementedOpcodes` to 98, run conformance, commit**

Run: `dotnet test tests/SixtyFiveXX.Conformance -f net10.0`
Expected: **PASS, 1506** (1464 + 42). Roughly 240 MB of new vectors.

Then both TFMs, then:

```bash
git add src/SixtyFiveXX/Opcodes65C816.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/W65C816AluTests.cs tests/SixtyFiveXX.Conformance/Harte816Tests.cs
git commit -m "feat: 65816 CMP, CPX and CPY, the first opcodes sized by the x flag"
```

**Gate:** conformance **1506**, unit **494**, both TFMs, 420,000 new vectors green, the mutation experiment recorded.

---

### Task 5: `ADC` and `SBC` — 30 opcodes

The hardest task in the phase. Binary and decimal ship together because they cannot be separated by gate: a single `.n` file mixes vectors with `D` set and clear, so no subset of the vectors certifies binary arithmetic alone.

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs` (two new members, per §10.2)
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (30 entries)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (two arms, two helpers)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (98 → 128)
- Test: `tests/SixtyFiveXX.Tests/W65C816AluTests.cs` (append)

**Interfaces:**
- Consumes: research §10.1, §10.2 and §10.5; `_wide`; `EmitAddressed816`.
- Produces: `Op.Adc816`, `Op.Sbc816`, `private void Adc816(...)`, `private void Sbc816(...)`.

**Read research §10 before writing a line of this task.** Three of its findings change what you write:

1. **§10.2 decides the `Op` members.** The plan below assumes the pre-committed default — a new `Op.Adc816`/`Op.Sbc816` pair, which is what the rule produces on divergence *or* on silence. If §10.2 concluded the sources state the behaviour is identical to the 65C02's, use `Op.AdcCmos`/`Op.SbcCmos` instead and skip step 3 entirely; everything else is unchanged.
2. **§10.5's first sentence decides whether an extra decimal cycle exists.** If the 65816's `ADC` formula carries a decimal term the way the 65C02's does, the emitter needs a conditionally skipped slot — see step 6's alternative. If it does not, `ADC` uses the ordinary read tail and no emitter change is needed at all.
3. **§10.1 may be a recorded gap.** If Clark was silent on the 16-bit decimal algorithm, derive it from the vectors in step 8 and write the derivation back into §10.1 as a *measured* result, labelled as measured. Do not retro-fit a citation to a number you measured.

- [ ] **Step 1: Write the failing binary tests**

Append to `tests/SixtyFiveXX.Tests/W65C816AluTests.cs`:

```csharp
    /// <summary>
    /// 16-bit binary add: V comes from bit 15, C from bit 16. Fails against an 8-bit
    /// <c>Adc</c> reached with a 16-bit operand.
    /// </summary>
    [Fact]
    public void Adc_SixteenBitBinary_TakesOverflowFromBitFifteen()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x69;       // ADC #
        ram[0xC001] = 0x01;
        ram[0xC002] = 0x00;       // operand $0001

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.D = false;
        cpu.State.C = false;
        cpu.State.A = 0x7FFF;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.V);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.C);
    }

    /// <summary>
    /// 8-bit add on the 65816 must not disturb the hidden B accumulator, which is a real risk
    /// here because <c>Adc</c> writes its result through <c>A8</c> internally rather than at the
    /// call site. Fails against an <c>A8</c> setter that assigns the whole 16-bit field.
    /// </summary>
    [Fact]
    public void Adc_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x69;       // ADC #
        ram[0xC001] = 0x01;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.D = false;
        cpu.State.C = false;
        cpu.State.A = 0x1210;     // B = $12

        cpu.Step();

        Assert.Equal(0x1211, cpu.State.A);
    }

    /// <summary>
    /// 16-bit subtract: C is the absence of a borrow out of bit 16.
    /// </summary>
    [Fact]
    public void Sbc_SixteenBitBinary_ClearsCarryOnABorrow()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE9;       // SBC #
        ram[0xC001] = 0x01;
        ram[0xC002] = 0x00;       // operand $0001

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.D = false;
        cpu.State.C = true;       // no incoming borrow
        cpu.State.A = 0x0000;

        cpu.Step();

        Assert.Equal(0xFFFF, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816AluTests"`
Expected: **FAIL, 3 of 8**, `UndefinedOpcodeException` for `$69` and `$E9`.

- [ ] **Step 3: Add the `Op` members (skip if §10.2 said reuse)**

In `src/SixtyFiveXX/Op.cs`, in the 65816 group beside `Xce`/`Rep`/`Sep`:

```csharp
    /// <summary>
    /// The 65816's <c>ADC</c> and <c>SBC</c>. Separate members from both <see cref="Adc"/>
    /// (NMOS) and <see cref="AdcCmos"/> for the reason phase 2a's ledger records for the CMOS
    /// pair: the decimal-mode correction and the flags it leaks are what distinguish these
    /// parts, and folding two behaviours into one member behind a variant test would put a
    /// branch on the ALU path to save an enum member. These are also the only arithmetic
    /// members that operate at two widths — see research document §10.1.
    /// </summary>
    Adc816, Sbc816,
```

- [ ] **Step 4: Add the 30 table entries**

```csharp
        Set(0x69, "ADC", AddrMode.Immediate,                  Op.Adc816, Access.Read, Width.M);
        Set(0x65, "ADC", AddrMode.DirectPage,                 Op.Adc816, Access.Read, Width.M);
        Set(0x75, "ADC", AddrMode.DirectPageX,                Op.Adc816, Access.Read, Width.M);
        Set(0x6D, "ADC", AddrMode.Absolute,                   Op.Adc816, Access.Read, Width.M);
        Set(0x7D, "ADC", AddrMode.AbsoluteX,                  Op.Adc816, Access.Read, Width.M);
        Set(0x79, "ADC", AddrMode.AbsoluteY,                  Op.Adc816, Access.Read, Width.M);
        Set(0x61, "ADC", AddrMode.DirectPageIndexedIndirectX, Op.Adc816, Access.Read, Width.M);
        Set(0x71, "ADC", AddrMode.DirectPageIndirectY,        Op.Adc816, Access.Read, Width.M);
        Set(0x72, "ADC", AddrMode.DirectPageIndirect,         Op.Adc816, Access.Read, Width.M);
        Set(0x67, "ADC", AddrMode.DirectPageIndirectLong,     Op.Adc816, Access.Read, Width.M);
        Set(0x77, "ADC", AddrMode.DirectPageIndirectLongY,    Op.Adc816, Access.Read, Width.M);
        Set(0x6F, "ADC", AddrMode.AbsoluteLong,               Op.Adc816, Access.Read, Width.M);
        Set(0x7F, "ADC", AddrMode.AbsoluteLongX,              Op.Adc816, Access.Read, Width.M);
        Set(0x63, "ADC", AddrMode.StackRelative,              Op.Adc816, Access.Read, Width.M);
        Set(0x73, "ADC", AddrMode.StackRelativeIndirectY,     Op.Adc816, Access.Read, Width.M);

        Set(0xE9, "SBC", AddrMode.Immediate,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xE5, "SBC", AddrMode.DirectPage,                 Op.Sbc816, Access.Read, Width.M);
        Set(0xF5, "SBC", AddrMode.DirectPageX,                Op.Sbc816, Access.Read, Width.M);
        Set(0xED, "SBC", AddrMode.Absolute,                   Op.Sbc816, Access.Read, Width.M);
        Set(0xFD, "SBC", AddrMode.AbsoluteX,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xF9, "SBC", AddrMode.AbsoluteY,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xE1, "SBC", AddrMode.DirectPageIndexedIndirectX, Op.Sbc816, Access.Read, Width.M);
        Set(0xF1, "SBC", AddrMode.DirectPageIndirectY,        Op.Sbc816, Access.Read, Width.M);
        Set(0xF2, "SBC", AddrMode.DirectPageIndirect,         Op.Sbc816, Access.Read, Width.M);
        Set(0xE7, "SBC", AddrMode.DirectPageIndirectLong,     Op.Sbc816, Access.Read, Width.M);
        Set(0xF7, "SBC", AddrMode.DirectPageIndirectLongY,    Op.Sbc816, Access.Read, Width.M);
        Set(0xEF, "SBC", AddrMode.AbsoluteLong,               Op.Sbc816, Access.Read, Width.M);
        Set(0xFF, "SBC", AddrMode.AbsoluteLongX,              Op.Sbc816, Access.Read, Width.M);
        Set(0xE3, "SBC", AddrMode.StackRelative,              Op.Sbc816, Access.Read, Width.M);
        Set(0xF3, "SBC", AddrMode.StackRelativeIndirectY,     Op.Sbc816, Access.Read, Width.M);
```

Note `$EB` is **not** here: on the 65816 that byte is `XBA`, not the 6502's undocumented `SBC` alias. Update the class `<remarks>`: 128 defined, 128 undefined.

- [ ] **Step 5: Implement binary arithmetic at both widths**

In `src/SixtyFiveXX/Cpu.Exec.cs`, add the two helpers and the two arms. **The decimal branches are written in step 7, from §10.1** — write them as the binary path only for now, so this step can be verified in isolation:

```csharp
    /// <summary>
    /// The 65816's add with carry, at whichever width <c>_wide</c> selects. Binary mode only in
    /// this form; the decimal correction is added below from research document §10.1.
    /// </summary>
    private void Adc816(byte value8, ushort value16)
    {
        if (!_wide)
        {
            var carry8 = _s.C ? 1 : 0;
            var sum8 = A8 + value8 + carry8;
            _s.C = sum8 > 0xFF;
            _s.V = (~(A8 ^ value8) & (A8 ^ sum8) & 0x80) != 0;
            A8 = (byte)sum8;
            SetZN(A8);
            return;
        }

        var carry = _s.C ? 1 : 0;
        var sum = _s.A + value16 + carry;
        _s.C = sum > 0xFFFF;
        _s.V = (~(_s.A ^ value16) & (_s.A ^ sum) & 0x8000) != 0;
        _s.A = (ushort)sum;
        SetZN16(_s.A);
    }

    /// <summary>The 65816's subtract with borrow. See <see cref="Adc816"/>.</summary>
    private void Sbc816(byte value8, ushort value16)
    {
        if (!_wide)
        {
            var borrow8 = _s.C ? 0 : 1;
            var diff8 = A8 - value8 - borrow8;
            _s.C = diff8 >= 0;
            _s.V = ((A8 ^ value8) & (A8 ^ diff8) & 0x80) != 0;
            A8 = (byte)diff8;
            SetZN(A8);
            return;
        }

        var borrow = _s.C ? 0 : 1;
        var diff = _s.A - value16 - borrow;
        _s.C = diff >= 0;
        _s.V = ((_s.A ^ value16) & (_s.A ^ diff) & 0x8000) != 0;
        _s.A = (ushort)diff;
        SetZN16(_s.A);
    }
```

The arms, in the arithmetic group:

```csharp
            // 65816 arithmetic. Both widths and both modes live in one helper each, because the
            // operand arrives in _data or _data16 depending on width and the helper is the only
            // place that knows which. Never reached by an 8-bit core — Op.Adc816 appears in no
            // table but the 65816's — so these need no variant guard, unlike Op.Lda's arm.
            case Op.Adc816: Adc816(_data, _data16); break;
            case Op.Sbc816: Sbc816(_data, _data16); break;
```

- [ ] **Step 6: Emitter — only if §10.5 says a decimal cycle exists**

If §10.5's first sentence says the 65816 pays **no** extra cycle in decimal mode, **do nothing in this step**: `ADC` and `SBC` are ordinary `Access.Read` opcodes and `EmitAddressed816` already emits the right sequence.

If it says a decimal cycle **is** paid, add a conditionally skipped slot, the idiom `MicroOp.BcdExtra` already uses for the 65C02: emit an extra micro-op after the access tail, and have the preceding micro-op advance `_mpc` past it when `D` is clear. Follow `MicroOp.ReadExecCmosArith`/`MicroOp.BcdExtra` in `Cpu.cs` and `MicroOpTable.EmitAccess` exactly rather than inventing a second mechanism, and classify the new micro-op in `MicroOps.BuildPinsTable` or `BuildInternalCycleTable` — `BusPinsTests.EveryMicroOpHasAPinClassification` will fail until you do.

- [ ] **Step 7: Write the decimal branches**

**If §10.1 gives a cited algorithm, implement that and ignore the code below.** §10.1 wins over everything here.

If §10.1 is a recorded gap, start from the code below. **It is a hypothesis, not a fact**, and the plan says so in the comment it carries into the source: the 8-bit path delegates to this codebase's already-certified CMOS helpers, and the 16-bit path is the four-digit generalisation of the same algorithm. Step 8's vectors are the arbiter, and whatever they establish gets written back into §10.1 as a measured result.

Insert at the top of each helper's non-`_wide` branch:

```csharp
        if (!_wide)
        {
            // 8-bit decimal on the 65816 is taken to be the 65C02's, which this codebase has
            // already certified against 40,000 vectors of $69/$72/$E9/$F2. Same silicon family,
            // and no source describes a divergence — see research document §10.1. If the vectors
            // disagree, they are right and this delegation is what has to change.
            if (_s.D) { AdcCmos(value8); return; }
            ...existing binary 8-bit body...
        }
```

and, for the 16-bit branch of `Adc816`, before the binary body:

```csharp
        if (_s.D)
        {
            // HYPOTHESIS, not a citation: the four-digit generalisation of AdcCmos above —
            // nibble-wise correction, V taken from the partially corrected top nibble, N and Z
            // from the final decimal result. Research document §10.1 records that the sources are
            // silent on this; the vectors settle it, and the settled version is written back
            // there as a measured result.
            var dcarry = _s.C ? 1 : 0;
            var n0 = (_s.A & 0x000F) + (value16 & 0x000F) + dcarry;
            if (n0 > 0x09) n0 += 0x06;
            var n1 = ((_s.A >> 4) & 0x0F) + ((value16 >> 4) & 0x0F) + (n0 > 0x0F ? 1 : 0);
            if (n1 > 0x09) n1 += 0x06;
            var n2 = ((_s.A >> 8) & 0x0F) + ((value16 >> 8) & 0x0F) + (n1 > 0x0F ? 1 : 0);
            if (n2 > 0x09) n2 += 0x06;
            var n3 = ((_s.A >> 12) & 0x0F) + ((value16 >> 12) & 0x0F) + (n2 > 0x0F ? 1 : 0);

            _s.V = (~(_s.A ^ value16) & (_s.A ^ (n3 << 12)) & 0x8000) != 0;

            if (n3 > 0x09) n3 += 0x06;
            _s.C = n3 > 0x0F;
            _s.A = (ushort)(((n3 & 0x0F) << 12) | ((n2 & 0x0F) << 8) |
                            ((n1 & 0x0F) << 4) | (n0 & 0x0F));
            SetZN16(_s.A);
            return;
        }
```

and, for `Sbc816`'s 16-bit branch, after the binary result and its flags have been computed but before `_s.A` is assigned — flags come from the binary result, exactly as the 8-bit `Sbc` and `SbcCmos` already do:

```csharp
        if (_s.D)
        {
            // HYPOTHESIS, as above: the four-digit generalisation of the nibble-wise NMOS
            // subtract correction. C, V, Z and N are already set from the binary difference
            // above; only the accumulator differs in decimal mode.
            var n0 = (_s.A & 0x0F) - (value16 & 0x0F) - borrow;
            var n1 = ((_s.A >> 4) & 0x0F) - ((value16 >> 4) & 0x0F);
            var n2 = ((_s.A >> 8) & 0x0F) - ((value16 >> 8) & 0x0F);
            var n3 = ((_s.A >> 12) & 0x0F) - ((value16 >> 12) & 0x0F);

            if ((n0 & 0x10) != 0) { n0 -= 0x06; n1--; }
            if ((n1 & 0x10) != 0) { n1 -= 0x06; n2--; }
            if ((n2 & 0x10) != 0) { n2 -= 0x06; n3--; }
            if ((n3 & 0x10) != 0) n3 -= 0x06;

            _s.A = (ushort)(((n3 & 0x0F) << 12) | ((n2 & 0x0F) << 8) |
                            ((n1 & 0x0F) << 4) | (n0 & 0x0F));
            return;
        }
```

Restructuring the binary bodies so `borrow` and the flag assignments are in scope where these blocks need them is part of this step.

- [ ] **Step 8: Run the vectors, and treat the first decimal failure as information, not as a bug to patch**

Run: `dotnet test tests/SixtyFiveXX.Conformance -f net10.0 --filter "FullyQualifiedName~Harte816Tests"` after raising `ExpectedImplementedOpcodes` to **128**.

If decimal vectors fail, do **not** tune constants until the vectors stop complaining. Instead: pick one failing vector, hand-trace it, and work out which of the four flags and which correction step disagrees. Then write the finding into §10.1 as a measured result with the vector that established it — the same way §11 records the `XCE` `SH` rule and phase 1's ledger records the NMOS `ADC` flag leak. A correction landed without an explanation is a correction the next reader cannot check.

- [ ] **Step 9: Add one decimal unit test, from whatever step 8 established**

Append a test pinning a 16-bit decimal case whose answer is now known — for example `A = $9999`, `ADC #$0001`, `D` set, `m = 0`, expecting `A = $0000` and `C` set if that is what §10.1 (as cited or as measured) says. Write the expectation from §10.1, and reference §10.1 in the test's doc comment.

- [ ] **Step 10: Run everything, both TFMs, commit**

Expected: unit **498** (494 + 4), conformance **1566** (1506 + 60). Roughly 340 MB of new vectors.

```bash
git add src/SixtyFiveXX/Op.cs src/SixtyFiveXX/Opcodes65C816.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/W65C816AluTests.cs tests/SixtyFiveXX.Conformance/Harte816Tests.cs \
        docs/superpowers/research/2026-08-03-65816-reference-sources.md
git commit -m "feat: 65816 ADC and SBC at both widths, binary and decimal"
```

**Gate:** conformance **1566**, unit **498**, both TFMs, 600,000 new vectors green with **no exclusions** — decimal included. Any divergence between §10 and the vectors is written back into §10, labelled as measured.

---

### Task 6: `BIT` — 5 opcodes

Small, and the one operation with a genuinely mode-dependent flag rule.

**Files:**
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (5 entries)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (two arms)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (128 → 133)
- Test: `tests/SixtyFiveXX.Tests/W65C816AluTests.cs` (append)

**Interfaces:**
- Consumes: `_wide`, `EmitAddressed816`.
- Produces: nothing new; `Op.Bit` and `Op.BitImm` already exist.

- [ ] **Step 1: Write the failing tests**

```csharp
    /// <summary>
    /// Every addressing mode of BIT but immediate copies the operand's top two bits into N and
    /// V. At sixteen bits those are bits 15 and 14, not 7 and 6. Fails against an arm that reads
    /// $80/$40 out of a 16-bit operand.
    /// </summary>
    [Fact]
    public void Bit_SixteenBit_TakesNFromBitFifteenAndVFromBitFourteen()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x2C;       // BIT abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // AA = $2000
        ram[0x002000] = 0x00;     // operand low
        ram[0x002001] = 0x40;     // operand high -> $4000: bit 15 clear, bit 14 set

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0xFFFF;

        cpu.Step();

        Assert.False(cpu.State.N);
        Assert.True(cpu.State.V);
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// The immediate form sets Z alone, at either width — the behaviour this codebase already
    /// models as <c>Op.BitImm</c> for the 65C02. N and V are pre-set to values the operand would
    /// overwrite if the wrong arm ran, so an <c>Op.Bit</c> mis-wiring fails here.
    /// </summary>
    [Fact]
    public void BitImmediate_SixteenBit_SetsOnlyZ()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x89;       // BIT #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x80;       // operand $8000 — would set N if Op.Bit ran

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0000;
        cpu.State.N = false;
        cpu.State.V = false;

        cpu.Step();

        Assert.True(cpu.State.Z);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.V);
    }
```

- [ ] **Step 2: Run and watch them fail**

Expected: **FAIL, 2**, `UndefinedOpcodeException` for `$2C` and `$89`.

- [ ] **Step 3: Add the 5 entries**

```csharp
        // BIT. The immediate form is a different operation, not a different mode of the same one:
        // it sets Z alone and leaves N and V untouched. Op.BitImm already models that for the
        // 65C02 and needs only widening here.
        Set(0x89, "BIT", AddrMode.Immediate,   Op.BitImm, Access.Read, Width.M);
        Set(0x24, "BIT", AddrMode.DirectPage,  Op.Bit,    Access.Read, Width.M);
        Set(0x34, "BIT", AddrMode.DirectPageX, Op.Bit,    Access.Read, Width.M);
        Set(0x2C, "BIT", AddrMode.Absolute,    Op.Bit,    Access.Read, Width.M);
        Set(0x3C, "BIT", AddrMode.AbsoluteX,   Op.Bit,    Access.Read, Width.M);
```

Update the class `<remarks>`: 133 defined, 123 undefined.

- [ ] **Step 4: Widen the two arms**

```csharp
            case Op.Bit:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                {
                    _s.Z = (A8 & _data) == 0;
                    _s.N = (_data & 0x80) != 0;
                    _s.V = (_data & 0x40) != 0;
                }
                else
                {
                    _s.Z = (_s.A & _data16) == 0;
                    _s.N = (_data16 & 0x8000) != 0;
                    _s.V = (_data16 & 0x4000) != 0;
                }
                break;

            case Op.BitImm:
                _s.Z = TVariant.Variant != CpuVariant.W65C816 || !_wide
                    ? (A8 & _data) == 0
                    : (_s.A & _data16) == 0;
                break;
```

- [ ] **Step 5: Run everything, raise `ExpectedImplementedOpcodes` to 133, both TFMs, commit**

Expected: unit **500** (498 + 2), conformance **1576** (1566 + 10). Roughly 57 MB of new vectors.

```bash
git add src/SixtyFiveXX/Opcodes65C816.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/W65C816AluTests.cs tests/SixtyFiveXX.Conformance/Harte816Tests.cs
git commit -m "feat: 65816 BIT at both widths, immediate form still setting only Z"
```

**Gate:** conformance **1576**, unit **500**, both TFMs, 100,000 new vectors green.

---

### Task 7: `LDX`, `LDY`, `STX`, `STY`, `STZ` — 20 opcodes and the phase's one new addressing mode

**Files:**
- Modify: `src/SixtyFiveXX/AddrMode.cs` (`DirectPageY`)
- Modify: `src/SixtyFiveXX/MicroOp.cs` (`DirectPageIndexY`, and `BuildInternalCycleTable`)
- Modify: `src/SixtyFiveXX/Cpu.cs` (the new micro-op's `Execute` case)
- Modify: `src/SixtyFiveXX/MicroOpTable.cs` (the `DirectPageY` case, and the bank-0 exclusion set)
- Modify: `src/SixtyFiveXX/Opcodes65C816.cs` (20 entries)
- Modify: `src/SixtyFiveXX/Cpu.Exec.cs` (four arms)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (133 → 153)
- Test: `tests/SixtyFiveXX.Tests/W65C816IndexModeTests.cs` (create)

**Interfaces:**
- Consumes: everything above.
- Produces: `AddrMode.DirectPageY`, `MicroOp.DirectPageIndexY`. Nothing later in this phase depends on them; phase 7c′ reuses both for nothing, and phase 7d for nothing — `dp,Y` is used by `LDX` and `STX` and by no other instruction on the part.

**The trap in this task, stated up front.** `MicroOpTable.EmitAddressed816` ends with:

```csharp
var carry = info.Mode is not (AddrMode.DirectPage or AddrMode.DirectPageX or AddrMode.StackRelative);
```

`DirectPageY` is direct-page addressing and is therefore **bank-0 confined**: it must join that exclusion set. Forgetting it makes a 16-bit `LDX $FF,Y` landing on `$00FFFF` read its high byte from `$010000` instead of `$000000`. No SingleStepTests vector is likely to reach that — the same zero-coverage shape as the `sr,S` and `(sr,S),Y` findings 7b's reviews caught in both directions — so it is pinned by a unit test below, not by the vectors.

- [ ] **Step 1: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816IndexModeTests.cs`:

```csharp
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Direct-page indexed by Y — the one addressing mode phase 7c adds, used by <c>LDX</c> and
/// <c>STX</c> and by nothing else on the part. Its wrapping and bank-confinement rules are the
/// same ones <c>dp,X</c> obeys, and the same ones phase 7b's reviews twice found wired the wrong
/// way round for a stack mode with no vector coverage; each test below was verified to fail
/// against a deliberately broken version of the corresponding production line.
/// </summary>
public class W65C816IndexModeTests
{
    /// <summary>
    /// Emulation mode with <c>DL == $00</c>: the index add wraps within the direct page and keeps
    /// <c>DH</c>. Clark's appendix is explicit that DH need not be zero, which is why DP is
    /// $FF00 here rather than $0000. Fails against an implementation that wraps at 16 bits
    /// instead: the read would land on $000001, the decoy.
    /// </summary>
    [Fact]
    public void LdxDirectPageY_EmulationMode_WrapsWithinThePage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0xFF;       // DO -> D + DO = $FFFF
        ram[0x00FF01] = 0x42;     // wrapped: (DP & $FF00) | (($FFFF + 2) & $FF)
        ram[0x000001] = 0x99;     // decoy: where a 16-bit wrap would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;       // emulation: x is forced to 1, so this is an 8-bit load
        cpu.State.DP = 0xFF00;    // DL == $00, DH == $FF
        cpu.State.Y = 0x0002;

        cpu.Step();

        Assert.Equal(0x0042, cpu.State.X);
    }

    /// <summary>
    /// The same addresses in native mode, where the page wrap does not apply: the add is a plain
    /// 16-bit one and lands on $000001. The mirror of the test above — together they pin both
    /// arms of the condition, so neither can be deleted unnoticed.
    /// </summary>
    [Fact]
    public void LdxDirectPageY_NativeMode_DoesNotWrapWithinThePage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0xFF;       // DO -> D + DO = $FFFF
        ram[0x000001] = 0x42;     // ($FFFF + 2) & $FFFF
        ram[0x00FF01] = 0x99;     // decoy: where the emulation-mode wrap would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = true;   // 8-bit index, so only one byte is read
        cpu.State.DP = 0xFF00;
        cpu.State.Y = 0x0002;

        cpu.Step();

        Assert.Equal(0x0042, cpu.State.X);
    }

    /// <summary>
    /// dp,Y is bank-0 confined, so a 16-bit load whose low byte sits at $00FFFF takes its high
    /// byte from $000000 — not $010000. Zero vector coverage: it needs x = 0 with D + DO landing
    /// exactly on $FFFF. Fails if <c>DirectPageY</c> is left out of
    /// <c>MicroOpTable.EmitAddressed816</c>'s bank-0 exclusion set, which is the mistake phase
    /// 7b's review found in the opposite direction for <c>(sr,S),Y</c>.
    /// </summary>
    [Fact]
    public void LdxDirectPageY_SixteenBit_WrapsTheHighByteWithinBankZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0x00;       // DO -> D + DO + Y = $FFFF
        ram[0x00FFFF] = 0x34;     // data low
        ram[0x000000] = 0x12;     // data high, wrapped within bank 0
        ram[0x010000] = 0x99;     // decoy: where a wrongly-carrying read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.DP = 0xFFF0;    // DL != $00, so no page wrap
        cpu.State.Y = 0x000F;     // $FFF0 + $00 + $0F = $FFFF

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.X);
    }

    /// <summary>
    /// A 16-bit index store writes two bytes, low first.
    /// </summary>
    [Fact]
    public void Stx_SixteenBitIndex_WritesBothBytes()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x86;       // STX dp
        ram[0xC001] = 0x10;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.DP = 0x0000;
        cpu.State.X = 0x1234;

        cpu.Step();

        Assert.Equal(0x34, ram[0x000010]);
        Assert.Equal(0x12, ram[0x000011]);
    }

    /// <summary>
    /// STZ stores an accumulator-width zero, so its width comes from m even though it names no
    /// register. Set up with m and x deliberately opposed: a Width.X mis-declaration would write
    /// one byte and leave the decoy at $11 intact.
    /// </summary>
    [Fact]
    public void Stz_TakesItsWidthFromTheAccumulatorFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x64;       // STZ dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0xAA;
        ram[0x000011] = 0xBB;     // decoy: untouched if STZ were sized by x

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.XFlag = true;   // 8-bit index — the flag a wrong declaration would read
        cpu.State.DP = 0x0000;

        cpu.Step();

        Assert.Equal(0x00, ram[0x000010]);
        Assert.Equal(0x00, ram[0x000011]);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816IndexModeTests"`
Expected: **FAIL, 5**, `UndefinedOpcodeException` for `$B6`, `$86` and `$64`.

- [ ] **Step 3: Add `AddrMode.DirectPageY`**

In `src/SixtyFiveXX/AddrMode.cs`, immediately after `DirectPageX`:

```csharp
    /// <summary>
    /// <c>dp,Y</c> — <see cref="DirectPage"/> indexed by Y before the read. Data is in bank 0,
    /// and the index add wraps within the direct page when <c>E == 1</c> and <c>DL == $00</c>,
    /// exactly as <see cref="DirectPageX"/> does. Used by <c>LDX</c> and <c>STX</c> and by no
    /// other 65816 instruction. Distinct from <see cref="ZeroPageY"/>, which is the eight-bit
    /// cores' mode and has neither the direct register nor the emulation-mode condition.
    /// </summary>
    DirectPageY,
```

- [ ] **Step 4: Add `MicroOp.DirectPageIndexY`**

In `src/SixtyFiveXX/MicroOp.cs`, next to `DirectPageIndexX`, with a `<summary>` mirroring it and naming research §10.3 as its source. Then add it to `BuildInternalCycleTable`'s list — it drives an address and performs no access, like every other `IO` row:

```csharp
                     MicroOp.End, MicroOp.Unimplemented816, MicroOp.ImpliedExec816, MicroOp.RepSepExec,
                     MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexX, MicroOp.DirectPageIndexY,
                     MicroOp.IndexDirectPageIndirectY,
                     MicroOp.AbsIndexFixup, MicroOp.StackRelativePenalty, MicroOp.IndexStackRelativeIndirectY,
```

Extend that method's `<summary>`, which enumerates why each member is there, with a sentence for the new one.

Do **not** add it to `BuildPinsTable` — internal cycles are `BusPins.None`, which is the array's default, and `BusPinsTests.EveryMicroOpHasAPinClassification` accepts `None` exactly for members declared internal.

- [ ] **Step 5: Implement the micro-op**

In `src/SixtyFiveXX/Cpu.cs`, immediately after `case MicroOp.DirectPageIndexX`:

```csharp
            case MicroOp.DirectPageIndexY:
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                _addr = _s.E && (_s.DP & 0xFF) == 0
                    ? (_addr & 0xFF00) | ((_addr + IndexY()) & 0xFF)
                    : (_addr + IndexY()) & 0xFFFF;
                break;
```

- [ ] **Step 6: Emit the mode, and put it in the bank-0 exclusion set**

In `MicroOpTable.EmitAddressed816`, after the `DirectPageX` case:

```csharp
            // Research document §10.3. Identical in shape to DirectPageX with Y substituted —
            // LDX and STX are the only instructions that use it.
            case AddrMode.DirectPageY:
                ops.AddRange([MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexY]);
                break;
```

And extend the exclusion set — **this line is the one the third unit test exists to protect:**

```csharp
        var carry = info.Mode is not (AddrMode.DirectPage or AddrMode.DirectPageX
            or AddrMode.DirectPageY or AddrMode.StackRelative);
```

Extend the comment above it to name `DirectPageY` alongside the other bank-0-confined modes.

- [ ] **Step 7: Add the 20 table entries**

```csharp
        // Index loads and stores, and STZ. LDX/LDY/STX/STY are Width.X — they move through an
        // index register. STZ is Width.M: it stores an accumulator-width zero, despite naming no
        // register at all.
        Set(0xA2, "LDX", AddrMode.Immediate,    Op.Ldx, Access.Read,  Width.X);
        Set(0xA6, "LDX", AddrMode.DirectPage,   Op.Ldx, Access.Read,  Width.X);
        Set(0xB6, "LDX", AddrMode.DirectPageY,  Op.Ldx, Access.Read,  Width.X);
        Set(0xAE, "LDX", AddrMode.Absolute,     Op.Ldx, Access.Read,  Width.X);
        Set(0xBE, "LDX", AddrMode.AbsoluteY,    Op.Ldx, Access.Read,  Width.X);

        Set(0xA0, "LDY", AddrMode.Immediate,    Op.Ldy, Access.Read,  Width.X);
        Set(0xA4, "LDY", AddrMode.DirectPage,   Op.Ldy, Access.Read,  Width.X);
        Set(0xB4, "LDY", AddrMode.DirectPageX,  Op.Ldy, Access.Read,  Width.X);
        Set(0xAC, "LDY", AddrMode.Absolute,     Op.Ldy, Access.Read,  Width.X);
        Set(0xBC, "LDY", AddrMode.AbsoluteX,    Op.Ldy, Access.Read,  Width.X);

        Set(0x86, "STX", AddrMode.DirectPage,   Op.Stx, Access.Write, Width.X);
        Set(0x96, "STX", AddrMode.DirectPageY,  Op.Stx, Access.Write, Width.X);
        Set(0x8E, "STX", AddrMode.Absolute,     Op.Stx, Access.Write, Width.X);

        Set(0x84, "STY", AddrMode.DirectPage,   Op.Sty, Access.Write, Width.X);
        Set(0x94, "STY", AddrMode.DirectPageX,  Op.Sty, Access.Write, Width.X);
        Set(0x8C, "STY", AddrMode.Absolute,     Op.Sty, Access.Write, Width.X);

        Set(0x64, "STZ", AddrMode.DirectPage,   Op.Stz, Access.Write, Width.M);
        Set(0x74, "STZ", AddrMode.DirectPageX,  Op.Stz, Access.Write, Width.M);
        Set(0x9C, "STZ", AddrMode.Absolute,     Op.Stz, Access.Write, Width.M);
        Set(0x9E, "STZ", AddrMode.AbsoluteX,    Op.Stz, Access.Write, Width.M);
```

Update the class `<remarks>`: 153 defined, 103 undefined, and drop the sentence saying later tasks in *this* phase fill the rest — the rest is 7c′ and 7d.

- [ ] **Step 8: Widen the four `Exec` arms**

```csharp
            case Op.Ldx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) { X8 = _data; SetZN(X8); }
                else { _s.X = _data16; SetZN16(_s.X); }
                break;

            case Op.Ldy:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) { Y8 = _data; SetZN(Y8); }
                else { _s.Y = _data16; SetZN16(_s.Y); }
                break;
```

```csharp
            case Op.Stx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = X8;
                else _data16 = _s.X;
                break;

            case Op.Sty:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Y8;
                else _data16 = _s.Y;
                break;

            case Op.Stz:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = 0;
                else _data16 = 0;
                break;
```

Note that `X8 = _data` is correct and must **not** be given `A8`'s B-preserving treatment: whenever `x` is set, `XH` is `$00` by the continuously held invariant, so clearing it here agrees with the hardware rather than losing data.

- [ ] **Step 9: Prove the bank-0 test discriminates, by mutation**

Temporarily remove `or AddrMode.DirectPageY` from the `carry` expression in step 6.

Run: `dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~LdxDirectPageY_SixteenBit"`
Expected: **FAIL**, `Expected: 4660, Actual: 39220` — `$1234` versus `$9934`, the decoy in bank 1.

**Revert the mutation** and re-run. Record the failure output in the task report. This test is the only thing standing between the exclusion set and a defect the vectors cannot see.

- [ ] **Step 10: Run everything, raise `ExpectedImplementedOpcodes` to 153, both TFMs, commit**

Expected: unit **505** (500 + 5), conformance **1616** (1576 + 40). Roughly 226 MB of new vectors.

```bash
git add src/SixtyFiveXX/AddrMode.cs src/SixtyFiveXX/MicroOp.cs src/SixtyFiveXX/Cpu.cs \
        src/SixtyFiveXX/MicroOpTable.cs src/SixtyFiveXX/Opcodes65C816.cs src/SixtyFiveXX/Cpu.Exec.cs \
        tests/SixtyFiveXX.Tests/W65C816IndexModeTests.cs tests/SixtyFiveXX.Conformance/Harte816Tests.cs
git commit -m "feat: 65816 index loads and stores, STZ, and the dp,Y addressing mode"
```

**Gate:** conformance **1616**, unit **505**, both TFMs, 400,000 new vectors green, the mutation experiment recorded.

---

### Task 8: Whole-branch review and fix wave

**Files:** whatever the review finds. Plus:
- Modify: `README.md` (the variant status table and the conformance section)
- Modify: `docs/superpowers/specs/2026-08-03-65816-core-design.md` (mark 7c verified, with the measured numbers)

- [ ] **Step 1: Produce the branch diff**

```bash
git diff main...HEAD > .superpowers/sdd/review-phase7c.diff
wc -l .superpowers/sdd/review-phase7c.diff
```

- [ ] **Step 2: Review it against this checklist, and against the spec**

Every item below is a specific failure this project has actually had:

- **Width declared on every new entry.** `W65C816WidthTests` covers it, but confirm the test itself still runs and that no opcode was added with `Width.None` and an access micro-op.
- **No unguarded width test in variant-shared code.** `grep -n "_wide" src/SixtyFiveXX/Cpu.Exec.cs` — every hit must sit behind `TVariant.Variant != CpuVariant.W65C816 ||`, except arms for operations that appear in no 8-bit table (`Op.Adc816`, `Op.Sbc816`).
- **Bank-carry grouping complete.** Re-derive the exclusion set from Clark §5.1.2 rather than from the code: bank-0 confinement is direct page and the stack; everything else carries.
- **Cycle counts derive to §10.5's formulas** for every new opcode, at every `m`/`x`/`w`/`p` combination — the check 7b's final review ran against §5 and which found all sixteen matched.
- **Every new test genuinely discriminates.** For each, name the production line it fails against. A test that passes against the broken code is worse than no test, because it reads as coverage. 7b's reviews found two of these.
- **No test asserts only one of a Z/N pair** where both are set by the operation under test.
- **`ExpectedImplementedOpcodes` is 153** and its doc comment describes all five bumps.
- **No vector file, cache directory, or `.bin` is staged.** `git status --porcelain` before every commit.
- **`PublicSurfaceTests` untouched.**

- [ ] **Step 3: Fix every Critical and Important finding, each as its own commit**

Minor findings are triaged: fix, or record in the ledger with the reason for not fixing. Do not silently drop one.

- [ ] **Step 4: Update the README**

The variant status table gains the 65816's new opcode count, and the conformance section gains the new vector total. Check the README for any claim this phase made false — 7b's final review found three such claims, including one repeated verbatim in a published XML doc comment.

- [ ] **Step 5: Mark the spec verified**

In `docs/superpowers/specs/2026-08-03-65816-core-design.md`'s Phase 7c section, add a **Verified** paragraph under the Gate heading, in the same shape 7a and 7b already use: the measured counts, both TFMs, and any rule that has no vector coverage and is pinned only by a unit test.

- [ ] **Step 6: Run the full gate one last time, on an idle machine**

```bash
uptime                                    # load average must be low before the throughput gate
dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"
dotnet test tests/SixtyFiveXX.Conformance
dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category=Performance"
```

Expected: **505**, **1616**, both TFMs, and a throughput figure above the 50 MHz floor. If the throughput gate fails, check `uptime` before believing it.

- [ ] **Step 7: Commit, and record the phase in the ledger**

Append a phase-7c section to `.superpowers/sdd/progress.md` in the established form: per-task commits, what each gate measured, every defect the vectors found that review did not, every defect review found that the vectors could not, and the carry-forward list for 7c′.

**Gate:** zero Critical findings. Unit **505**, conformance **1616**, both TFMs, build with zero warnings, working tree clean.

---

## Carry-forward to phase 7c′

Written here so task 8 has something to check itself against rather than composing the list from memory:

- Everything in "Carry-forward hazards from phase 7b" above is still live and still untouched.
- `Sequences.RmwMiddle` is still `Unimplemented816`, and 7c′ is where datasheet Note 17's run-time `E`-dependent read-modify-write direction has to be solved — the one behaviour in this project that cannot be resolved at table-build time.
- `_wide` is latched at fetch. 7c′'s accumulator and implied forms need it; 7d's `PLP` and `RTI` are the first instructions that can change `m`/`x` mid-sequence, and the latch is the intended behaviour there.
- The two tripwires stay: `EmitAddressed816`'s `default:` throw, and `Harte816Tests.ExpectedImplementedOpcodes` (153 after this phase), which must be bumped per batch.
- `Op.AdcCmos`/`Op.SbcCmos` versus `Op.Adc816`/`Op.Sbc816`: whichever task 5 settled, 7c′'s `INC`/`DEC` and the shifts need the same width treatment and should follow the same shape.
