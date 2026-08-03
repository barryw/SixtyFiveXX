# Phase 6b — The sim6502 Adapter Swap

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** sim6502 executes on SixtyFiveXX. `SimulatorBackend` becomes a thin adapter, `sim6502/Proc/` is deleted, and the Aaron Mell BSD notice leaves sim6502's `NOTICE`.

**This phase is executed in `~/Git/sim6502`, not in SixtyFiveXX.** It is planned here because the plan's facts were gathered here and because 6a is its prerequisite. sim6502 is at **v4.0.1** with a live suite of 1,740 tests; this is the highest blast-radius change in the whole roadmap.

**Spec:** `docs/superpowers/specs/2026-08-02-variant-cores-design.md` §"Phase 6", `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md` §6.

## Global Constraints

- `IExecutionBackend` is **unchanged** — 21 members. It is the contract every other backend (VICE, U64, NovaVM) implements, and changing it would drag them all in.
- Conventional Commits. Branch off `main` in sim6502. Its `main` cuts releases the same way SixtyFiveXX's does.

## Established facts — verified before planning, do not re-derive

- **SixtyFiveXX v0.1.0 is the dependency.** It was published for this phase; nothing existed on nuget.org before it. sim6502 takes a normal `PackageReference`.
- **`SimulatorBackend` is only 128 lines**, and most members forward one-to-one. The work is not in the twenty-one members; it is in the four items below.
- ***** The variant is a compile-time type parameter, and sim6502 chooses at runtime. ***** `Cpu<TBus, TVariant>` closes over the variant so the JIT can fold it away, but the DSL says `processor(6510)` at parse time. Something has to bridge that. Make `SimulatorBackend<TVariant>` generic and have `BackendFactory` switch once over `ProcessorType` to pick the closed type — five constructions in one switch, rather than an `ICore` interface and five hand-written wrappers.
- **The DSL knows only three processors**: `ProcessorType.MOS6502`, `MOS6510` and `WDC65C02` (`SimBaseListener.GetProcessorType`). SixtyFiveXX has five variants; Synertek and Rockwell have no DSL spelling and need none unless the grammar grows one.
- **`ProcessorType` lives in `sim6502/Proc/ProcessorType.cs`** and therefore dies with the directory. The grammar, `MemoryMapFactory` and `SimBaseListener` all use it, so it has to move out of `Proc/` before the deletion rather than being replaced wholesale by `CpuVariant` — the DSL's vocabulary is its own, and coupling the grammar to a dependency's enum is a worse trade than keeping a three-value enum.
- **`RunRoutine` does not JSR, despite `ExecuteJsr`'s name.** It sets `ProgramCounter = address`, starts `subroutineCount` at 1 and counts `$20`/`$60` opcodes as it goes. Reproduce it exactly:

      ProgramCounter = address;
      do {
          if (opcode == 0x20) subroutineCount++;
          if (opcode == 0x60) { subroutineCount--; if (subroutineCount == 0 && stopOnRts) keepRunning = false; }
          if (opcode == 0x00) { keepRunning = false; if (failOnBrk) exitCleanly = false; }
          if (ProgramCounter == stopOnAddress && stopOnAddress > 0) keepRunning = false;
          NextStep();
      } while (keepRunning);

  Three details are load-bearing and none are obvious. The opcode is peeked at `PC` **before** the step, so **the terminating instruction still executes** — the final `RTS` runs and `PC` ends at the return address. `stopOnAddress` is only honoured when **greater than zero**. And the stop check happens *before* the step, so reaching the stop address still costs one more instruction. `Cpu.RunUntil` cannot express this directly; drive `Step()` in the adapter and keep the depth count there. The core stays a CPU.
- **`Processor` leaks past the backend in two places.** `SimBaseListener.Proc => (Backend as SimulatorBackend)?.Processor` is a public escape hatch, and `U64SimBackend` reads `_sim.Processor.CycleCount` for its cycle counter. Both need a route that does not name `Processor` — `GetCycles()` already exists on the interface for the second.
- **`IMemoryMap` is the bus.** It has `Read`/`Write` (which tick `IncrementCycleCount`) and `ReadWithoutCycle`/`WriteWithoutCycle`. Wrap it in a class implementing SixtyFiveXX's `IBus` and hand that to `RefBus`, which exists precisely to accept runtime polymorphism. **Decide deliberately whether the adapter calls `Read` or `ReadWithoutCycle` for CPU accesses**: `Read` keeps banking and I/O handlers working but also drives `IncrementCycleCount`, and SixtyFiveXX counts its own cycles. Two counters that both advance is the bug this bullet exists to prevent.
- **Trace strings are the adapter's job.** `Processor.Disassembly.cs:182` builds them as
  `$"${PC:X4}: {OpCodeString} {DisassemblyOutput,-10} A=${A:X2} X=${X:X2} Y=${Y:X2} SP=${SP:X2} {FormatFlags()}"`.
  `Disassembler.Decode` supplies the first two fields — mnemonic and operand — and the register and flag decoration stays here, which is exactly why 6a returns operand text rather than a formatted line.
- ***** sim6502's branch arithmetic looks wrong and is not. ***** `movement = d > 127 ? d - 255 : d`, then `PC + movement + 1`, then a further `+1` only when `movement >= 0`. The missing 256 and the missing `+2` cancel exactly, and it agrees with SixtyFiveXX for every displacement. **Do not "fix" it** — it is being deleted, and while it exists it is a reference to verify against.
- **sim6502 has no `ZeroPageRelative`, no `BBR`/`BBS`.** Its `65C02` support maps to WDC. SixtyFiveXX renders the Rockwell bit operations with the bit fused into the mnemonic (`RMB0`), which is what a trace should show.

## The thing that must be said out loud

**Deleting `Proc/` deletes 1,120 of sim6502's 1,740 tests — 64% of the suite.** Counted exactly: `ProcessorTests` 962, `Opcodes65C02Tests` 110, `OpcodeRegistryTests` 20, `Processor6510Tests` 16, `StackPointerWrappingTests` 6, and 2 each from `KlausDormannFunctionalTests`, `ProcessorExecutionTests` and `ProcessorTypeTests`. They test the code being removed, so "sim6502's own suite green" after the swap is a far weaker claim than it sounds: the 620 that remain gate the DSL, the grammar and the other backends, and **nothing in sim6502 will test a CPU any more**. It inherits SixtyFiveXX's certification instead — 10,220,000 vectors, Klaus's three programs, VICE's cpuport — which is a much stronger gate than 962 hand-written cases, but it is a *different* gate and the swap should be described that way rather than blurred.

Note which files only *look* doomed. `SimulatorBackendTests` (9), `BackendFactoryTests` (14), `U64SimBackendTests` (16), `MemoryMapFactoryTests` (5) and `UtilityFileTests` (9) all name `sim6502.Proc`, but only for `ProcessorType` or through the backend — which survives. **`SimulatorBackendTests` is the most direct gate the rewrite has**: it exercises the adapter through its public surface and must go green against the new implementation without being rewritten to suit it.

Those 962 tests are also, right now, an independent oracle written from a different lineage. Task 3 spends them before they are lost.

## File Structure (all paths in `~/Git/sim6502`)

| File | Responsibility |
| --- | --- |
| `sim6502/sim6502.csproj` | Modify: `PackageReference` to SixtyFiveXX 0.1.0 |
| `sim6502/ProcessorType.cs` | Move out of `Proc/` before the deletion |
| `sim6502/Backend/MemoryMapBus.cs` | Create: `IMemoryMap` → SixtyFiveXX `IBus` |
| `sim6502/Backend/SimulatorBackend.cs` | Rewrite as `SimulatorBackend<TVariant>` |
| `sim6502/Backend/BackendFactory.cs` | Modify: switch `ProcessorType` to a closed generic |
| `sim6502/Grammar/SimBaseListener.cs` | Modify: drop the `Proc` escape hatch |
| `sim6502/Backend/U64SimBackend.cs` | Modify: `GetCycles()` rather than `Processor.CycleCount` |
| `sim6502/Proc/` | **Delete**, last |
| `NOTICE` | Modify: remove the Aaron Mell BSD notice |

---

### Task 1: Depend on SixtyFiveXX and bridge the bus

- [ ] **Step 1:** `PackageReference` to SixtyFiveXX 0.1.0. Confirm it restores for every framework sim6502 targets.
- [ ] **Step 2:** `MemoryMapBus`, an `IBus` over `IMemoryMap`, handed to the core through `RefBus`. Settle the `Read` versus `ReadWithoutCycle` question here and write the reason down — two cycle counters both advancing is the failure this is guarding.
- [ ] **Step 3: Commit.**

### Task 2: The adapter

- [ ] **Step 1:** Move `ProcessorType` out of `Proc/` so the grammar survives the deletion.
- [ ] **Step 2:** `SimulatorBackend<TVariant>` implementing all 21 members. `GetCycles`/`ResetCycleCount` map to `Cycles`/`ResetCycleCount`; registers and flags to `CpuState`.
- [ ] **Step 3: `ExecuteJsr`.** Reproduce `RunRoutine` exactly, including all three details above. Write a test per detail — the terminating instruction executing, `stopOnAddress > 0`, and the extra instruction after the stop address — because each is a silent behaviour change if missed.
- [ ] **Step 4: Trace.** Build the line from `Disassembler.Decode` plus registers and flags. Compare against `Proc`'s output on a real program **while both still exist**; that comparison is impossible after Task 5.
- [ ] **Step 5:** `BackendFactory` switches `ProcessorType` onto the closed generic. Remove the `SimBaseListener.Proc` escape hatch and `U64SimBackend`'s `Processor.CycleCount`.
- [ ] **Step 6: Commit.**

### Task 3: Spend the old tests before deleting them

**The point of this task is to use 962 independent test cases as an oracle while they still exist.** Anything that disagrees is a finding, not a test to delete.

- [ ] **Step 1:** Point what can be pointed at the new backend. `ProcessorTests` exercises `Processor` directly, so a wholesale port is not the goal — take the cases that map onto `IExecutionBackend` and run them against `SimulatorBackend<TVariant>`.
- [ ] **Step 2: Investigate every disagreement.** Expect some: sim6502's core is not per-cycle accurate for undocumented opcodes or dummy accesses, so **cycle counts will move**, and the DSL exposes `cycles` to user test suites through `assert(cycles < 100, ...)`. A consumer's suite can therefore go red on a correct change. That is a release-note item, not a bug to hide.
- [ ] **Step 3:** Record what was checked and what was given up, then let the ported cases go with the rest.
- [ ] **Step 4: Commit.**

### Task 4: The suite that remains

- [ ] **Step 1:** All 625 non-`Proc` tests green — grammar, backends, integration, CLI.
- [ ] **Step 2:** Run the example suites in `example/` end to end. `filter_test.6502` asserts on `cycles` and is the closest thing to a consumer's suite in the tree.
- [ ] **Step 3: Commit.**

### Task 5: Delete

- [ ] **Step 1:** Delete `sim6502/Proc/` and the tests that covered it.
- [ ] **Step 2:** Remove the Aaron Mell BSD notice from `NOTICE`. **`LicenseTests` asserts on it** — line 32 requires the file to contain "Aaron Mell" — so that assertion changes in the same commit as the notice, and it must be a deliberate edit rather than a failing test quietly relaxed. Two lines in `NOTICE` are easy to confuse and only one goes: the attribution at line 18 exists because the BSD-licensed code was in the distribution, so it is tied to `Proc/`'s presence. Line 9, "prior to version 4.0.0, sim6502 was distributed under the BSD 2-Clause licence", is a statement about sim6502's own history and stays regardless.
- [ ] **Step 3:** Suite green. Final whole-branch review, then merge.

---

## Risks

- **Cycle counts change, and they are user-visible.** The DSL lets a consumer write `assert(cycles < 100)`. SixtyFiveXX is per-cycle certified and sim6502's core is not, so some of those numbers move — correctly. This needs a release note, and it is the strongest argument for a major version bump on sim6502.
- **The gate is much weaker after the deletion than before it.** See above: nothing in sim6502 tests a CPU any more. Task 3 exists to spend the old tests rather than merely discard them, but it cannot fully substitute.
- **The runtime-to-compile-time bridge is the design risk.** Get it wrong and either every variant pays for a runtime branch — which is the cost SixtyFiveXX's whole type-parameter design exists to avoid — or the backend ends up with five hand-maintained copies that drift.
- **`RunRoutine`'s three quirks are exactly the kind of thing a reimplementation smooths over.** Each is a silent behaviour change for every existing consumer suite.
- **sim6502 is at v4.0.1 and ships.** Unlike SixtyFiveXX's phases, this one changes a released tool's behaviour. It is not a refactor with the absence of change as its gate.
