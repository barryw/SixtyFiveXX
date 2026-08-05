# Phase 7d — control flow, the stack, and the interrupts

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The last 44 opcodes of the 65816 — every branch, jump, call, return, push, pull, interrupt, block move and halt — certified per-cycle against the **full** 5,120,000 SingleStepTests vectors, taking the core to 256 of 256.

**Architecture:** The 65816's stack is sixteen bits wide in native mode and lives anywhere in bank 0, so this phase begins by giving the 65816 its own stack access path — `StackAddress816()`, `PushStack816()`, `PullStack816()` — and its own stack micro-ops, then builds every remaining opcode on top. The three defects carried since phase 7b all live in that same code and are cleared by the first code task. Control flow, the stack and the interrupts do not decompose into an addressing phase plus an access phase, so they are emitted by a hand-written `EmitControlFlow816` that switches on `Op`, exactly as `EmitStack` already does for the five 8-bit cores.

**Tech Stack:** C# 13, .NET 8 and .NET 10 (both must pass), xUnit, no NuGet dependencies in `src/`.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7d".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md`. **§9 is the cycle-by-cycle specification the addressing engine was built against.** §12 is phase 7c's (four gaps still open), §13 is phase 7c′'s. Task 1 of this plan adds **§14** — the research document already has §10–§13, so do not write "new §10"; phase 7c's plan made that mistake and needed correcting mid-phase. §3 records three places a source is wrong.

**Scope:** The 44 opcodes listed in "The opcode map for this phase" below, and nothing else. Explicitly **not** in scope: the 65816 disassembler, which throws `NotSupportedException` for the 65816's own addressing modes and is phase 7e. Do not add `Disassembler` arms.

## Global Constraints

- **The five 8-bit cores must not change.** **1,309** of the conformance tests are theirs and must stay at 1,309 passing on both TFMs. Any drift is a defect, not a trade.
- **Baselines measured on `main` at the phase-7c′ merge:** unit **528** with `--filter "Category!=Performance"` (529 unfiltered — the extra one is the throughput gate), conformance **1734**.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on with documentation generation, so **every public member needs an XML doc comment**.
- Both target frameworks must pass. Iterate with `-f net10.0`; run both before declaring a task done.
- **This phase adds no public API.** `PublicSurfaceTests.ExpectedPublicTypes` must be **unchanged**.
- **Vectors:** `SingleStepTests/65816`, `v1/{opcode:x2}.e.json` and `.n.json`, roughly 11 MB per opcode across both modes. This phase pulls about **500 MB**. Never commit a vector file.
- **Running the conformance suite: pass an explicit 600000 ms timeout on the Bash call.** The default is 120 seconds and the suite takes 3–6 minutes per framework; in phase 7c that default silently auto-backgrounded a run and stalled a task with everything uncommitted.
- **Commit before running any probe.** Reverting a deliberate mutation with `git checkout -- <file>` destroyed an implementer's uncommitted work once.
- **Restore probe files with `git checkout --`, never `mv file.bak file`** — the latter preserves an old mtime and defeats MSBuild's staleness detection, which cost a phase-7c task two phantom failures.
- **The `task-brief` script writes a fixed filename.** Rename each brief to `p7d-task-N-brief.md` immediately after extracting it, or it silently overwrites the previous phase's.
- Conventional Commits. Branch `phase7d-control-flow-stack-interrupts`, forked from `main` at the phase-7c′ merge. **Do not push `main` without `[skip ci]`** — a non-skipped push cuts a public nuget.org release.

## Established facts — verified, do not re-derive

- **`S8`'s setter already holds the emulation-mode invariant.** `S8 = value` writes `0x0100 | value` when the variant is the 65816 and `E` is set, so page one is enforced for every caller including the reset sequence. `FetchOpcode` re-forces `SH = $01`, `m = 1`, `x = 1`, `XH = YH = $00` at every instruction boundary in emulation mode. Native mode has no such forcing and `S` is a full sixteen bits.
- **`PcAddress()` is the program-stream address** — `(PBR << 16) | PC` on the 65816, bare `PC` elsewhere, guarded by a compile-time `TVariant.Variant` test so the five 8-bit cores fold to the bare `PC`. Every 65816 program read goes through it. `InternalCycle(address)` is its non-accessing counterpart: it records the address and calls `IBus.Internal` on the 65816 only.
- **`_data16` is the 65816's 16-bit value in flight**, and `_wide` is the latched operand width. `_wide`'s remarks name this phase specifically: it is latched at fetch and **must not** be turned into a live read of `_s.M`, because `PLP` and `RTI` can rewrite `P` mid-sequence.
- **`Flag.M` (0x20) is the same bit as `Flag.U`; `Flag.X` (0x10) is the same bit as `Flag.B`.** Every width test in variant-shared code must read `TVariant.Variant != CpuVariant.W65C816 || ...`, **variant test first**, so it folds to `if (true)` on the five 8-bit cores. Conformance cannot catch a violation: 0 of 10,000 `6502/a5` vectors have bit 5 of P clear. Phase 7b shipped this bug once; `UnusedFlagBitRegressionTests` exists for it. Members appearing in no 8-bit table need no guard.
- **`AddrMode.Stack` is this codebase's established mode for hand-written sequences** — the pushes, the pulls, `BRK`, and the absolute `JMP` and `JSR` all use it in `Opcodes6502.cs`, and `Disassembler.DecodeStack` recovers the length from the `Op`. The 65816 follows the same convention.
- **`MicroOps.IsWriteCycle` is a static `bool[]` indexed by micro-op**, consulted by `IsWriteCycleNext()` on every tick of every core because RDY must never halt a write. Every new pushing micro-op must be added to it.
- **`BusPinsTests.EveryMicroOpHasAPinClassification`** fails on any new `MicroOp` that is neither given pins nor declared an internal cycle. `BusPins.Vpb` already exists and `Harte816Tests.BuildPinString` already renders it, but no cycle in this repository has ever asserted it.
- **The two width tripwires do not catch a uniformly-wrong `Width`**, and `Cpu`'s constructor never calls `Reset()`, so `P == $00` and both width flags read clear by default. **Any test meaning to discriminate `m` from `x` must set them to opposed values explicitly.**
- **`W65C816WidthTests` asserts set equality** between "declares a `Width`" and "reaches one of `ReadExec816`, `ExecWrite816`, `ImmExec816`, `RmwRead816`". **Every opcode in this phase declares `Width.None`** and tests `_s.M`/`_s.XFlag` inside its own arm — the doctrine phase 7c′ established for the implied forms. `Width` keeps its precise meaning: *the operand fetched from memory is 16 bits.* Do not add a member to `Width` and do not add a new micro-op to that test's list.
- **An instruction that runs one cycle short surfaces as `UndefinedOpcodeException: Undefined opcode $00 at $<garbage>`** — the harness ticks past the end and fetches whatever follows. Nothing in that message names the real defect. Suspect a cycle count, not a table entry.
- **Note 17's generalisation, measured in phase 7c′:** the read-modify-write middle cycle's native and emulation forms are pin-identical and differ only in `RWB`. **Check Table 5-7's pin columns rather than inferring pins from a cycle's apparent purpose.** Sixteen vector failures one character wide established this.

## The three carried defects — all cleared by task 2

Carried since phase 7b and deferred every phase because no opcode reached them. **None is optional.**

1. **`MicroOp.PullP` masks `~Flag.B`, which is also `~Flag.X`.** On a native-mode 65816 that makes `PLP` and `RTI` clear the index-width flag as a side effect. Cleared by giving `Op.Plp` a 65816 branch and by the 65816 never reaching `MicroOp.PullP`; the shared micro-op is **left exactly as it is**, because it is correct for the five 8-bit cores.
2. **`ImpliedExec`, `FetchAddrHiX`/`Y`, the branch micro-ops, `JmpAbs`, `BrkPad`, `IntDummy` and every stack micro-op compute a bare 16-bit `PC` and/or `0x0100 + S8`.** Cleared by the 65816 reaching none of them, and **proved** by a new reachability test rather than by inspection.
3. **The 65816 IRQ sequence mutates `S` and memory before `Unimplemented816` throws.** Cleared by giving the table a per-variant interrupt section; the 65816's is a lone `Unimplemented816` in task 2, which throws on its first cycle before touching anything, and becomes the real sequence in task 3.

## File Structure

| File | Responsibility |
| --- | --- |
| `docs/superpowers/research/2026-08-03-65816-reference-sources.md` | Modify (task 1). New §14. |
| `src/SixtyFiveXX/AddrMode.cs` | Modify. `RelativeLong`, `BlockMove`, `AbsoluteIndirectLong`. |
| `src/SixtyFiveXX/Op.cs` | Modify. `Phb` `Phd` `Phk` `Plb` `Pld` `Cop` `Wdm` `Mvn` `Mvp` `Brl` `Jml` `Jsl` `Rtl` `Pea` `Pei` `Per`. |
| `src/SixtyFiveXX/MicroOp.cs` | Modify. Every new micro-op, plus its write, pin and internal-cycle classification. |
| `src/SixtyFiveXX/MicroOpTable.cs` | Modify. `EmitControlFlow816`, the per-variant interrupt section, the `Emit816` routing branch. |
| `src/SixtyFiveXX/Cpu.cs` | Modify. The stack helpers, the vector selector, and every new micro-op case. |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Modify. Every new `Exec` arm. |
| `src/SixtyFiveXX/Opcodes65C816.cs` | Modify, once per opcode task. 44 new entries; 212 → 256. |
| `tests/SixtyFiveXX.Tests/W65C816StackTests.cs` | **Create** (task 2). The stack plumbing, the pushes and pulls, and defect 1. |
| `tests/SixtyFiveXX.Tests/W65C816ReachabilityTests.cs` | **Create** (task 2). Defect 2's proof. |
| `tests/SixtyFiveXX.Tests/W65C816InterruptTests.cs` | **Create** (task 3). Vectors, the `PBR` push, `VPB`. |
| `tests/SixtyFiveXX.Tests/W65C816BlockMoveTests.cs` | **Create** (task 4). `MVN`/`MVP`. |
| `tests/SixtyFiveXX.Tests/WaiStpTests.cs` | Modify (task 5). The 65816's `WAI`/`STP`, alongside the WDC 65C02's. |
| `tests/SixtyFiveXX.Tests/W65C816ControlFlowTests.cs` | **Create** (task 6, appended by tasks 7–8). Branches, jumps, calls, returns, `PEA`/`PEI`/`PER`. |
| `tests/SixtyFiveXX.Tests/W65C816StateTests.cs` | Modify (task 9). Delete `UnimplementedOpcode_Throws`. |
| `tests/SixtyFiveXX.Tests/MicroOpTableTests.cs` | Modify (task 9). The all-tables-full tripwire that replaces it. |
| `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` | Modify, once per opcode task. `ExpectedImplementedOpcodes`. |

## The opcode map for this phase

Listed once, here, so every task can be checked against one place.

| Group | Bytes | Task |
| --- | --- | --- |
| Pushes | `$48` PHA, `$08` PHP, `$DA` PHX, `$5A` PHY, `$8B` PHB, `$0B` PHD, `$4B` PHK | 2 |
| Pulls | `$68` PLA, `$28` PLP, `$FA` PLX, `$7A` PLY, `$AB` PLB, `$2B` PLD | 2 |
| Interrupts | `$00` BRK, `$02` COP, `$42` WDM | 3 |
| Block move | `$54` MVN, `$44` MVP | 4 |
| Halt | `$CB` WAI, `$DB` STP | 5 |
| Branches | `$10` BPL, `$30` BMI, `$50` BVC, `$70` BVS, `$90` BCC, `$B0` BCS, `$D0` BNE, `$F0` BEQ, `$80` BRA, `$82` BRL | 6 |
| Jumps | `$4C` JMP abs, `$6C` JMP (abs), `$7C` JMP (abs,X), `$5C` JML long, `$DC` JML [abs] | 7 |
| Calls | `$20` JSR abs, `$FC` JSR (abs,X), `$22` JSL long | 7 |
| Returns | `$40` RTI, `$60` RTS, `$6B` RTL | 7 |
| Stack, address | `$F4` PEA, `$D4` PEI, `$62` PER | 8 |

`$54` is MVN and `$44` is MVP — the **destination** bank byte comes first in the instruction stream for both, which is the opposite of the order the mnemonic's operands are written in most assemblers. Task 1 confirms this against a source; do not take it from here.

---

### Task 1: Research §14 — everything this phase must not guess

**No code.** Phase 7c's and 7c′'s equivalent tasks are why 3,600,000 vectors went green on their first run with no cycle count tuned against a failure. This repeats it, over a larger surface than either.

**Files:**
- Modify: `docs/superpowers/research/2026-08-03-65816-reference-sources.md` (append §14)

**Interfaces:**
- Produces: research §14, cited by section number from tasks 2–8. §14.1 the stack, §14.2 the interrupts, §14.3 the block moves, §14.4 the halts, §14.5 the branches, §14.6 the jumps, calls and returns, §14.7 `PEA`/`PEI`/`PER`, §14.8 a cycle formula for all 44.

**Sources**, fetched rather than recalled:
- Clark, "65C816 Opcodes", via the GitHub mirror (6502.org 404s non-browser agents): `https://raw.githubusercontent.com/6502org/6502.org/main/public/tutorials/65c816opcodes.html`
- WDC W65C816S datasheet: `https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf`, Table 5-7 pp. 36–42, and §2.8.
- A vector file, read directly, for §14.3 and §14.4 — those two questions are answered by looking, not by reasoning.

**The honesty rule.** Where a source is silent, **record that it is silent, in those words.** Do not fill a gap from memory, do not infer 65816 behaviour from the 6502 or 65C02 without saying that is what you are doing, and do not write a number you cannot cite. §12 records four such gaps and §13 records two; every one of them was useful, and the one time a phase guessed, the vectors disagreed.

Two search traps, both of which have cost a revision: **Clark names flags by letter far more often than by name — search `d flag`, not `decimal`** — and the document is form-feed paginated, so a literal phrase can split across a blank line and fail to match.

- [ ] **Step 1: Read §9, §12 and §13 so the new section matches their notation**

§9 gives per-mode blocks: cycle number, VDA/VPA pair, address-bus expression, data-bus contents, and the note gating any conditional cycle. §13 shows how a measured result is recorded versus a cited one, and §13.1 records the Note 17 pin generalisation this phase should inherit. §14 must read alongside all three.

- [ ] **Step 2: Settle §14.1 — the stack**

Transcribe Table 5-7's rows for the pushes and the pulls, in §9's format, and answer each of these outright:

- Does native-mode `S` wrap within bank 0, or can a push at `S == $0000` reach bank 1? (The `HighByteAddressBank0` helper already assumes bank-0 confinement for the stack, citing Clark §5.1.2. Confirm or refute.)
- Are `PHA`/`PHX`/`PHY`/`PHD`/`PEA`/`PEI`/`PER` pushed **high byte first**, and are the pulls **low byte first**?
- What does `PHP` push for bits 4 and 5 in each mode, and what does `PLP` load into them in each mode?
- Does `PLP` setting `x = 1` force `XH = YH = $00` immediately, as `SEP` does?
- Cycle counts for all thirteen: which are `4-m`, `4-x`, flat 3, flat 4, flat 5.
- Which cycles are internal (`VDA = VPA = 0`, value `null`), and at what address each internal cycle drives.

- [ ] **Step 3: Settle §14.2 — the interrupts**

A table of the four sequences — `BRK`, `COP`, `IRQ`, `NMI` — in both modes:

- The vector address for each, in each mode. State each as a number.
- Whether `PBR` is pushed, and whether it is cleared before the handler runs.
- What the pushed `P` holds in bit 4 in each mode.
- Whether `D` is cleared, and whether `I` is set, and in which cycle relative to the push.
- Which cycles assert `VPB`, and whether any assert `VDA` at the same time.
- The total cycle count in each mode.
- Whether the NMI-hijack anomaly the NMOS cores have exists on this part at all.

`WDM`'s length and cycle count belong here too: it is a reserved two-byte no-operation, and what its second byte does — fetched or not — decides whether it is two cycles or three.

- [ ] **Step 4: Settle §14.3 — `MVN` and `MVP`, by reading a vector file**

Answer from the datasheet **and** from `$54.n.json`:

- The per-byte cycle count and the full per-cycle bus sequence.
- Which register holds the count, whether the count is bytes or bytes-minus-one, and which registers are incremented or decremented, in which direction, for each of the two.
- Whether `DBR` is written from the operand, and which operand byte.
- Whether `PC` is rewound, and by how much.
- **What one vector file actually contains** — one iteration or the whole move. Read the first vector's `cycles` array and its initial and final states, and record the observation as a measurement, in §12's style for a measured result.

- [ ] **Step 5: Settle §14.4 — `WAI` and `STP`, by reading a vector file**

Read `$CB.n.json` and `$DB.n.json`. Record how many cycles a vector contains, what the final state is, and whether the vector set models the hold at all. This decides whether tasks 5's opcodes can be certified by vectors or only by unit tests, and that answer must be known before the task is dispatched.

- [ ] **Step 6: Settle §14.5 — the branches**

- Is the taken-branch page-cross cycle emulation-mode-only? Quote the note.
- `BRL`'s length, cycle count, and whether it has any conditional cycle.
- Does a taken branch's displacement add wrap within the bank, leaving `PBR` unchanged?

- [ ] **Step 7: Settle §14.6 — the jumps, the calls and the returns**

- The bank each jump form reads its pointer from: `JMP (abs)`, `JMP (abs,X)`, `JML [abs]`.
- Confirm the 65816 does **not** reproduce the NMOS `JMP ($xxFF)` page-wrap bug, with a citation.
- `JSR (abs,X)`'s cycle order, which pushes before it has finished forming its address.
- What each of `JSR`, `JSL` pushes, and whether the pushed address is the last byte of the instruction or the next one.
- How many bytes `RTS`, `RTL` and `RTI` each pull in each mode, and which of them add one to the pulled value.
- Whether `RTI` pulls `PBR` in native mode, and whether it pulls `P` before or after the return address.

- [ ] **Step 8: Settle §14.7 — `PEA`, `PEI`, `PER`**

Their operand sources, their cycle counts, and confirmation that all three push two bytes regardless of `m`. `PER`'s displacement is relative to something — say what, exactly.

- [ ] **Step 9: Settle §14.8 — a cycle formula for every one of the 44**

Use §5's format and symbols (`m`, `x`, `w` for the direct-page penalty, `p` for a page cross, `e` where a formula differs by mode). Every opcode in the map above gets a row.

- [ ] **Step 10: Commit**

```bash
git add docs/superpowers/research/2026-08-03-65816-reference-sources.md
git commit -m "docs: research §14, the facts phase 7d must not guess"
```

**Gate:** `git diff --stat main -- src tests` empty. Every claim carries a named source or an explicit statement that the sources are silent. §14.3 and §14.4 each record what a vector file was observed to contain. The section is numbered **§14**, not §10.

---

### Task 2: The stack plumbing, the three carried defects, and the thirteen pushes and pulls

13 opcodes, and the code every remaining task is built on.

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816StackTests.cs`, `tests/SixtyFiveXX.Tests/W65C816ReachabilityTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (212 → 225)

**Interfaces:**
- Produces, and every later task consumes:
  - `private int StackAddress816()` — the address the next stack access drives.
  - `private void PushStack816(byte value)` — write at `StackAddress816()`, then move `S` down.
  - `private byte PullStack816()` — move `S` up, then read at `StackAddress816()`.
  - `MicroOp.StackPushInternal816`, `MicroOp.PushHigh816`, `MicroOp.PushLow816`, `MicroOp.StackPullInternal816`, `MicroOp.PullLow816`, `MicroOp.PullHigh816`.
  - `MicroOpTable.EmitControlFlow816(List<MicroOp>, OpcodeInfo)` — the hand-written 65816 sequence emitter, extended by tasks 3–8.
  - `Op.Phb`, `Op.Phd`, `Op.Phk`, `Op.Plb`, `Op.Pld`.

- [ ] **Step 1: Read research §14.1 and check the sequences below against it**

The shapes in step 5 are what this plan expects. **If §14.1 disagrees on a cycle count, an internal cycle's address, or a push order, follow §14.1 and record the deviation in the task report.** Do not tune anything to make a vector pass; if a vector fails, report the opcode, the vector name and the expected-versus-actual line, and stop.

- [ ] **Step 2: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816StackTests.cs`:

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's stack: sixteen bits wide in native mode, anywhere in bank 0, and forced into
/// page one in emulation mode. Every assertion here discriminates a rule that no vector in
/// this phase's own files necessarily reaches.
/// </summary>
public class W65C816StackTests
{
    /// <summary>
    /// A native-mode push lands at S itself, not at $0100 + SL. With S outside page one, the
    /// eight-bit formula and the sixteen-bit one give different addresses, which is the whole
    /// point of the plumbing this task adds.
    /// </summary>
    [Fact]
    public void NativePush_LandsAtTheFullSixteenBitStackPointer()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;             // 8-bit accumulator
        cpu.State.XFlag = false;        // opposed, so a width read from x would be visible
        cpu.State.S = 0x1FFF;
        cpu.State.A = 0x0042;

        cpu.Step();

        Assert.Equal(0x42, ram[0x001FFF]);
        Assert.Equal(0x1FFE, cpu.State.S);
    }

    /// <summary>
    /// An emulation-mode push stays in page one and wraps within it: S = $00 pushes to $0100
    /// and leaves S at $01FF, never $00FF and never $0000.
    /// </summary>
    [Fact]
    public void EmulationPush_WrapsWithinPageOne()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x0100;
        cpu.State.A = 0x0042;

        cpu.Step();

        Assert.Equal(0x42, ram[0x000100]);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// A 16-bit PHA pushes the high byte first, so the low byte ends up at the lower address.
    /// </summary>
    [Fact]
    public void SixteenBitPush_PushesHighByteFirst()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;            // 16-bit accumulator
        cpu.State.XFlag = true;         // opposed
        cpu.State.S = 0x1FFF;
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x12, ram[0x001FFF]);
        Assert.Equal(0x34, ram[0x001FFE]);
        Assert.Equal(0x1FFD, cpu.State.S);
    }

    /// <summary>
    /// Defect 1, carried since phase 7b. In native mode bit 4 of P is the index-width select,
    /// not the break flag, so PLP must load it verbatim. The shared MicroOp.PullP masks
    /// ~Flag.B — the same bit — and would silently clear x here.
    /// </summary>
    [Fact]
    public void Plp_NativeMode_DoesNotClearTheIndexWidthFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x28;             // PLP
        ram[0x1FFF] = Flag.X;           // x set, everything else clear

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.S = 0x1FFE;

        cpu.Step();

        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x1FFF, cpu.State.S);
    }

    /// <summary>
    /// PLP that sets x must narrow the index registers the same instant SEP does, or a
    /// following indexed instruction reads a high byte that cannot exist at x = 1.
    /// </summary>
    [Fact]
    public void Plp_SettingIndexWidth_NarrowsXAndY()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x28;             // PLP
        ram[0x1FFF] = Flag.X;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.S = 0x1FFE;
        cpu.State.X = 0xBEEF;
        cpu.State.Y = 0xCAFE;

        cpu.Step();

        Assert.Equal(0x00EF, cpu.State.X);
        Assert.Equal(0x00FE, cpu.State.Y);
    }

    /// <summary>
    /// PHK pushes the program bank, PHB the data bank, and PLB loads the data bank and sets
    /// N and Z from it as an eight-bit result. One byte each, regardless of m and x.
    /// </summary>
    [Fact]
    public void Phk_Phb_Plb_MoveOneByteEach()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x4B;           // PHK
        ram[0x120001] = 0x8B;           // PHB
        ram[0x120002] = 0xAB;           // PLB

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;          // Make() defaults PC to $C000; these opcodes are at $120000
        cpu.State.DBR = 0x80;
        cpu.State.S = 0x1FFF;

        cpu.Step();                      // PHK
        Assert.Equal(0x12, ram[0x001FFF]);

        cpu.Step();                      // PHB
        Assert.Equal(0x80, ram[0x001FFE]);

        cpu.Step();                      // PLB — pulls the $80 it just pushed
        Assert.Equal(0x80, cpu.State.DBR);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// PHD and PLD move all sixteen bits of the direct register whatever m and x say, and
    /// PLD's flags come from the sixteen-bit result.
    /// </summary>
    [Fact]
    public void Phd_Pld_AreAlwaysSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0B;             // PHD
        ram[0xC001] = 0x2B;             // PLD

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;             // both narrow — PHD must ignore them
        cpu.State.XFlag = true;
        cpu.State.S = 0x1FFF;
        cpu.State.DP = 0x8000;

        cpu.Step();                      // PHD
        Assert.Equal(0x80, ram[0x001FFF]);
        Assert.Equal(0x00, ram[0x001FFE]);
        Assert.Equal(0x1FFD, cpu.State.S);

        cpu.State.DP = 0x0000;
        cpu.Step();                      // PLD
        Assert.Equal(0x8000, cpu.State.DP);
        Assert.True(cpu.State.N);
    }
}
```

- [ ] **Step 3: Write the reachability test — defect 2's proof**

Create `tests/SixtyFiveXX.Tests/W65C816ReachabilityTests.cs`:

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Defect 2, carried since phase 7b: a set of micro-ops compute a bare sixteen-bit PC or a
/// bare $0100 + SL, both of which are wrong on a 65816 — program reads are at PBR,PC and the
/// native-mode stack is a full sixteen bits anywhere in bank 0.
/// <para>
/// The fix is that the 65816 reaches none of them. That is asserted here rather than by
/// inspection, because inspection is what let the defect survive four phases.
/// </para>
/// </summary>
public class W65C816ReachabilityTests
{
    /// <summary>
    /// Every micro-op below computes an address the 65816 cannot use. The list is produced
    /// mechanically, not from memory: every <c>case MicroOp.X</c> in <c>Cpu.Execute</c> whose
    /// body passes a bare <c>_s.PC</c> to <c>ReadBus</c>/<c>WriteBus</c>, or contains the
    /// literal <c>0x0100</c>.
    /// </summary>
    private static readonly MicroOp[] EightBitOnly =
    [
        MicroOp.ImpliedExec, MicroOp.ImpliedDummy, MicroOp.ImmExec,
        MicroOp.FetchAddrHiX, MicroOp.FetchAddrHiY,
        MicroOp.BranchFetch, MicroOp.BranchTaken, MicroOp.BranchFixup,
        MicroOp.BitBranchFetch, MicroOp.BitBranchFixup,
        MicroOp.JmpAbs, MicroOp.JsrFinish, MicroOp.JmpIndLo, MicroOp.JmpIndHi,
        MicroOp.JmpIndBugDummy, MicroOp.PtrJmpHi, MicroOp.JmpAbsXDummy,
        MicroOp.NopAbsExtraRead,
        MicroOp.BrkPad, MicroOp.IntDummy,
        MicroOp.StackDummyRead, MicroOp.StackDummyReadInc, MicroOp.StackDummyReadDec,
        MicroOp.PushPch, MicroOp.PushPcl, MicroOp.PullPcl, MicroOp.PullPch,
        MicroOp.RtsFinish, MicroOp.PullP, MicroOp.Push, MicroOp.Pull,
        MicroOp.PushPBrk, MicroOp.PushPInt, MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos,
        MicroOp.WaiHold, MicroOp.StpHold, MicroOp.JamHold,
    ];

    [Fact]
    public void No816SequenceReachesAnEightBitOnlyMicroOp()
    {
        var table = MicroOpTable.For<W65C816Variant>();
        var banned = new HashSet<MicroOp>(EightBitOnly);

        for (var opcode = 0; opcode < 256; opcode++)
        {
            for (var i = table.Entry[opcode]; table.Ops[i] != MicroOp.End; i++)
            {
                Assert.False(banned.Contains(table.Ops[i]),
                    $"${opcode:X2} {table.Info[opcode].Mnemonic} reaches {table.Ops[i]}, " +
                    "which computes a bare 16-bit PC or a bare $0100 + SL.");
            }
        }
    }

    [Fact]
    public void NeitherThe816InterruptNorItsResetSectionReachesOne()
    {
        var table = MicroOpTable.For<W65C816Variant>();
        var banned = new HashSet<MicroOp>(EightBitOnly);

        foreach (var (name, entry) in new[]
                 {
                     ("IrqEntry", table.IrqEntry), ("ResetEntry", table.ResetEntry),
                 })
        {
            for (var i = entry; table.Ops[i] != MicroOp.End; i++)
            {
                Assert.False(banned.Contains(table.Ops[i]),
                    $"{name} reaches {table.Ops[i]}, which computes a bare 16-bit PC " +
                    "or a bare $0100 + SL.");
            }
        }
    }
}
```

**Derive the list rather than trusting it.** Before running, produce it yourself:

```bash
grep -n "case MicroOp\.\|ReadBus(_s\.PC\|WriteBus(_s\.PC\|0x0100" src/SixtyFiveXX/Cpu.cs
```

Every `case` whose body contains one of the latter three hits belongs in the array. **Remove any entry the grep does not justify** — a micro-op that already calls `PcAddress()` is bank-aware and belongs nowhere near this list — and add any it does. If a micro-op in the list turns out to be reachable from an *existing* 65816 sequence, that is a defect this test has just found: report it, and do not delete the entry to make the test green.

- [ ] **Step 4: Run both new files and watch them fail**

```bash
dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~W65C816StackTests|FullyQualifiedName~W65C816ReachabilityTests"
```

Expected: **FAIL**. The stack tests throw `UndefinedOpcodeException` for `$48`; the reachability tests fail on the interrupt and reset sections, which are the shared ones today.

**Note the reset failure.** `ResetEntry` uses `StackDummyReadDec`, which drives `0x0100 + S8`. In emulation mode that is right by accident, because `S8`'s setter forces page one — but a native-mode reset does not exist (reset always enters emulation mode), so this may be correct rather than defective. **Read `Cpu.Reset()` before deciding.** If reset genuinely cannot run outside emulation mode, give the 65816 its own reset section using `StackDummyReadDec816` anyway — the assertion is about which address is *computed*, and a formula that is right only because another invariant happens to hold is exactly the class of defect this phase is clearing.

- [ ] **Step 5: Add the stack helpers to `Cpu.cs`**

Next to `PcAddress()`:

```csharp
    /// <summary>
    /// The address the next stack access drives, on the 65816. Native mode puts the stack
    /// anywhere in bank 0 and uses all sixteen bits of <c>S</c>; emulation mode confines it to
    /// page one, which <see cref="S8"/>'s setter and <see cref="FetchOpcode"/> hold as an
    /// invariant rather than something this method re-imposes. Both cases are therefore the
    /// same expression.
    /// </summary>
    /// <remarks>
    /// 65816 only. The five 8-bit cores keep their own <c>0x0100 + S8</c> in each stack
    /// micro-op — those micro-ops are correct for them and are not reached from any 65816
    /// sequence, which <c>W65C816ReachabilityTests</c> asserts.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int StackAddress816() => _s.S;

    /// <summary>Writes one byte at <see cref="StackAddress816"/>, then moves <c>S</c> down.</summary>
    private void PushStack816(byte value)
    {
        WriteBus(StackAddress816(), value);
        if (_s.E) S8--; else _s.S--;
    }

    /// <summary>Moves <c>S</c> up, then reads one byte at <see cref="StackAddress816"/>.</summary>
    /// <remarks>
    /// The order is the hardware's and matters: a push writes before decrementing and a pull
    /// increments before reading, so <c>S</c> always points at the next free byte.
    /// <c>S8++</c> rather than <c>_s.S++</c> in emulation mode is what keeps the wrap inside
    /// page one — <c>_s.S++</c> at <c>$01FF</c> would produce <c>$0200</c>.
    /// </remarks>
    private byte PullStack816()
    {
        if (_s.E) S8++; else _s.S++;
        return ReadBus(StackAddress816());
    }

    /// <summary>
    /// True when the stack operation now executing moves sixteen bits. Read from the live flag
    /// rather than from <c>_wide</c>: these opcodes declare <see cref="Width.None"/>, because
    /// <c>Width</c> means "the operand fetched from memory is 16 bits" and that is what keeps
    /// <c>W65C816WidthTests</c>'s set equality meaningful.
    /// </summary>
    private bool StackIsWide() => _op switch
    {
        Op.Pha or Op.Pla => !_s.M,
        Op.Phx or Op.Plx or Op.Phy or Op.Ply => !_s.XFlag,
        Op.Phd or Op.Pld => true,
        _ => false,             // PHP, PHB, PHK, PLB — always one byte
    };
```

- [ ] **Step 6: Add the six micro-ops to `MicroOp.cs`, with their classification**

Declare `StackPushInternal816`, `PushHigh816`, `PushLow816`, `StackPullInternal816`, `PullLow816`, `PullHigh816`. Then:

- `PushHigh816` and `PushLow816` go in `BuildWriteTable`'s array. **RDY must never halt them.**
- `PushHigh816`, `PushLow816`, `PullLow816`, `PullHigh816` are `BusPins.Vda` — a stack access is a data access, never a program one.
- `StackPushInternal816` and `StackPullInternal816` are `BusPins.None` **and** go in `BuildInternalCycleTable`. Check research §14.1's pin columns before writing this: phase 7c′ learned the hard way that a cycle's apparent purpose does not predict its pins.

- [ ] **Step 7: Add the micro-op cases to `Cpu.cs`**

```csharp
            case MicroOp.StackPushInternal816:
                // Cycle 2 of every push: an internal cycle at PBR,PC+1 — research §14.1. The
                // value is formed here, before the first write, so both write cycles are pure.
                InternalCycle(PcAddress());
                Exec();
                if (!StackIsWide()) _mpc++;      // skip PushHigh816: one byte only
                break;

            case MicroOp.PushHigh816:
                PushStack816((byte)(_data16 >> 8));
                break;

            case MicroOp.PushLow816:
                PushStack816((byte)_data16);
                break;

            case MicroOp.StackPullInternal816:
                // Cycle 3 of every pull, mirroring the eight-bit cores' StackDummyReadInc
                // position — but an internal cycle here, and it does NOT move S: PullStack816
                // increments before it reads.
                InternalCycle(StackAddress816());
                break;

            case MicroOp.PullLow816:
                _data16 = PullStack816();
                if (StackIsWide()) break;
                Exec();
                EndInstruction();
                break;

            case MicroOp.PullHigh816:
                _data16 |= (ushort)(PullStack816() << 8);
                Exec();
                break;
```

- [ ] **Step 8: Add the `Exec` arms to `Cpu.Exec.cs`**

Every one of these appears in no 8-bit table except `Pha`/`Php`/`Pla`/`Plp`/`Phx`/`Phy`/`Plx`/`Ply`, which do — so those eight need the variant guard and the five new ones do not.

```csharp
            // ---- 65816 pushes. Each leaves the value in _data16; the push micro-ops move it.
            case Op.Pha when TVariant.Variant == CpuVariant.W65C816:
                _data16 = _s.A;
                break;

            case Op.Phx when TVariant.Variant == CpuVariant.W65C816:
                _data16 = IndexX();
                break;

            case Op.Phy when TVariant.Variant == CpuVariant.W65C816:
                _data16 = IndexY();
                break;

            case Op.Php when TVariant.Variant == CpuVariant.W65C816:
                // Emulation mode pushes the 6502's B and U bits set, as every eight-bit core
                // does. In native mode bits 4 and 5 are x and m and are pushed as they stand.
                // Research §14.1 states both halves; do not infer the native half from the
                // emulation one.
                _data16 = _s.E ? (ushort)(_s.P | Flag.B | Flag.U) : _s.P;
                break;

            case Op.Phb:
                _data16 = _s.DBR;
                break;

            case Op.Phk:
                _data16 = _s.PBR;
                break;

            case Op.Phd:
                _data16 = _s.DP;
                break;

            // ---- 65816 pulls. _data16 holds what was pulled, narrowed already when 8-bit.
            case Op.Pla when TVariant.Variant == CpuVariant.W65C816:
                if (_s.M) { A8 = (byte)_data16; SetZN(A8); }
                else { _s.A = _data16; SetZN16(_s.A); }
                break;

            case Op.Plx when TVariant.Variant == CpuVariant.W65C816:
                if (_s.XFlag) { X8 = (byte)_data16; SetZN(X8); }
                else { _s.X = _data16; SetZN16(_s.X); }
                break;

            case Op.Ply when TVariant.Variant == CpuVariant.W65C816:
                if (_s.XFlag) { Y8 = (byte)_data16; SetZN(Y8); }
                else { _s.Y = _data16; SetZN16(_s.Y); }
                break;

            case Op.Plp when TVariant.Variant == CpuVariant.W65C816:
                // Defect 1. Nothing is masked: in native mode bit 4 is x and bit 5 is m, and
                // the shared MicroOp.PullP's ~Flag.B would clear the index-width flag. In
                // emulation mode the two bits carry no architectural meaning and FetchOpcode
                // re-forces them at the next boundary; forcing them here as well keeps the
                // state consistent for anything that reads P before then.
                _s.P = (byte)_data16;
                if (_s.E) { _s.M = true; _s.XFlag = true; }
                // Setting x narrows the index registers the same instant SEP does — see
                // Op.Sep's arm, which this mirrors deliberately rather than duplicating a rule.
                if (_s.XFlag) { _s.X &= 0x00FF; _s.Y &= 0x00FF; }
                break;

            case Op.Plb:
                _s.DBR = (byte)_data16;
                SetZN(_s.DBR);
                break;

            case Op.Pld:
                _s.DP = _data16;
                SetZN16(_s.DP);
                break;
```

**Check `Op.Sep`'s arm before writing the last two lines of `Op.Plp`** and match whatever it actually does. If it narrows differently, `PLP` is the one that is wrong, not `SEP`.

- [ ] **Step 9: Add `EmitControlFlow816` and route to it**

In `MicroOpTable.Emit816`, ahead of the `EmitAddressed816` fall-through:

```csharp
        // Control flow, the stack and the interrupts do not decompose into an addressing phase
        // plus an access phase — the same reason EmitStack exists for the five 8-bit cores.
        // Routed by mode here and switched by operation there.
        if (info.Mode == AddrMode.Stack)
        {
            EmitControlFlow816(ops, info);
            return;
        }
```

And the emitter itself, which tasks 3–8 extend:

```csharp
    /// <summary>
    /// The 65816's hand-written sequences: the pushes and pulls, the calls and returns, the
    /// interrupts, and the three stack-addressing pushes. Switched on
    /// <see cref="OpcodeInfo.Operation"/> rather than on the mode, because
    /// <see cref="AddrMode.Stack"/> covers instructions of one, two, three and four bytes and
    /// only the operation tells them apart — the same shape <see cref="EmitStack"/> has for
    /// the eight-bit cores, and the same shape <c>Disassembler.DecodeStack</c> relies on.
    /// </summary>
    private static void EmitControlFlow816(List<MicroOp> ops, OpcodeInfo info)
    {
        switch (info.Operation)
        {
            // Cycle 2 is an internal cycle at PBR,PC+1 that also forms the value; the high
            // slot is skipped when the push is one byte wide. Research §14.1.
            case Op.Pha or Op.Php or Op.Phx or Op.Phy or Op.Phb or Op.Phd or Op.Phk:
                ops.AddRange([
                    MicroOp.StackPushInternal816, MicroOp.PushHigh816, MicroOp.PushLow816,
                ]);
                break;

            // Two internal cycles, then the pull; PullLow816 ends the instruction when the
            // pull is one byte wide, so the high slot costs nothing.
            case Op.Pla or Op.Plp or Op.Plx or Op.Ply or Op.Plb or Op.Pld:
                ops.AddRange([
                    MicroOp.ImpliedInternal816, MicroOp.StackPullInternal816,
                    MicroOp.PullLow816, MicroOp.PullHigh816,
                ]);
                break;

            default:
                throw new InvalidOperationException(
                    $"{info.Mnemonic}: {info.Operation} has no 65816 control-flow sequence.");
        }
    }
```

- [ ] **Step 10: Give the 65816 its own interrupt section — defect 3**

In the `MicroOpTable` constructor, replace the unconditional shared section:

```csharp
        IrqEntry = (ushort)ops.Count;
        if (variant == CpuVariant.W65C816)
        {
            // Phase 7d task 3 replaces this with the real sequence — two vector sets, a PBR
            // push and VPB. Until then it throws on its FIRST cycle, before touching S or
            // memory: the shared section below pushed three bytes and moved S three times
            // before reaching seq.IntPushP's Unimplemented816, which meant a 65816 IRQ
            // corrupted the stack on the way to reporting that it was not implemented.
            ops.Add(MicroOp.Unimplemented816);
        }
        else
        {
            ops.AddRange([
                MicroOp.IntDummy,
                MicroOp.PushPch,
                MicroOp.PushPcl,
                seq.IntPushP,
                MicroOp.VectorLo,
                MicroOp.VectorHi,
            ]);
        }
        ops.Add(MicroOp.End);
```

Now `seq.IntPushP` is no longer read for the 65816, which was the only reason `NotYet816` existed. **Delete the `NotYet816` record and change `SequencesFor`'s `W65C816` arm to `Nmos`** — with a comment saying the 65816 never consults it and that the arm exists only because that switch deliberately refuses a silent default. Build. **If the build fails, the record is still read from somewhere; find the reader and record it in the task report rather than restoring the placeholder blindly.** The phase 7c′ spec claimed this was clearable once already and was wrong.

While here, check whether `Sequences.RmwMiddle` now has any reader at all. Record the answer either way.

- [ ] **Step 11: Add the five `Op` members and the thirteen table entries**

`Op.cs`, in the 65816 block:

```csharp
    /// <summary>
    /// Push and pull the data bank register, and push the program bank register. One byte
    /// each, regardless of <c>m</c> and <c>x</c>; <c>PLB</c> sets <c>N</c> and <c>Z</c> from
    /// the eight-bit value, and there is no <c>PLK</c> — the program bank is changed only by
    /// a long jump, a long call, a long return or an interrupt. 65816 only.
    /// </summary>
    Phb, Phk, Plb,

    /// <summary>
    /// Push and pull the direct register. Always sixteen bits — <c>D</c> has no narrow form —
    /// and <c>PLD</c>'s <c>N</c> and <c>Z</c> come from all sixteen. 65816 only.
    /// </summary>
    Phd, Pld,
```

`Opcodes65C816.cs`:

```csharp
        // The stack. All AddrMode.Stack — the mode this codebase uses for hand-written
        // sequences — and all Width.None: they fetch no operand from memory, so each arm
        // tests its own flag. PHP/PHB/PHK/PLB move one byte whatever m and x say; PHD/PLD
        // move two; PHA/PLA are sized by m and PHX/PHY/PLX/PLY by x.
        Set(0x48, "PHA", AddrMode.Stack, Op.Pha, Access.None);
        Set(0x08, "PHP", AddrMode.Stack, Op.Php, Access.None);
        Set(0xDA, "PHX", AddrMode.Stack, Op.Phx, Access.None);
        Set(0x5A, "PHY", AddrMode.Stack, Op.Phy, Access.None);
        Set(0x8B, "PHB", AddrMode.Stack, Op.Phb, Access.None);
        Set(0x0B, "PHD", AddrMode.Stack, Op.Phd, Access.None);
        Set(0x4B, "PHK", AddrMode.Stack, Op.Phk, Access.None);

        Set(0x68, "PLA", AddrMode.Stack, Op.Pla, Access.None);
        Set(0x28, "PLP", AddrMode.Stack, Op.Plp, Access.None);
        Set(0xFA, "PLX", AddrMode.Stack, Op.Plx, Access.None);
        Set(0x7A, "PLY", AddrMode.Stack, Op.Ply, Access.None);
        Set(0xAB, "PLB", AddrMode.Stack, Op.Plb, Access.None);
        Set(0x2B, "PLD", AddrMode.Stack, Op.Pld, Access.None);
```

Update the class `<remarks>`: 225 defined, 31 undefined, and name what the remaining 31 are.

- [ ] **Step 12: Run the unit suite, then the vectors, then both frameworks**

```bash
dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "Category!=Performance"
```

Then raise `Harte816Tests.ExpectedImplementedOpcodes` to **225** and run the conformance suite **with an explicit 600000 ms timeout**:

```bash
dotnet test tests/SixtyFiveXX.Conformance -f net10.0
```

Expected: conformance **1760** (1734 + 26). Then both frameworks.

**If a vector fails, do not tune anything.** Report the failing opcode, the vector name, and the expected-versus-actual line, and stop. Sixteen one-character pin failures in phase 7c′ turned out to be the most useful result that phase produced, and they were useful only because nobody adjusted a pin table to make them go away.

- [ ] **Step 13: Bump the README's 65816 opcode count and commit**

The README's support-matrix row was deliberately made count-free — leave it that way — but the 65816 section's prose count moves to 225. **Do this in the same commit**; skipping it once left the count doubly stale and cost a later task a correction.

```bash
git add -A
git commit -m "feat: 65816 stack plumbing, the thirteen pushes and pulls, and three carried defects"
```

**Gate:** conformance **1760**, unit **528 + the new tests**, both TFMs, 260,000 new vectors green, both reachability tests passing, `NotYet816` deleted or its surviving reader named in the report.

---

### Task 3: `BRK`, `COP`, `WDM`, and the 65816's own interrupt sequences

3 opcodes, and the phase's highest-risk mechanism.

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`, `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Op.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816InterruptTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (225 → 228)

**Interfaces:**
- Consumes: `PushStack816`, `StackAddress816`, `EmitControlFlow816` from task 2.
- Produces: `private int Vector816(Op reason)` — the one place native and emulation vector addresses are chosen; `MicroOp.VectorLo816`, `MicroOp.VectorHi816`, which task 7's `RTI` counterpart mirrors.
- Produces: `Op.Cop`, `Op.Wdm`.

- [ ] **Step 1: Read research §14.2 and transcribe its cycle table into the task report**

Before writing code, write out the four sequences — `BRK`, `COP`, `IRQ`, `NMI` — in both modes, cycle by cycle, from §14.2. If §14.2 leaves any of them silent, **stop and say so**; do not infer a 65816 interrupt sequence from the 6502's.

**Know before you start what the vectors can and cannot check.** §14.2 measured it: the SingleStepTests set is one file per opcode per mode and contains **no interrupt-line stimulus at all**, so `IRQ` and `NMI` have no vectors. `BRK` and `COP` share cycles 3–8 with Table 5-7's row 22a and those cycles *are* arbitrated by `$00` and `$02`'s files — but the two leading `IO` cycles at `PBR,PC`, the recognition timing, and the `NMI`/`IRQ` vector selection are certified by **unit tests only**. Budget for that in step 2 rather than discovering it at step 8. §14.2 also records that the sources are silent on whether the NMOS NMI-hijack anomaly exists on this part: **it must not be carried over from the 8-bit cores on the strength of their behaviour.** If you implement a hijack, cite something; if you do not, say why in the report.

- [ ] **Step 2: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816InterruptTests.cs`. Cover, at minimum:

```csharp
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's interrupts: two vector sets, a program-bank push that no eight-bit core has,
/// and the first cycles in this repository to assert VPB.
/// </summary>
public class W65C816InterruptTests
{
    /// <summary>
    /// A native-mode BRK pushes PBR, then the return address, then P, reads the native BRK
    /// vector, and clears PBR so the handler runs in bank 0.
    /// </summary>
    [Fact]
    public void NativeBrk_PushesProgramBank_AndTakesTheNativeVector()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x00;           // BRK
        ram[0x120001] = 0xEE;           // signature byte
        ram[0xFFE6] = 0x34;             // native BRK vector — confirm the address in §14.2
        ram[0xFFE7] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;
        cpu.State.S = 0x1FFF;

        cpu.Step();

        Assert.Equal(0x12, ram[0x001FFF]);      // PBR pushed first
        Assert.Equal(0x00, cpu.State.PBR);      // handler runs in bank 0
        Assert.Equal(0x1234, cpu.State.PC);
        Assert.True(cpu.State.I);
    }

    /// <summary>
    /// An emulation-mode BRK pushes no program bank and takes the eight-bit vector, so its
    /// stack footprint is three bytes rather than four.
    /// </summary>
    [Fact]
    public void EmulationBrk_PushesThreeBytes_AndTakesTheEmulationVector()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x00;             // BRK
        ram[0xC001] = 0xEE;
        ram[0xFFFE] = 0x34;
        ram[0xFFFF] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x01FF;

        cpu.Step();

        Assert.Equal(0x01FC, cpu.State.S);      // three bytes, not four
        Assert.Equal(0x1234, cpu.State.PC);
    }

    /// <summary>
    /// The two vector-read cycles assert VPB and no other core in this repository ever has.
    /// Asserted through the pin readback rather than through the vectors, because a unit test
    /// that fails in a second is worth more here than a 20,000-vector file that fails in five
    /// minutes.
    /// </summary>
    [Fact]
    public void TheVectorReadsAssertVectorPull()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x00;           // BRK
        ram[0x120001] = 0xEE;
        ram[0xFFE6] = 0x34;
        ram[0xFFE7] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;
        cpu.State.S = 0x1FFF;

        // Cycle-at-a-time rather than Step(), so every cycle's pins can be read — the idiom
        // PinTests.cs uses. The guard bounds the loop rather than trusting a cycle count this
        // test deliberately does not hard-code: research §14.2 owns that number, not this file.
        var pins = new List<BusPins>();
        cpu.Tick();
        pins.Add(cpu.LastPins);
        for (var guard = 0; cpu.State.PC != 0x1234 && guard < 16; guard++)
        {
            cpu.Tick();
            pins.Add(cpu.LastPins);
        }

        Assert.Equal(0x1234, cpu.State.PC);

        var vectorCycles = pins
            .Select((p, i) => (Pins: p, Index: i))
            .Where(x => (x.Pins & BusPins.Vpb) != 0)
            .Select(x => x.Index)
            .ToList();

        // Exactly two, and they are the last two: VPB is asserted on the vector reads and
        // nowhere else in the sequence.
        Assert.Equal(2, vectorCycles.Count);
        Assert.Equal(pins.Count - 2, vectorCycles[0]);
        Assert.Equal(pins.Count - 1, vectorCycles[1]);
    }
}
```

The vector addresses above are placeholders **only** in the sense that research §14.2 states the real ones — transcribe them from §14.2 before running, and if `$FFE6` is not the native `BRK` vector, the test is wrong, not the implementation.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL**, `UndefinedOpcodeException` for `$00`.

- [ ] **Step 4: Add the vector selector to `Cpu.cs`**

The existing `NmiVector`, `IrqVector` and `ResetVector` constants stay — they are the emulation-mode and eight-bit-core addresses. Add the native four as constants beside them, named from §14.2, and one selector:

```csharp
    /// <summary>
    /// The vector an interrupt reads, on the 65816. Native mode has its own four addresses;
    /// emulation mode reuses the eight-bit cores'. Research §14.2 — every address here is
    /// transcribed from that table, not derived from the 6502's.
    /// </summary>
    /// <remarks>
    /// One selector rather than a test inside each interrupt micro-op: the choice depends only
    /// on <c>E</c> and on which interrupt is being taken, both of which are known before the
    /// sequence starts, and keeping it in one place is what makes the four addresses reviewable
    /// against §14.2 as a block.
    /// </remarks>
    private int Vector816(Op reason) => ...;
```

- [ ] **Step 5: Add the interrupt micro-ops and the sequences**

New micro-ops, each classified in all three tables:

- `BrkPad816` — the signature-byte fetch. `BusPins.Vpa`.
- `PushPbr816` — native only; the sequence skips it in emulation mode.
- `PushPch816`, `PushPcl816` — writes; both go in `BuildWriteTable`.
- `PushPInt816` — the P push, which also sets `I` and clears `D` per §14.2. A write.
- `VectorLo816`, `VectorHi816` — `BusPins.Vda | BusPins.Vpb`. `VectorHi816` also clears `PBR`.

Then the `EmitControlFlow816` arms for `Op.Brk` and `Op.Cop`, and the 65816 branch of the constructor's interrupt section — replacing task 2's lone `Unimplemented816`. **Delete `MicroOp.Unimplemented816` once nothing emits it**, and confirm by building.

`WDM` is not an interrupt; it is a reserved two-byte no-operation. Give it its own arm and whatever cycle count §14.2 states.

- [ ] **Step 6: Point the hardware-interrupt dispatch at the right vector**

`FetchOpcode`'s `_intPoll` branch sets `_vector` to `NmiVector` or `IrqVector` unconditionally. On the 65816 it must use `Vector816`. Guard it with `TVariant.Variant == CpuVariant.W65C816` so the five 8-bit cores fold to exactly the code that is there today.

- [ ] **Step 7: Add the three table entries**

```csharp
        // Interrupts. BRK and COP are two-byte instructions whose second byte is fetched and
        // discarded; WDM is a reserved two-byte no-operation that WDC guarantees will never be
        // given a meaning on this part.
        Set(0x00, "BRK", AddrMode.Stack,   Op.Brk, Access.None);
        Set(0x02, "COP", AddrMode.Stack,   Op.Cop, Access.None);
        Set(0x42, "WDM", AddrMode.Implied, Op.Wdm, Access.None);
```

`WDM` is two bytes, so `AddrMode.Implied` is wrong for the disassembler that phase 7e will write. **If §14.2 confirms it consumes a second byte, give it `AddrMode.ImmediateByte`** — an existing mode meaning exactly "one operand byte, always eight bits" — and note the choice in the report.

- [ ] **Step 8: Run everything, raise `ExpectedImplementedOpcodes` to 228, both TFMs, commit**

Expected: conformance **1766**. Bump the README to 228.

```bash
git commit -m "feat: 65816 BRK, COP, WDM and the native interrupt sequences"
```

**Gate:** conformance **1766**, both TFMs, 60,000 new vectors green, `Unimplemented816` deleted, the `VPB` test passing.

---

### Task 4: `MVN` and `MVP`

2 opcodes, and the only instruction in this engine that moves `PC` backwards.

**Files:**
- Modify: `src/SixtyFiveXX/AddrMode.cs` (`BlockMove`), `src/SixtyFiveXX/Op.cs` (`Mvn`, `Mvp`), `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816BlockMoveTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (228 → 230)

**Interfaces:**
- Consumes: nothing from tasks 2–3 beyond the emitter routing.
- Produces: `AddrMode.BlockMove`, `Op.Mvn`, `Op.Mvp`.

- [ ] **Step 1: Read research §14.3, including what it recorded a vector file to contain**

§14.3 answers, by observation, whether one vector covers one iteration or the whole move. **The implementation follows that observation.** If §14.3 records that the sources are silent on a register's direction, stop and report rather than picking one.

- [ ] **Step 2: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816BlockMoveTests.cs`. Assert, at minimum: one iteration moves one byte from the source bank to the destination bank; `DBR` ends as the destination bank; the index registers move in the direction §14.3 states, opposite for the two opcodes; the accumulator decrements; and `PC` is rewound so that a second `Step()` re-executes the same instruction while the count is not exhausted, and is not rewound on the final iteration.

Write each assertion against §14.3's stated rule and cite the subsection in the test's doc comment.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL**, `UndefinedOpcodeException` for `$54`.

- [ ] **Step 4: Add `AddrMode.BlockMove`**

```csharp
    /// <summary>
    /// <c>MVN</c>/<c>MVP</c> — two operand bytes, each a bank: the destination bank first in
    /// the instruction stream, then the source bank. The instruction re-executes itself once
    /// per byte moved by rewinding <c>PC</c>, so a single fetch can run for tens of thousands
    /// of cycles. 65816 only.
    /// </summary>
    BlockMove,
```

Confirm the operand order against §14.3 before committing to that comment.

- [ ] **Step 5: Add the micro-ops, the emitter arm, and the table entries**

The sequence is whatever §14.3's per-cycle table gives. Classify every new micro-op in all three tables, and put the write cycle in `BuildWriteTable`.

The rewind belongs in the last micro-op of the sequence, which must **not** call `EndInstruction()` differently depending on the count — it ends the instruction either way, and re-executes because `PC` now points back at the opcode. That is how hardware does it, and it is what makes an interrupt able to land between iterations.

```csharp
        // Block moves. Two operand bytes, both banks, and one instruction per byte moved:
        // the sequence rewinds PC so the next fetch re-executes it until the count runs out.
        Set(0x54, "MVN", AddrMode.BlockMove, Op.Mvn, Access.None);
        Set(0x44, "MVP", AddrMode.BlockMove, Op.Mvp, Access.None);
```

`Emit816` needs a `BlockMove` branch alongside the `Stack` one.

- [ ] **Step 6: Relax the harness's instruction-boundary assertion for these two opcodes**

**Research §14.3 measured this and it changes the gate:** `$54.n`'s cycle arrays are 9,999 × 100 entries and one × 98; `$44.n` is 9,997 × 100 plus one each of 63, 28 and 14. **The 100-cycle vectors stop mid-instruction** — `54 n 1` starts with `A = $EF9B`, 61,340 bytes to move, and its recorded final state is fourteen bytes in. `Harte816Tests` line 180 asserts `cpu.AtInstructionBoundary` after ticking, and that assertion fails on 9,999 of 10,000 `$54` vectors **however correct the core is**.

Everything else the harness does is already right for these files: the tick loop already runs exactly `test.Cycles.Length` cycles, and `AssertRegisters`/`AssertMemory`/`AssertCycles` compare against the vector's own recorded final state, which is the mid-instruction state. So the change is to that one assertion and nothing else.

Add a named set and skip only that assertion for it:

```csharp
    /// <summary>
    /// The block moves, whose vectors are truncated at 100 cycles with a final state part-way
    /// through the move — research document §14.3, measured from the files rather than inferred.
    /// A block move runs seven cycles per byte and moves up to 65,536 bytes, so no fixed-length
    /// vector could contain a whole one.
    /// </summary>
    /// <remarks>
    /// ONLY the instruction-boundary assertion is skipped. Every cycle's address, value and
    /// eight-character pin string is still compared, and so are the final registers and memory —
    /// against the mid-instruction state the vector actually records. For $54 that is a hundred
    /// arbitrated cycles of a real block move per vector, rewind included, across 10,000 vectors:
    /// stronger coverage than most opcodes in this core get, not weaker.
    /// </remarks>
    private static readonly HashSet<int> VectorsTruncatedMidInstruction = [0x54, 0x44];
```

and at the assertion:

```csharp
            if (!VectorsTruncatedMidInstruction.Contains(opcode))
            {
                Assert.True(cpu.AtInstructionBoundary,
                    $"{test.Name}: instruction did not finish within the vector's " +
                    $"{test.Cycles.Length} cycles.");
            }
```

**No vector file is excluded and no vector is skipped.** Say exactly that in the task report, because the phase gate is worded "all 512 files, no exclusions".

- [ ] **Step 7: Run everything, raise `ExpectedImplementedOpcodes` to 230, both TFMs, commit**

Expected: conformance **1770**. Bump the README to 230.

```bash
git commit -m "feat: 65816 MVN and MVP"
```

**Gate:** conformance **1770**, both TFMs, 40,000 new vectors green — every cycle, register and memory assertion, with the instruction-boundary assertion alone relaxed for `$54` and `$44` and the reason cited to research §14.3.

---

### Task 5: `WAI` and `STP`

2 opcodes, whose gate research §14.4 determined before this task was dispatched.

**Files:**
- Modify: `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/WaiStpTests.cs` (append the 65816 cases)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (230 → 232)

**Interfaces:**
- Consumes: `MicroOps.HoldsAtPc`, which today names `WaiHold` and `StpHold` and must name their 65816 counterparts too.

- [ ] **Step 1: Read research §14.4's vector observation**

If §14.4 records that the vector files do not model the hold, say so in the task report and gate this task on unit tests plus whatever the vectors *do* assert. **Do not skip the vector files** — they still run, and whatever they contain must pass.

- [ ] **Step 2: Write the failing tests**

`tests/SixtyFiveXX.Tests/WaiStpTests.cs` already covers both for the WDC 65C02. Read it, then append the 65816 equivalents to it — same file, since the two variants' rules are the same rules — with `E` set explicitly and the width flags set to opposed values. Assert that `WAI` resumes on an interrupt *signal* rather than on a poll, which is the rule the 8-bit implementation already encodes and comments, and that `STP` is escaped only by `Reset()`.

- [ ] **Step 3: Add `WaiHold816` and `StpHold816`**

The 8-bit forms call `ReadBus(_s.PC)`. The 65816's must drive `PcAddress()`, and §14.4 decides whether the held cycle is a read or an internal cycle. Add both to `MicroOps.HoldsAtPc` — a held cycle must drive PC, and for these two the hold is unbounded, which is exactly the case that comment exists for.

Add the `Op.Wai`/`Op.Stp` arm to `Emit816`, ahead of the implied branch, mirroring the eight-bit `Emit`.

```csharp
        Set(0xCB, "WAI", AddrMode.Implied, Op.Wai, Access.None);
        Set(0xDB, "STP", AddrMode.Implied, Op.Stp, Access.None);
```

- [ ] **Step 4: Teach the harness a null-address cycle**

**Research §14.4 measured this and it changes the gate:** all 40,000 `$CB`/`$DB` vectors are four entries, and the fourth is `[null, null, "--------"]` — no address, no value, and not even a read/write character in the pin string. The harness cannot read that cycle today: `AssertCycles` calls `raw[0].GetInt32()`, which throws on a JSON null. It also asserts `cpu.AtInstructionBoundary`, and a core that is correctly holding is not at a boundary.

Two changes, both narrow:

1. **`AssertCycles` handles a null address and a null pin string.** When `raw[0]` is null, the expected cycle is "the core drove no address and performed no access". Match it against whatever the core records for a held cycle; if the core cannot express that today, that is the finding — report it before inventing an encoding.
2. **Add `$CB` and `$DB` to `VectorsTruncatedMidInstruction`**, which task 4 introduced, and widen its doc comment: it now means "vectors whose final entry is not an instruction boundary", covering both the block moves' truncation and the halts' hold.

Everything else stays asserted: the three executed cycles' addresses, values and pin strings, and the recorded final state.

**What the vectors cannot check, and what must therefore be unit-tested:** §14.4 records that the hold itself, the wake on `IRQB`/`NMIB`, `WAI`'s `i`-flag special case and `STP`'s reset-only exit are absent from the vector set entirely. Those four rules are the whole difference between these opcodes and a three-cycle `NOP`, so step 2's unit tests are the only certification they get. Say so in the task report.

- [ ] **Step 5: Run everything, raise `ExpectedImplementedOpcodes` to 232, both TFMs, commit**

Expected: conformance **1774**. Bump the README to 232.

```bash
git commit -m "feat: 65816 WAI and STP"
```

**Gate:** conformance **1774**, both TFMs, 40,000 new vectors green, and unit tests covering the four rules the vectors do not model.

---

### Task 6: The ten branches

10 opcodes, on the plumbing tasks 2–5 certified.

**Files:**
- Modify: `src/SixtyFiveXX/AddrMode.cs` (`RelativeLong`), `src/SixtyFiveXX/Op.cs` (`Brl`), `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Create: `tests/SixtyFiveXX.Tests/W65C816ControlFlowTests.cs`
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (232 → 242)

**Interfaces:**
- Produces: `AddrMode.RelativeLong`, `Op.Brl`, and `MicroOp.BranchFetch816`/`BranchTaken816`/`BranchFixup816`, which nothing later consumes.

- [ ] **Step 1: Read research §14.5**

The one question that decides the sequence: **is the taken-branch page-cross cycle emulation-mode-only?** If §14.5 says yes, `BranchTaken816` ends the instruction in native mode whether or not the add crossed a page, and the fixup slot is emulation-only. Write the answer into the task report before writing the micro-op.

- [ ] **Step 2: Write the failing tests**

Create `tests/SixtyFiveXX.Tests/W65C816ControlFlowTests.cs`. Assert, at minimum: a taken branch that crosses a page costs the extra cycle in emulation mode and not in native (count cycles through `cpu.Cycles`, the way `BranchTests.cs` already does for the 8-bit cores — read it first); a branch's displacement add leaves `PBR` unchanged and wraps within the bank; `BRA` is unconditional; and `BRL`'s sixteen-bit displacement reaches backwards as well as forwards.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL**, `UndefinedOpcodeException` for `$10`.

- [ ] **Step 4: Add `AddrMode.RelativeLong` and `Op.Brl`**

```csharp
    /// <summary>
    /// A signed sixteen-bit branch displacement, measured from the byte after the instruction.
    /// Reaches anywhere in the current program bank and never changes <c>PBR</c>. Used only by
    /// <c>BRL</c>. 65816 only.
    /// </summary>
    RelativeLong,
```

- [ ] **Step 5: Add the micro-ops and the emitter arm**

`Emit816` gains a branch for `AddrMode.Relative` and `AddrMode.RelativeLong`. The three conditional micro-ops mirror the eight-bit `BranchFetch`/`BranchTaken`/`BranchFixup` in shape — the same conditional-slot idiom, `EndInstruction()` when the branch is not taken — with `PcAddress()` for every read and §14.5's rule for the fixup slot.

`IsBranchTaken()` already covers all nine conditions including `Op.Bra`. It needs no change; check that before editing it.

- [ ] **Step 6: Add the ten table entries**

```csharp
        // Branches. Eight conditional, BRA unconditional, and BRL with a sixteen-bit
        // displacement. Width.None throughout: a displacement is not an operand fetched at
        // a width the flags select.
        Set(0x10, "BPL", AddrMode.Relative, Op.Bpl, Access.None);
        Set(0x30, "BMI", AddrMode.Relative, Op.Bmi, Access.None);
        Set(0x50, "BVC", AddrMode.Relative, Op.Bvc, Access.None);
        Set(0x70, "BVS", AddrMode.Relative, Op.Bvs, Access.None);
        Set(0x90, "BCC", AddrMode.Relative, Op.Bcc, Access.None);
        Set(0xB0, "BCS", AddrMode.Relative, Op.Bcs, Access.None);
        Set(0xD0, "BNE", AddrMode.Relative, Op.Bne, Access.None);
        Set(0xF0, "BEQ", AddrMode.Relative, Op.Beq, Access.None);
        Set(0x80, "BRA", AddrMode.Relative, Op.Bra, Access.None);

        Set(0x82, "BRL", AddrMode.RelativeLong, Op.Brl, Access.None);
```

- [ ] **Step 7: Run everything, raise `ExpectedImplementedOpcodes` to 242, both TFMs, commit**

Expected: conformance **1794**. Bump the README to 242.

```bash
git commit -m "feat: 65816 branches, including BRA and BRL"
```

**Gate:** conformance **1794**, both TFMs, 200,000 new vectors green.

---

### Task 7: The jumps, the calls and the returns

11 opcodes, and the last of the phase's structural work.

**Files:**
- Modify: `src/SixtyFiveXX/AddrMode.cs` (`AbsoluteIndirectLong`), `src/SixtyFiveXX/Op.cs` (`Jml`, `Jsl`, `Rtl`), `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/W65C816ControlFlowTests.cs` (append)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (242 → 253)

**Interfaces:**
- Consumes: `PushStack816`, `PullStack816` from task 2; `VectorHi816`'s `PBR` handling as the model for `RTI`'s `PBR` pull.
- Produces: `AddrMode.AbsoluteIndirectLong`, `Op.Jml`, `Op.Jsl`, `Op.Rtl`.

- [ ] **Step 1: Read research §14.6 and write its seven answers into the task report**

Especially: **whether `RTI` pulls `PBR` in native mode**, and **`JSR (abs,X)`'s cycle order**. Both are shapes, not values — getting either wrong is a re-write, not a tweak.

- [ ] **Step 2: Write the failing tests, appended to `W65C816ControlFlowTests.cs`**

Assert, at minimum:

- `JMP ($nnnn)` reads its pointer from bank 0 and does **not** reproduce the NMOS page-wrap bug: with the pointer at `$xxFF`, the high byte comes from `$xx00 + 1`, not `$xx00`. This is the assertion that would catch a copy of the eight-bit `JmpIndHi`.
- `JML $llhhbb` and `JML [$nnnn]` both load `PBR`.
- `JSR` pushes two bytes and leaves `PBR` alone; `JSL` pushes three and sets it.
- `RTL` pulls three bytes; `RTS` pulls two; each adds one to the pulled address, or does not, per §14.6.
- `RTI` in native mode restores `PBR` and does **not** add one to the pulled address.
- `RTI` restores `P` without clearing the index-width flag — the same defect-1 assertion as `PLP`, on the second instruction that can hit it.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL**, `UndefinedOpcodeException` for `$4C`.

- [ ] **Step 4: Add `AddrMode.AbsoluteIndirectLong` and the three `Op` members**

```csharp
    /// <summary>
    /// <c>[abs]</c> — a three-byte pointer fetched from bank 0 at the sixteen-bit operand
    /// address. Used only by <c>JML</c>, and the only jump that takes its destination bank
    /// from memory rather than from the instruction stream. 65816 only.
    /// </summary>
    AbsoluteIndirectLong,
```

The absolute `JMP` keeps `AddrMode.Stack` and `JMP (abs)` keeps `AddrMode.Indirect`, matching the eight-bit tables' conventions — `Disassembler.DecodeStack` and the `Indirect` arm already format both, and the 65816's missing page-wrap bug is a property of its *sequence*, not of its notation. Do not reuse `AddrMode.IndirectFixed`: that mode also fixes the 65C02's six-cycle count, which is not this part's.

- [ ] **Step 5: Add the micro-ops and the `EmitControlFlow816` arms**

Eleven sequences, each transcribed from §14.6. Classify every micro-op in all three tables; every push goes in `BuildWriteTable`.

`Emit816` needs branches for `AddrMode.Indirect`, `AddrMode.AbsoluteIndexedIndirect` and `AddrMode.AbsoluteIndirectLong` alongside the `Stack` one — all four route to `EmitControlFlow816`, which switches on the operation.

- [ ] **Step 6: Add the eleven table entries**

```csharp
        // Jumps. JMP abs is AddrMode.Stack by this codebase's convention for hand-written
        // sequences; the indirect forms keep their own modes because the disassembler
        // formats an operand from them.
        Set(0x4C, "JMP", AddrMode.Stack,                   Op.Jmp, Access.None);
        Set(0x6C, "JMP", AddrMode.Indirect,                Op.Jmp, Access.None);
        Set(0x7C, "JMP", AddrMode.AbsoluteIndexedIndirect, Op.Jmp, Access.None);
        Set(0x5C, "JML", AddrMode.AbsoluteLong,            Op.Jml, Access.None);
        Set(0xDC, "JML", AddrMode.AbsoluteIndirectLong,    Op.Jml, Access.None);

        // Calls.
        Set(0x20, "JSR", AddrMode.Stack,                   Op.Jsr, Access.None);
        Set(0xFC, "JSR", AddrMode.AbsoluteIndexedIndirect, Op.Jsr, Access.None);
        Set(0x22, "JSL", AddrMode.AbsoluteLong,            Op.Jsl, Access.None);

        // Returns.
        Set(0x40, "RTI", AddrMode.Stack, Op.Rti, Access.None);
        Set(0x60, "RTS", AddrMode.Stack, Op.Rts, Access.None);
        Set(0x6B, "RTL", AddrMode.Stack, Op.Rtl, Access.None);
```

`$5C` and `$22` use `AddrMode.AbsoluteLong`, which `EmitAddressed816` also handles for `LDA long`. **`Emit816` must route on the operation for these two before it routes on the mode**, or a long jump will be given a long *load*'s addressing sequence. Add the operation test and a comment saying why it comes first.

- [ ] **Step 7: Run everything, raise `ExpectedImplementedOpcodes` to 253, both TFMs, commit**

Expected: conformance **1816**. Bump the README to 253.

```bash
git commit -m "feat: 65816 jumps, calls and returns"
```

**Gate:** conformance **1816**, both TFMs, 220,000 new vectors green, the `JMP ($xxFF)` no-bug assertion passing.

---

### Task 8: `PEA`, `PEI` and `PER`

3 opcodes, and the last three bytes of the instruction set.

**Files:**
- Modify: `src/SixtyFiveXX/Op.cs` (`Pea`, `Pei`, `Per`), `src/SixtyFiveXX/MicroOp.cs`, `src/SixtyFiveXX/MicroOpTable.cs`, `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Opcodes65C816.cs`
- Modify: `tests/SixtyFiveXX.Tests/W65C816ControlFlowTests.cs` (append)
- Modify: `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` (253 → **256**)

**Interfaces:**
- Consumes: `PushStack816` and the push micro-ops from task 2. `StackIsWide()` gains three arms.

- [ ] **Step 1: Read research §14.7**

All three push two bytes regardless of `m`. Confirm that, and confirm what `PER`'s displacement is measured from — an off-by-one there is invisible to every test that does not compute the expected address independently.

- [ ] **Step 2: Write the failing tests, appended to `W65C816ControlFlowTests.cs`**

Assert, at minimum: `PEA $1234` pushes `$12` then `$34` **with `m = 1`**, which is what makes "regardless of `m`" a real assertion rather than a restatement; `PEI` reads its sixteen-bit value from the direct page in bank 0 and pushes that, not the operand; and `PER`'s pushed value is the address §14.7 states, computed in the test from the instruction's own address rather than copied from the implementation.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL**, `UndefinedOpcodeException` for `$F4`.

- [ ] **Step 4: Add the three arms to `StackIsWide()`**

```csharp
        Op.Phd or Op.Pld or Op.Pea or Op.Pei or Op.Per => true,
```

- [ ] **Step 5: Add the micro-ops, the emitter arms, and the three table entries**

```csharp
        // The three stack-addressing pushes. All push two bytes whatever m says: PEA pushes
        // its own operand, PEI pushes a sixteen-bit value read from the direct page, and PER
        // pushes an address formed from a signed sixteen-bit displacement.
        Set(0xF4, "PEA", AddrMode.Stack, Op.Pea, Access.None);
        Set(0xD4, "PEI", AddrMode.Stack, Op.Pei, Access.None);
        Set(0x62, "PER", AddrMode.Stack, Op.Per, Access.None);
```

- [ ] **Step 6: Retire the undefined-opcode probe test and replace it, in this same task**

The moment the table reaches 256, `W65C816StateTests.UnimplementedOpcode_Throws` has no probe byte left — it derives one from the first `Op.Undefined` entry and calls `Assert.Fail` with an explanatory message when there is none. **Delete it and add its replacement in the same commit**, so the suite never goes red and no commit exists in which nothing asserts the tables are full.

Delete `W65C816StateTests.UnimplementedOpcode_Throws`. Add to `tests/SixtyFiveXX.Tests/MicroOpTableTests.cs`:

```csharp
    /// <summary>
    /// Every variant's table defines all 256 opcodes. Replaces the probe test that fetched the
    /// first undefined byte and asserted it threw — a test with no probe left once the 65816
    /// reached 256 of 256, and a weaker one in any case: this fails for a hole in ANY table,
    /// not only the one variant that happened to have one.
    /// </summary>
    [Fact]
    public void EveryVariantDefinesAll256Opcodes()
    {
        AssertTableIsFull(MicroOpTable.For<Mos6502Variant>(), "6502");
        AssertTableIsFull(MicroOpTable.For<Mos6510Variant>(), "6510");
        AssertTableIsFull(MicroOpTable.For<Synertek65C02Variant>(), "Synertek 65C02");
        AssertTableIsFull(MicroOpTable.For<Rockwell65C02Variant>(), "Rockwell 65C02");
        AssertTableIsFull(MicroOpTable.For<Wdc65C02Variant>(), "WDC 65C02");
        AssertTableIsFull(MicroOpTable.For<W65C816Variant>(), "65816");

        static void AssertTableIsFull(MicroOpTable table, string name)
        {
            for (var opcode = 0; opcode < 256; opcode++)
            {
                Assert.True(table.Info[opcode].Operation != Op.Undefined,
                    $"{name}: ${opcode:X2} is undefined.");
            }
        }
    }
```

The six type names above are the actual file names in `src/SixtyFiveXX/Variants/`.

`UndefinedOpcodeException` and `FetchOpcode`'s guard that throws it **stay**. The type is public API in a released package, so removing it is a breaking change that buys nothing, and the guard is the defensive path for exactly the hole this new test detects. Leave its remarks count-free.

- [ ] **Step 7: Run everything, raise `ExpectedImplementedOpcodes` to 256, both TFMs, commit**

Expected: conformance **1822**, and the unit suite **green** — no known failures. Bump the README to 256, and say the 65816 is complete.

```bash
git commit -m "feat: 65816 PEA, PEI and PER — 256 of 256 opcodes"
```

**Gate:** conformance **1822**, unit suite green with no known failures, both TFMs, 60,000 new vectors green.

---

### Task 9: Whole-branch review and the full 512-file gate

**Files:** whatever the review finds, plus `README.md` and the spec's Phase 7d Gate section.

The undefined-opcode path was settled in task 8: the probe test is gone and `MicroOpTableTests.EveryVariantDefinesAll256Opcodes` replaced it in the same commit, so no commit on this branch has either a red suite or an unguarded table. `UndefinedOpcodeException` and its fetch guard stay.

- [ ] **Step 1: Produce the branch diff**

```bash
git diff main...HEAD > .superpowers/sdd/p7d-review.diff
```

- [ ] **Step 2: Review against this checklist**

Each item is a failure this project has actually had:

- **Every new micro-op is classified, and classified *correctly*** — `BusPinsTests` proves presence, not correctness. Check each against research §14's pin columns, not against what the cycle appears to do. Phase 7c′'s sixteen one-character failures are the precedent.
- **`IsWriteCycle` is right for every pushing micro-op.** RDY must never halt a push, a `PBR` push, a `P` push or a block-move write.
- **`MicroOps.HoldsAtPc` names the 65816's `WAI`/`STP` micro-ops**, not only the eight-bit ones.
- **No opcode added in this phase declares a `Width`**, and `W65C816WidthTests` still asserts set equality with its original four-micro-op list.
- **Both reachability tests still pass**, and the deny-list in `W65C816ReachabilityTests` still matches what a fresh grep for a bare `_s.PC` address and for `0x0100` in `Cpu.cs` produces.
- **Every new test that discriminates a width sets both flags to opposed values.**
- **Cycle counts derive to research §14.8's formulas** for every one of the 44.
- **No unguarded width test in variant-shared code.** `grep -n "_s\.M\|_s\.XFlag\|_wide" src/SixtyFiveXX/Cpu.Exec.cs src/SixtyFiveXX/Cpu.cs` — every hit must sit behind `TVariant.Variant != CpuVariant.W65C816 ||` except arms for operations appearing in no 8-bit table.
- **`NotYet816`, `Unimplemented816` and `Sequences.RmwMiddle`** are each either gone or have a named live reader recorded in the ledger. No placeholder survives this phase silently.
- **No count in a doc comment that will drift.** Three sites were deliberately made count-free; do not reintroduce one.
- **`PublicSurfaceTests` untouched**, and no vector file or cache directory staged.

- [ ] **Step 3: Fix Critical and Important findings, each as its own commit**

Minor findings are either fixed or recorded in the ledger with the reason for not fixing. Do not silently drop one.

- [ ] **Step 4: Update the README**

The 65816 section says 256 of 256 and lists the phase's groups. The support-matrix row was deliberately made count-free — leave it that way.

- [ ] **Step 5: Add the Verified paragraph to the spec, and mark the phase-table row**

Under §"Phase 7d" Gate, in the shape 7a, 7b, 7c and 7c′ already use: the measured counts, both TFMs, and any rule with no vector coverage that is pinned only by a unit test. Then mark the phase-split table's **7d** row complete.

- [ ] **Step 6: Run the full gate on an idle machine**

```bash
uptime
dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"
dotnet test tests/SixtyFiveXX.Conformance
dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category=Performance"
```

Expected: conformance **1822** on both TFMs — all 512 files, 5,120,000 vectors — the unit suite green on both, and a throughput figure above the 50 MHz floor. Pass an explicit 600000 ms timeout on the conformance call. If the throughput gate fails, check `uptime` before believing it.

- [ ] **Step 7: Record the phase in the ledger**

Append a phase-7d section to `.superpowers/sdd/progress.md`: per-task commits, what each gate measured, every defect the vectors found that review did not, every defect review found that the vectors could not, every research gap §14 recorded as open, and the carry-forward list for 7e.

**Gate:** zero Critical findings. Conformance **1822**, unit suite green, both TFMs, build zero warnings, working tree clean.

---

## Carry-forward to phase 7e

- **The disassembler does not decode any 65816 addressing mode** and throws `NotSupportedException`. Every mode this phase added — `RelativeLong`, `BlockMove`, `AbsoluteIndirectLong` — needs an arm, as do all fifteen from 7b. That is 7e's work, together with the 64tass round-trip gate phase 6a established.
- **Research §12's four decimal-mode gaps remain open**: the correction algorithm at 8 bits, decimal `V`, invalid BCD digits, and part of `Z`/`C` sourcing. §13's two remain open as well. Whatever §14 records as silent joins them.
- **`main` is deliberately unpushed and far ahead of `origin/main`.** A non-skipped push cuts a public nuget.org release. Pushing is the owner's decision, not a step in any phase.
