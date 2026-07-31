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
| Bus abstraction | `Cpu<TBus> where TBus : struct, IBus` | JIT specializes and inlines every bus access — no virtual call on the hottest path |
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

public sealed class Cpu<TBus> where TBus : struct, IBus
```

A `struct` type parameter makes the JIT emit a specialized `Cpu<TBus>` per bus type, so
bus accesses inline to direct array indexing. Consumers name their bus type.

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
- NMI hijacking a BRK or IRQ sequence when it arrives during vector fetch.
- `RDY` halting the CPU on read cycles only — writes complete regardless. This is the
  mechanism personalities use for bus stealing.
- `SO` pin setting the overflow flag.

### 5.4 Variants

| Variant | Delta from baseline |
| --- | --- |
| `Mos6502` | NMOS baseline. All 105 undocumented opcodes, including the unstable `ANE`/`LXA`/`TAS`/`SHA`/`SHX`/`SHY` magic-constant behaviour. BCD `ADC`/`SBC` with real, undefined N/V/Z. |
| `Mos6510` | 6502 plus the on-chip `$00` DDR and `$01` port registers. These are CPU registers, so they live in the **core**, not in a bus. |
| `Wdc65C02` | New opcodes; `JMP ($xxFF)` page-wrap bug fixed; `D` cleared on interrupt; corrected BCD flags at the cost of one cycle; per-opcode NOP timing table. Sub-flags: `Rockwell` (`RMB`/`SMB`/`BBR`/`BBS`), `Wdc` (adds `WAI`/`STP`), `Synertek`. Harte tests these three separately. |
| `W65C816` | 16-bit `A`/`X`/`Y`, `M`/`X`/`E` mode bits, 24-bit addressing, `DBR`/`PBR`/`DP`, `MVN`/`MVP` block moves, `COP`, and the extra cycle when the direct-page low byte is non-zero. A near-separate opcode table on the same engine. |

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
| **Wolfgang Lorenz** C64 suite | Freely distributed | NMOS undocumented opcodes, timing, `$00`/`$01` port | Needs a minimal KERNAL shim to host it. |
| **Bruce Clark** decimal test | Freely distributed | Exhaustive BCD `ADC`/`SBC`, both NMOS and CMOS semantics | Small; catches the classic decimal bugs. |

Plus:

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

| # | Deliverable | Gate |
| --- | --- | --- |
| 1 | Micro-op engine, `IBus`, `FlatBus`, 6502 legal opcodes | Harte 6502 legal-opcode subset |
| 2 | Undocumented opcodes; interrupts, RDY, SO | **Full** Harte 6502; Klaus functional; Bruce Clark |
| 3 | Disassembler; sim6502 adapter | **sim6502 swaps over.** Its own suite green; `sim6502/Proc/` deleted |
| 4 | 65C02, three sub-variants | Harte 65c02 ×3; Klaus 65C02 extended |
| 5 | 6510 core (`$00`/`$01` port) | Wolfgang Lorenz suite |
| 6 | Personality contract; C64 personality | Differential vs VICE and Ultimate64 |
| 7 | 65816 | Harte 65816 |

Phase 3 is the point at which the current emulator is replaced. Nothing before it depends
on personalities, and nothing in phases 1–5 is blocked by them.

Apple IIe, NES, and Apple IIgs personalities are follow-on work against the contract
frozen in phase 6, and are out of scope for this spec.

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| Micro-op indirection costs more than expected | Benchmark gate from phase 1. If the 50 MHz floor is missed, the declarative table can drive a Roslyn source generator emitting a flat per-opcode switch, without changing the table or the tests. |
| Unstable NMOS opcodes (`ANE`, `LXA`, `TAS`) are genuinely analogue and vary by chip | Match the magic constants Harte's vectors encode; document that these are undefined on real silicon. |
| Harte data size (~1 GB) makes CI slow | Cached between runs; conformance is a separate CI stage from unit tests. |
| VIC-II timing is deep enough to become its own project | Fidelity is capped at what the CPU can observe (§8.1). Differential testing against VICE bounds the work: match, or find the specific divergence. |
| 65816 diverges enough to strain the shared engine | It is last, deliberately. If the engine cannot carry it cleanly, it gets its own opcode table on the same micro-op loop — which the flat table design already permits. |
