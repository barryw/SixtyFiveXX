# SixtyFiveXX — Design

**Date:** 2026-07-31
**Status:** Approved
**Author:** Barry Walker

## 1. Purpose

A clean-room, bus-accurate emulator for the MOS/WDC 65xx processor family, written in
C# and released under MIT. It replaces the Aaron Mell BSD-licensed 6502Net core
currently vendored in `sim6502/Proc/`, and it is designed to be useful outside
sim6502 as a standalone library.

Two properties drive every decision below:

1. **Cycle accuracy.** sim6502 has a mode in which tests assert cycle counts. Those
   assertions are only as trustworthy as the core underneath them.
2. **Variant coverage.** 6502, 65C02 (Rockwell/Synertek/WDC), 6510, and 65816, sharing
   one engine rather than four forks.

### Non-goals

- Rendered video or audio output. See §8 — machine personalities model what the CPU can
  observe or be delayed by, and nothing else.
- Emulating machines beyond the C64 in this spec. The personality *contract* is in
  scope; Apple IIe, NES, and Apple IIgs are follow-on work against a frozen contract.
- Redistribution of any copyrighted ROM or PLA image. See §9.

## 2. Context

sim6502 today embeds a ~3,900-line instruction-stepped core:

```
sim6502/Proc/  Processor.Core.cs, Processor.Execution.cs, Processor.Addressing.cs,
               Processor.Operations.cs, Processor.Memory.cs, Processor.Disassembly.cs,
               OpcodeRegistry.cs, ProcessorType.cs, AddressingMode.cs, …
```

It supports `MOS6502`, `MOS6510`, and `WDC65C02`, and reaches memory through
`sim6502.Systems.IMemoryMap`. Everything above it goes through
`sim6502.Backend.IExecutionBackend`, which sits alongside VICE, Ultimate64, and NovaVM
backends.

That core is instruction-stepped: it executes a whole instruction and adds a cycle
count. It therefore cannot be validated against per-cycle test vectors, and it cannot
model anything that happens *inside* an instruction — dummy reads, interrupt sampling
points, or a VIC-II stealing the bus mid-opcode.

sim6502 is GPL-3.0 and carries a BSD notice for the Mell core. Replacing that core
removes the notice.

## 3. Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Accuracy model | Bus-accurate, cycle-stepped | Superset of cycle counting; the only design that passes per-cycle suites or can later drive a real machine |
| Dispatch | Micro-op table, interpreted | One declarative source of truth per variant; no codegen; JIT compiles the tick switch to a jump table |
| Bus abstraction | `Cpu<TBus, TVariant> where TBus : struct, IBus` | JIT specializes and inlines every bus access — no virtual call on the hottest path |
| Variant dispatch | `TVariant : struct, ICpuVariant` with static abstract members | `if (TVariant.CmosDecimal)` folds to a compile-time constant, so the dead branch is eliminated — a 6502 pays nothing for the 65C02's differences, and one engine serves every core |
| Optimization timing | After all cores land | One shared engine means each optimization is written once and benefits every variant. Optimizing against a 6502-only shape risks choices the 65816 cannot use |
| Licence | MIT | GPL-3.0 sim6502 can consume it; so can anyone else |
| Packaging | Own repo, NuGet package | Independently versioned and testable; reusable |
| Variant order | 6502 → 65C02 → 6510 → 65816 | Each certified before the next begins |
| Target framework | `net10.0` | Matches sim6502; best JIT |
| CI | Woodpecker, mirroring sim6502 | Matches the existing pipeline |

## 4. Repository layout

```
SixtyFiveXX/                          MIT, net10.0
├── src/SixtyFiveXX/                  core library → NuGet "SixtyFiveXX", zero dependencies
├── src/SixtyFiveXX.Machines/         personality layer → NuGet "SixtyFiveXX.Machines"
├── tests/SixtyFiveXX.Tests/          unit tests (xUnit)
├── tests/SixtyFiveXX.Conformance/    external suite runners
├── bench/SixtyFiveXX.Benchmarks/     BenchmarkDotNet, perf gate
├── docs/
├── .woodpecker.yml
└── SixtyFiveXX.sln
```

Tests use plain xUnit assertions. FluentAssertions 8.x is under the Xceed Community
Licence, which requires a paid licence for commercial use; an MIT library should not
carry that dependency.

## 5. Core architecture

### 5.1 Bus

```csharp
public interface IBus
{
    byte Read(int address);              // 0..0xFFFFFF for 65816; 8-bit cores mask to 0xFFFF
    void Write(int address, byte value);
}

public interface ICpuVariant
{
    static abstract bool CmosDecimal { get; }        // 65C02 corrects N/Z and costs a cycle
    static abstract bool FixedJmpIndirect { get; }   // 65C02 fixes the NMOS JMP ($xxFF) bug
    static abstract bool ClearsDecimalOnInterrupt { get; }
    static abstract bool HasIoPort { get; }          // 6510's on-chip $00/$01 registers
    static abstract bool SixteenBit { get; }         // 65816 native mode
}

public sealed class Cpu<TBus, TVariant>
    where TBus : struct, IBus
    where TVariant : struct, ICpuVariant
```

A `struct` type parameter makes the JIT emit a specialized `Cpu<TBus, TVariant>` per
combination, so bus accesses inline to direct array indexing **and** every
variant-dependent branch folds to a compile-time constant. `if (TVariant.CmosDecimal)`
costs an NMOS 6502 nothing at all — the branch and its body are eliminated before the
code ever runs.

This is what makes a single engine viable across five cores. Every optimization in the
performance pass (§10, phase 7) is written once against this engine and benefits all of
them, which is the whole reason variant selection is a type parameter rather than a
field.

Consumers who choose a core at runtime go through a non-generic facade; the facade's
methods are per-*run*, never per-cycle, so the hot loop stays inside the generic type.

Shipped buses:

- `FlatBus` — 64 KB array, no decoding. The default for conformance suites and unit tests.
- `RefBus` — adapts any `IBus` *reference*. One virtual call per access. Documented as
  the convenient-but-slower door, for consumers who must choose a bus at runtime.

A non-generic `ICpu` facade exposes reset, run, register, and pin operations for
runtime-selected buses. Its methods are per-*run*, never per-cycle: the tick loop always
stays inside the generic type.

### 5.2 Micro-op engine

Each opcode of each variant is a sequence of micro-ops, stored flat rather than jagged
for cache locality:

```csharp
internal enum MicroOp : byte { FetchLo, FetchHi, ReadEff, WriteEff, DummyRead,
                               AluAdc, PushPch, PullP, FixupPage, … }   // ~50 kinds

internal static readonly MicroOp[] Ops;      // every variant's sequences, one array
internal static readonly ushort[] Entry;     // opcode → offset into Ops
```

`Tick()` executes exactly one micro-op, performs at most one bus access, and increments
the cycle counter. The switch over ~50 dense enum values compiles to a jump table.

**Data-dependent timing** — page-cross penalties, branch taken, branch across a page —
is expressed as micro-ops that conditionally advance the micro-PC past a fixup op. The
tick loop itself contains no per-opcode special cases.

### 5.3 What "cycle accurate" includes

These are the difference between counting cycles and being cycle accurate, and Harte's
vectors check every one:

- Dummy reads on indexed addressing before the page-cross fixup.
- Dummy read *and* dummy write on NMOS read-modify-write instructions (the 65C02 does a
  dummy read instead).
- IRQ and NMI sampled on the penultimate cycle of an instruction, not at its boundary.
- NMI edge-latched; IRQ level-sensitive.
- NMI hijacking a BRK or IRQ sequence when it is latched before that sequence's fifth
  cycle, the P push — the cycle on which silicon commits the vector address.
- `RDY` halting the CPU on read cycles only — writes complete regardless. This is the
  mechanism personalities use for bus stealing.
- `SO` pin setting the overflow flag.

### 5.4 Variants

| Variant | Delta from baseline |
| --- | --- |
| `Mos6502` | NMOS baseline. All 105 undocumented opcodes, including the unstable `ANE`/`LXA`/`TAS`/`SHA`/`SHX`/`SHY` magic-constant behaviour. BCD `ADC`/`SBC` with real, undefined N/V/Z. |
| `Mos6510` | 6502 plus the on-chip `$00` DDR and `$01` port registers. These are CPU registers, so they live in the **core**, not in a bus. |
| `Wdc65C02` | New opcodes; `JMP ($xxFF)` page-wrap bug fixed; `D` cleared on interrupt; corrected BCD flags at the cost of one cycle; per-opcode NOP timing table. Sub-flags: `Rockwell` (`RMB`/`SMB`/`BBR`/`BBS`), `Wdc` (adds `WAI`/`STP`), `Synertek`. Harte tests these three separately. |
| `W65C816` | 16-bit `A`/`X`/`Y`, `M`/`X`/`E` mode bits, 24-bit addressing, `DBR`/`PBR`/`DP`, `MVN`/`MVP` block moves, `COP`, and the extra cycle when the direct-page low byte is non-zero. Its own opcode table on the **same** engine and the same tick loop. |

`CpuState` carries 16-bit `A`/`X`/`Y` plus the bank and direct-page registers for every
variant; the 8-bit cores use only the low bytes, and `TVariant.SixteenBit` folds away the
mode checks they never take. This costs the 8-bit cores a slightly larger state struct in
exchange for one engine, one tick loop, and one place to optimize. Whether that trade
holds is a measurement for phase 7, not an assumption — if the wider state measurably
hurts the 8-bit cores, the fallback is a separate `Cpu816` sharing the same building
blocks.

### 5.5 Public API surface

```csharp
long Cycles { get; }
void ResetCycleCount();
void Reset();

void Tick();                                 // one clock cycle
void Step();                                 // run to the next instruction boundary
long Run(long cycles);
long RunUntil(Func<ICpu, bool> stop);

ref CpuState State { get; }                  // A, X, Y, S, P, PC (+ C, DBR, PBR, DP, E on 65816)
void SetIrq(bool asserted);
void SetNmi(bool asserted);
void SetRdy(bool ready);
void SetSo();
```

Higher-level control flow — sim6502's `ExecuteJsr(address, stopOnAddress, stopOnRts,
failOnBrk)` — is built on `RunUntil` plus stack-depth tracking **in the adapter**, not in
the core. The core stays a CPU.

A `Disassembler` decodes from any `IBus` for a given variant, driven by the same opcode
table as the engine, so the two cannot drift.

## 6. sim6502 integration

`IExecutionBackend` is unchanged. `SimulatorBackend` becomes a thin adapter:

- `sim6502.Systems.IMemoryMap` → `IBus` via a struct wrapper.
- `ExecuteJsr` → `RunUntil` plus stack-depth tracking.
- `GetCycles` / `ResetCycleCount` → `Cycles` / `ResetCycleCount`.
- Trace strings → `Disassembler`.

Deleted on swap: all of `sim6502/Proc/` (~3,900 lines) and the Aaron Mell BSD notice in
`NOTICE`.

## 7. Testing and certification

A variant is "certified" only when every applicable suite is green. Nothing ships on the
strength of unit tests alone.

| Suite | Licence | Covers | Handling |
| --- | --- | --- | --- |
| **Harte / SingleStepTests** `65x02`, `65816` | MIT | 10,000 per-cycle vectors per opcode, with full bus activity, for 6502, NES6502, Rockwell/Synertek/WDC 65c02, 65816 | ~1 GB across sets. Downloaded and cached to a gitignored directory, not committed. Offline path supported. |
| **Klaus Dormann** functional + 65C02 extended | GPL (binaries only) | Broad behavioural coverage, full BCD | Pre-built binaries fetched by the runner. Running a GPL test binary does not affect our licence. |
| **Wolfgang Lorenz** C64 suite | Public domain | NMOS undocumented opcodes, timing | **Cannot certify the `$00`/`$01` port on a bare core** — see below. Retained only as a possible phase 8 gate, once a C64 personality exists to host it. |
| **VICE `cpuport`** (`testprogs/CPU/cpuport/test1`) | GPL (binary only) | The 6510 `$00` DDR / `$01` port delta | Small, deterministic, needs no KERNAL, CIA or VIC-II. Same "execute a GPL binary" posture as Klaus. |
| **Bruce Clark** decimal test | Freely distributed | Exhaustive BCD `ADC`/`SBC`, both NMOS and CMOS semantics | Small; catches the classic decimal bugs. |

Plus:

**Why Lorenz cannot gate the 6510.** Its CPU-port tests are not silicon tests. `MMU` and
`MMUFETCH` assert on real Commodore ROM byte values visible through C64 banking, and
`CPUPORT` exercises board-level analog behaviour — the unconnected-pin decay the C64's
wiring produces, not anything the 6510 defines. Tom Harte's CLK harness excludes exactly
those three for that reason. There is also **no 6510 vector set** anywhere in
SingleStepTests. So the 6510 is certified in two parts: its inherited opcodes by the
existing 6502 suites, and its `$00`/`$01` delta by the VICE `cpuport` test. The analog
decay behaviours (`bitfade`, `delaytime`) are a deliberate non-goal — VICE's own authors
describe their timing as guesswork — the same posture this project takes toward the
unstable NMOS opcodes' magic constants.

- **The packed-assembly public-surface test.** `dotnet pack` the library, then reflect over
  the packed assembly and assert the intended surface: `Cpu<,>`, `ICpuVariant`, `IBus`,
  `FlatBus`, `RefBus`, `CpuState`, `CpuVariant` and each variant struct **public**;
  `OpcodeInfo`, `AddrMode`, `Op`, `Access`, `MicroOp` and `MicroOpTable` **not visible**.

  This exists because of a real regression. During the phase 3 refactor, `OpcodeInfo` being
  internal forced `ICpuVariant` internal, which forced `Cpu` internal (CS0703 — a public
  type cannot be constrained on an internal one). The published package would have shipped
  with its only entry point invisible to every consumer, and **all 569 tests still passed**,
  because every test project has `InternalsVisibleTo`. No behavioural suite can catch this
  class of defect; only a test that looks at the artifact a consumer actually receives can.

  It also guards the inverse — a descriptor type accidentally made public becomes API this
  project would then owe compatibility to. Keeping `OpcodeInfo` internal is what lets phase
  4 reshape it freely.

- **Unit tests** per addressing mode, flag, stack wrap, page cross, and interrupt timing.
- **Benchmarks** as a gate: ≥50 MHz simulated 6502 single-threaded with `FlatBus`,
  target 100 MHz.

Conformance runs as its own CI stage — 2.56 M vectors for the 6502 alone is too slow to
sit in the inner loop.

## 8. Machine personalities

Personalities live in `SixtyFiveXX.Machines`. The core keeps zero dependencies.

### 8.1 Fidelity

A personality models **everything the CPU can observe or be delayed by, and nothing
else**: banking and address decoding, I/O register read/write semantics, timers that
raise IRQ/NMI, and bus stealing. No framebuffer, no audio samples, no disk.

This is the level that makes measured cycle counts match real hardware — VIC-II badlines
steal 40–43 cycles per line, which changes exactly the numbers sim6502 asserts on — at a
fraction of the cost of a full machine emulator.

### 8.2 Contract

```csharp
public interface IMachine
{
    string Id { get; }                              // "c64"
    CpuVariant Cpu { get; }                         // Mos6510
    IReadOnlyList<RomSlot> RequiredRoms { get; }    // name, size, SHA-256, load address

    void LoadRom(string slot, ReadOnlySpan<byte> data);
    void Reset();

    void Tick();                                    // phase 1 of a CPU cycle
    ref readonly Pins Pins { get; }                 // Irq, Nmi, Rdy

    byte Read(int address);
    void Write(int address, byte value);
}
```

**Cycle order.** Within each `Cpu.Tick()`, the machine ticks first — advancing CIA
timers and the VIC raster counter, deciding the badline, and setting BA/RDY — and then
the CPU performs its bus access, unless RDY is low and this is a read cycle. Bus
stealing therefore falls out of the `RDY` pin the core already models (§5.3); it is not
a special case anywhere.

**Performance.** A personality ships a small `struct C64Bus : IBus` wrapping its sealed
machine class, registered with `MachineFactory`. `IMachine` is a construction-time
contract and never appears in the hot loop. Personalities unwilling to write the struct
use `RefBus` and pay one virtual call per access.

### 8.3 C64 personality

The proving implementation for the contract. sim6502's existing `C64MemoryMap` models
banking from `$01` alone; this corrects and extends it:

- Full PLA: `LORAM`/`HIRAM`/`CHAREN` together with the `GAME`/`EXROM` cartridge lines,
  producing all 14 memory configurations including Ultimax mode.
- CIA1 and CIA2 timers, TOD, and their IRQ/NMI lines.
- VIC-II to the extent the CPU sees it: raster counter, `$D011`/`$D012`, badline
  detection, sprite DMA, and BA/RDY assertion three cycles ahead of a steal.
- SID and VIC register read semantics, including open-bus behaviour on write-only
  registers.

### 8.4 Certifying a personality

No Harte-equivalent exists for whole machines, so the gate is **differential**. sim6502
already ships VICE and Ultimate64 backends. The same `.prg` fixtures run under
`SixtyFiveXX` + C64 personality, under VICE, and on real hardware via Ultimate64; cycle
counts and memory are diffed. Badline stalls, CIA timer IRQ latency, and PLA banking
either match or they do not.

## 9. Licensing and legal position

**Our code is MIT.** No emulator source is copied. Where behaviour is derived from
another implementation, only permissively licensed sources (MIT, BSD, Apache) are
consulted, and behaviour — not code — is reproduced.

**The C64 PLA is implemented, not distributed.** The banking logic is public: the C64
Programmer's Reference Guide documents it, and the reverse-engineered 906114-01 equations
are widely reproduced. Implementing that logic as C# boolean expressions from public
documentation is standard practice across the emulator ecosystem. A binary fusemap dump
of the 82S100 is a copy of a Commodore part's contents and is **not** included; it is
also unnecessary, since the documented five-input configuration table produces identical
behaviour.

**All ROMs are user-supplied, always.** KERNAL, BASIC, CHARGEN, Apple II/IIe/IIgs ROMs,
NES cartridges. SixtyFiveXX ships a per-personality *manifest* — slot name, expected
size, SHA-256, load address, bank — and validates what the user provides against it.
Hashes and load addresses are facts about a file, not copyrightable expression.

**Mapper, soft-switch, and banking logic is ours**, written from public documentation.

**Test suites** are used under their own licences. Harte's vectors are MIT and may be
vendored; they are fetched rather than committed purely for repository size. Klaus
Dormann's tests are consumed as pre-built binaries, so their GPL licence does not reach
our source.

## 10. Delivery phases

Each phase is gated by green suites, not by a subjective sense of completion.

| # | Deliverable | Gate | Status |
| --- | --- | --- | --- |
| 1 | Micro-op engine, `IBus`, `FlatBus`, 6502 legal opcodes | Harte 6502 legal-opcode subset | **Complete** — 1,510,000 vectors |
| 2 | Undocumented opcodes; interrupts, RDY, SO | **Full** Harte 6502 (all 256); Klaus functional + interrupt | **Complete** — 2,560,000 vectors |
| 3 | `ICpuVariant` refactor; public-surface gate | **Zero behaviour change** — every phase 1–2 suite still green — **plus** the packed-assembly surface test below | |
| 4 | 65C02, three sub-variants | Harte `wdc65c02` / `rockwell65c02` / `synertek65c02`; Klaus 65C02 extended (WDC + Rockwell only) | |
| 5 | 6510 core (`$00`/`$01` port) | Existing 6502 suites for inherited opcodes; VICE `cpuport` for the port delta | |
| 6 | Disassembler; sim6502 adapter | **sim6502 swaps over.** Its own suite green; `sim6502/Proc/` deleted | |
| 7 | 65816 | Harte 65816 | Deferred — no consumer needs it yet |
| 8 | **Performance pass across all cores** | Every optimization: a measured delta **and** every Harte suite still green | |
| 9 | Personality contract; C64 personality | Differential vs VICE and Ultimate64 | |

**Why the performance pass comes after the cores.** All the cores share one engine, so an
optimization written against that engine benefits every variant at once. Optimizing before
the cores exist risks shapes a later variant cannot use, and would mean re-validating each
change against test suites that do not exist yet. Correctness first, across every core,
then one pass that lifts all of them.

That rule is absolute: **no optimization ships without both a measured improvement and a
green run of every conformance suite the project has by then.** A change that is faster
and wrong is a regression.

**Why the swap moved from phase 3 to phase 6.** The original order put the sim6502 swap
first, to get a real consumer against the public API early. Two facts changed it. First,
sim6502 already supports 6502, 6510 **and** 65C02, selectable per test suite — so swapping
before those cores exist would regress a working consumer, or force it to run two CPU
cores side by side. Second, `ICpuVariant` necessarily changes the public API, so an
adapter written before the refactor would be rewritten after it. Building the cores first
costs later API feedback and buys a single clean cutover with full parity.

**Why the variant refactor is its own phase.** `Cpu<TBus>` currently binds the 6502
micro-op table at construction, so no second variant can exist until that changes. Doing
it alone, gated on *zero behaviour change* against 2.56 M vectors and two Klaus programs,
means any regression it introduces is attributable to the refactor and nothing else.

Apple IIe, NES, and Apple IIgs personalities are follow-on work against the contract
frozen in phase 8, and are out of scope for this spec.

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Micro-op indirection costs more than expected | Benchmark gate from phase 1. If the 50 MHz floor is missed, the declarative table can drive a Roslyn source generator emitting a flat per-opcode switch, without changing the table or the tests. |
| Unstable NMOS opcodes (`ANE`, `LXA`, `TAS`) are genuinely analogue and vary by chip | Match the magic constants Harte's vectors encode; document that these are undefined on real silicon. |
| Harte data size (~1 GB) makes CI slow | Cached between runs; conformance is a separate CI stage from unit tests. |
| VIC-II timing is deep enough to become its own project | Fidelity is capped at what the CPU can observe (§8.1). Differential testing against VICE bounds the work: match, or find the specific divergence. |
| 65816 diverges enough to strain the shared engine | It is the last core, deliberately. It gets its own opcode table on the same micro-op loop, which the flat table design already permits, and `CpuState` is widened for every variant so the tick loop needs no special case. If the wider state measurably costs the 8-bit cores in phase 7, the fallback is a separate `Cpu816` sharing the same building blocks. |
| Deferring optimization until phase 7 leaves a slow core in front of a real consumer (phase 3) | The phase 1 baseline is already ~230 MHz simulated with zero allocations — roughly 230× a real C64. sim6502 measures cycle counts, not wall time, so speed is a convenience there, not a requirement. Nothing is blocked by waiting. |
| A phase 7 optimization silently breaks a core whose suite is slower to run | The rule is a green run of *every* suite per change, not a sampled subset. The suites are already wired as separate CI stages precisely so this stays affordable. |
