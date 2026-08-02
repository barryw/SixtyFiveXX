# Phase 3 — `ICpuVariant` Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the CPU's opcode table and behavioural deltas selectable per variant at compile time, so a second core can exist — while changing the behaviour of the existing 6502 by exactly nothing.

**Architecture:** `Cpu<TBus>` currently binds `MicroOpTable.Mos6502` at construction. This adds a second type parameter carrying the variant, so the JIT specialises one tick loop per variant and folds away the checks a given core never takes. No runtime branch per micro-op — the engine's performance argument depends on the loop staying monomorphic.

**Tech Stack:** C# / .NET (`net8.0;net10.0`), xUnit, generic struct type parameters with static abstract interface members.

**Spec:** `docs/superpowers/specs/2026-08-02-variant-cores-design.md` §"Phase 3"

## Global Constraints

- **The gate is zero behaviour change.** Every existing suite must stay green: 2,560,000 Harte vectors, Klaus functional, Klaus interrupt, and the full unit suite — on **both** `net8.0` and `net10.0`.
- **No new opcodes, no variant behaviour, no CMOS deltas.** Those are phase 4. This phase only makes variance *possible*.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on; every public member needs an XML doc comment.
- **The core invariant holds:** one `Tick()` = one clock cycle = at most one bus access.
- **The tick loop must not gain a runtime variant branch.** If a variant check cannot be resolved at compile time, that is a design problem to report, not to work around with an `if`.
- Do not modify `tests/SixtyFiveXX.Conformance/klaus/6502_interrupt_test.asm`.
- Conventional Commits. Do not create tags or run `cog bump`.
- Work on a branch off `main`; do not commit to `main` directly.

## Established facts — verified, do not re-derive

- `OpcodeInfo` (`src/SixtyFiveXX/OpcodeInfo.cs`, 16 lines) is already variant-agnostic: `readonly record struct OpcodeInfo(string Mnemonic, AddrMode Mode, Op Operation, Access Access)`, plus a static `Undefined`.
- `Opcodes6502.Table` (`src/SixtyFiveXX/Opcodes6502.cs`) is `static readonly OpcodeInfo[]`, always 256 entries.
- `MicroOpTable` (`src/SixtyFiveXX/MicroOpTable.cs`, 283 lines) is `internal sealed`, exposes `static readonly MicroOpTable Mos6502 = new(Opcodes6502.Table)`, and public readonly fields `Ops`, `Entry`, `Info`, `IrqEntry`, `ResetEntry`. Its constructor is **private** and takes `OpcodeInfo[]`. Its builders — `Emit`, `EmitAddressing`, `EmitAccess`, `EmitStack` — are all `private static`.
- `Cpu<TBus>` (`src/SixtyFiveXX/Cpu.cs`, 738 lines) holds `_table`, `_ops`, `_entry` and is `public sealed partial class Cpu<TBus> where TBus : struct, IBus`. Execution lives in `Cpu.Exec.cs` (331 lines).
- `CpuVariant` (`src/SixtyFiveXX/CpuVariant.cs`) is a **public enum** with `Mos6502`, `Mos6510`, `Wdc65C02`, `Rockwell65C02`, `Synertek65C02`, `W65C816`. **Nothing consumes it yet.**
- `MicroOp` is an `internal` enum, so anything exposing it must be `internal` too (CS0051).
- Existing public API: `Cycles`, `ResetCycleCount()`, `Reset()`, `Tick()`, `Step()`, `Run(long)`, `RunUntil(...)`, `State`, `Bus`, `AtInstructionBoundary`, `IsJammed`, `SetIrq`/`SetNmi`/`SetRdy`/`SetSo`, `IrqAsserted`/`NmiAsserted`/`Ready`.
- Consumers of `Cpu<TBus>` inside this repo: `tests/SixtyFiveXX.Tests/TestMachine.cs` (`Cpu<FlatBus>`, `Cpu<RefBus>`), `tests/SixtyFiveXX.Conformance/*` (`Cpu<RefBus>`), `bench/SixtyFiveXX.Benchmarks`.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SixtyFiveXX/ICpuVariant.cs` | Create: the variant contract — static abstract members supplying the opcode table and behavioural flags |
| `src/SixtyFiveXX/Variants/Mos6502Variant.cs` | Create: the NMOS 6502 implementation of that contract |
| `src/SixtyFiveXX/MicroOpTable.cs` | Modify: build per variant rather than one static 6502 instance |
| `src/SixtyFiveXX/Cpu.cs` | Modify: second type parameter, resolve the table from it |
| `src/SixtyFiveXX/Cpu.Exec.cs` | Modify: type parameter only; no behavioural change |
| `tests/`, `bench/` | Modify: update `Cpu<TBus>` usages to the new arity |

---

### Task 1: The variant contract and the 6502 implementation

**Files:**
- Create: `src/SixtyFiveXX/ICpuVariant.cs`, `src/SixtyFiveXX/Variants/Mos6502Variant.cs`
- Modify: `src/SixtyFiveXX/MicroOpTable.cs`
- Test: `tests/SixtyFiveXX.Tests/VariantTests.cs`

**Interfaces:**
- Consumes: `OpcodeInfo`, `Opcodes6502.Table`, `MicroOpTable`.
- Produces: `internal interface ICpuVariant` with static abstract members; `internal readonly struct Mos6502Variant : ICpuVariant`; `MicroOpTable.For<TVariant>()`.

**Read first:** `OpcodeInfo.cs`, `Opcodes6502.cs`, and all of `MicroOpTable.cs`. The contract must be shaped by what the table builder actually needs, not by what phase 4 might want.

The contract carries the opcode table plus the behavioural flags phase 4 will need. Include only flags whose *absence* would block phase 4 — a flag with no consumer is speculative and violates the no-new-behaviour rule. At minimum the table itself; the design spec's phase 4 section lists the CMOS deltas that will need flags, and a flag defined now must be **read by nothing** in this phase.

Use **static abstract interface members** so every member resolves at compile time through the struct type parameter, with no instance and no virtual dispatch.

- [ ] **Step 1: Write the failing test**

`tests/SixtyFiveXX.Tests/VariantTests.cs` — assert that the 6502 variant supplies the same 256 descriptors `Opcodes6502.Table` does, and that a table built through the variant is structurally identical to the current `MicroOpTable.Mos6502` (same `Ops` length and contents, same `Entry`, same `IrqEntry`, same `ResetEntry`). This is the test that pins "zero behaviour change" at the table level, before any CPU runs.

Note: `MicroOp` is `internal`, so a test method touching it must be declared `internal` (CS0051); xUnit still runs it.

- [ ] **Step 2: Run it and confirm it fails**

`dotnet test tests/SixtyFiveXX.Tests --filter VariantTests -v q` — expect a compile error: `ICpuVariant` does not exist.

- [ ] **Step 3: Write `ICpuVariant` and `Mos6502Variant`**

Define the interface with static abstract members and the 6502 struct implementing it, returning `Opcodes6502.Table`.

- [ ] **Step 4: Add `MicroOpTable.For<TVariant>()`**

Replace the `static readonly Mos6502` field with a generic per-variant accessor that builds once per variant and caches. A `static` field on a generic type is already per-constructed-type in .NET — use that rather than a dictionary, so lookup is free.

Keep the private constructor and all four `Emit*` builders unchanged. **They must produce byte-identical output for the 6502.**

- [ ] **Step 5: Run the test green**

`dotnet test tests/SixtyFiveXX.Tests --filter VariantTests -v q`

- [ ] **Step 6: Full unit suite, both TFMs**

`dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category!=Performance" -v n` — 307/307 on each TFM. `Cpu` is untouched so far, so anything red here is a table-construction regression.

- [ ] **Step 7: Commit**

```bash
git add src/SixtyFiveXX tests/SixtyFiveXX.Tests/VariantTests.cs
git commit -m "refactor: introduce ICpuVariant and build micro-op tables per variant"
```

---

### Task 2: Thread the variant through `Cpu`

**Files:**
- Modify: `src/SixtyFiveXX/Cpu.cs`, `src/SixtyFiveXX/Cpu.Exec.cs`
- Modify: `tests/SixtyFiveXX.Tests/TestMachine.cs`, `tests/SixtyFiveXX.Conformance/*`, `bench/SixtyFiveXX.Benchmarks/*`

**Interfaces:**
- Consumes: `ICpuVariant`, `Mos6502Variant`, `MicroOpTable.For<TVariant>()`.
- Produces: `Cpu<TBus, TVariant> where TBus : struct, IBus where TVariant : struct, ICpuVariant`.

**This is the risky task.** It touches the hot path of a core certified against millions of vectors, and its gate is the *absence* of change.

**Read `Cpu.cs` and `Cpu.Exec.cs` in full before editing.** Both are partial halves of one class; the type parameter must change identically in both or they will not compile as one type.

- [ ] **Step 1: Add the type parameter**

Change `Cpu<TBus>` to `Cpu<TBus, TVariant>` in both files, and resolve `_table` from `MicroOpTable.For<TVariant>()` in the constructor instead of `MicroOpTable.Mos6502`.

**Change nothing else.** No reordering, no cleanup, no comment rewrites beyond those the type change makes factually wrong. A diff that mixes a mechanical type change with incidental edits cannot be reviewed against a zero-change gate.

- [ ] **Step 2: Update every consumer**

`TestMachine.Flat`/`Logged` → `Cpu<FlatBus, Mos6502Variant>` / `Cpu<RefBus, Mos6502Variant>`; the conformance harnesses and the benchmark likewise. `Mos6502Variant` is `internal`, and all three consumer projects already have `InternalsVisibleTo`.

Find them all: `grep -rn "Cpu<" --include=*.cs src tests bench`.

- [ ] **Step 3: Build clean**

`dotnet build -c Release -v q` — 0 warnings, 0 errors, both TFMs.

- [ ] **Step 4: Full unit suite, both TFMs**

`dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category!=Performance" -v n` — 307/307 each.

- [ ] **Step 5: The real gate — full conformance, both TFMs**

`dotnet test tests/SixtyFiveXX.Conformance -c Release -v n` — **259/259 on each TFM.** This is 2,560,000 Harte vectors plus Klaus functional and Klaus interrupt. It takes several minutes per TFM.

**Any failure here is the refactor's fault** — nothing else changed. Report the failing vector or trap address rather than working around it.

- [ ] **Step 6: Confirm the tick loop stayed monomorphic**

Confirm no runtime variant branch entered the per-cycle path: `grep -n "CpuVariant\|typeof(TVariant)\|is Mos6502Variant" src/SixtyFiveXX/Cpu*.cs` should find nothing in `Tick`/`Execute`. Report what you found.

Then run the performance gate in Release: `dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category=Performance" -v n`. It asserts a 50 MHz floor and is sensitive to machine load — if it fails, check `uptime` before concluding anything, and report the load figure alongside the result.

- [ ] **Step 7: Commit**

```bash
git add src tests bench
git commit -m "refactor: make the CPU generic over its variant"
```

---

### Task 3: Public variant selection

**Files:**
- Modify: `src/SixtyFiveXX/CpuVariant.cs`, `src/SixtyFiveXX/Variants/Mos6502Variant.cs`
- Test: `tests/SixtyFiveXX.Tests/VariantTests.cs` (extend)

**Interfaces:**
- Consumes: `CpuVariant` enum, `ICpuVariant`.
- Produces: a `CpuVariant` member on the variant contract, and its 6502 value.

`CpuVariant` is public and consumed by nothing. sim6502 selects its processor at runtime from a DSL, so phase 6's adapter will need to map that enum onto a variant type. This task makes each variant *declare* which enum value it is — the one direction that is resolvable at compile time.

**Do not** add a runtime factory mapping `CpuVariant` → `Cpu<,>`. That belongs in phase 6's adapter, where the set of variants is known and the boxing cost is paid once per machine rather than per cycle.

- [ ] **Step 1: Write the failing test**

Extend `VariantTests.cs`: assert `Mos6502Variant` reports `CpuVariant.Mos6502`.

- [ ] **Step 2: Run it and confirm it fails**

`dotnet test tests/SixtyFiveXX.Tests --filter VariantTests -v q`

- [ ] **Step 3: Add the member**

A static abstract `CpuVariant Variant { get; }` on `ICpuVariant`, implemented by `Mos6502Variant`.

- [ ] **Step 4: Run green, then the full unit suite both TFMs**

- [ ] **Step 5: Update `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md`**

Mark phase 3 complete in §10, and correct §5.5's public API sketch if the type parameter change makes it wrong. Stale specs are treated as defects here.

- [ ] **Step 6: Commit**

```bash
git add src tests docs
git commit -m "feat: let a variant declare its CpuVariant value"
```

---

## Self-review notes

Checked against `docs/superpowers/specs/2026-08-02-variant-cores-design.md` §"Phase 3":

- **Compile-time resolution** — static abstract interface members through a struct type parameter, verified by Task 2 Step 6.
- **Zero behaviour change** — gated three ways: table-structural equality (Task 1), the full unit suite on both TFMs (every task), and 2.56 M vectors plus both Klaus programs on both TFMs (Task 2 Step 5).
- **Nothing from phase 4** — no opcodes, no CMOS deltas; behavioural flags may be *defined* but must be read by nothing.
- The spec's "second type parameter carrying the variant's opcode table and its behavioural flags" is Task 1's contract and Task 2's threading.

Two things this plan does not settle, because only the work can:

**The exact flag set on `ICpuVariant`.** The spec lists phase 4's CMOS deltas, but which become variant flags versus separate opcode tables depends on how `Emit*` is shaped — which Task 1's implementer reads. Defining too many now is speculative; too few means reopening the contract in phase 4. The rule that resolves it: a flag earns its place only if phase 4 is blocked without it.

**Whether `static abstract` members meet the `net8.0` floor cleanly.** They are C# 11 / .NET 7+, so both target frameworks support them — but the interaction with `InternalsVisibleTo` and an `internal` interface implemented by an `internal` struct is worth confirming early in Task 1 rather than discovering at Task 2's consumer update.
