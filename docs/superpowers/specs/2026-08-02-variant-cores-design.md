# SixtyFiveXX — variant cores and the sim6502 swap

**Goal:** Take the engine from one certified core to four, then replace the emulator
sim6502 uses, with full parity and `sim6502/Proc/` deleted.

**Status:** design approved 2026-08-02.

**Scope:** `Mos6502` (complete), `Mos6510`, and the three 65C02 sub-variants
(`Wdc65C02`, `Rockwell65C02`, `Synertek65C02`). **The 65816 is deferred** — nothing
consumes it, and its 16-bit state would force re-certifying three cores that already pass.

This spec sits under `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md`, whose §10
phase table and §7 suite table were amended alongside it. Where the two disagree, the
architecture spec governs.

## Established facts — verified, do not re-derive

**What sim6502 actually needs** (from a full read of both repositories):

- sim6502's current core is a **vendored hand-written fork of Aaron Mell's BSD-licensed
  `6502Net`** — `sim6502/Proc/Processor.cs`, 3,862 lines. Not a package.
- Production code does **not** touch `Processor` directly. It goes through
  `IExecutionBackend` — **21 members** covering memory, registers, flags, execution and
  trace — implemented by `SimulatorBackend`, which reaches ~28 `Processor` members. Only
  test code reaches `Processor` directly, via a `.Proc` escape hatch.
- sim6502 has **six** backends (`sim`, `vice`, `novavm`, `verilator`, `u64sim`, `u64`).
  Only `sim` touches the CPU core, so the swap is narrowly scoped.
- **Variant support is real and active**: `ProcessorType.MOS6502 / MOS6510 / WDC65C02`,
  selectable per test suite from the DSL. 6510 gets I/O-port emulation. 65C02 is partial
  (STZ, PHX/PLX/PHY/PLY, BRA, `(zp)`) and growing.
- sim6502's core **throws on unknown opcodes**. SixtyFiveXX has all 105 undocumented ones
  certified, so the swap is an upgrade on that axis.
- `IMemoryMap` is already an abstraction, and it is selected at runtime — which suits
  `RefBus`, not a struct bus baked in at compile time.

**What the gates can actually be** (each downloaded and inspected, not taken from a wiki):

- **Harte 65C02 sets exist and are genuinely three sets.** `SingleStepTests/65x02`
  publishes `wdc65c02`, `rockwell65c02` and `synertek65c02`, 256 × 10,000 MIT vectors
  each, independently generated rather than one set relabelled. Same JSON shape
  `HarteCache.cs` already consumes.
- **There is no 6510 vector set** anywhere in the SingleStepTests organisation.
- **Klaus's 65C02 extended test is prebuilt and downloadable** from the URL `KlausCache`
  already uses. Entry `$0400`, success trap `$24F1`. It exercises the WDC/Rockwell bit
  operations, **skips `WAI`/`STP` entirely, and would fail Synertek.**
- **The Wolfgang Lorenz suite cannot certify the 6510 delta on a bare core.** Its
  `MMU`/`MMUFETCH` tests assert on real Commodore ROM bytes seen through C64 banking, and
  `CPUPORT` tests board-level analog decay, not silicon. Tom Harte's CLK harness excludes
  exactly those three for that reason.
- **VICE's `testprogs/CPU/cpuport/test1` can.** Small, deterministic, GPL, and needs no
  KERNAL, CIA or VIC-II.

## Phase 3 — the `ICpuVariant` refactor

`Cpu<TBus>` binds `MicroOpTable` at construction, so no second variant can exist. This
phase changes that and **nothing else**.

The variant must be resolved at compile time, not through a runtime branch per micro-op —
the engine's whole performance argument is that the tick loop is monomorphic. The shape is
a second type parameter carrying the variant's opcode table and its behavioural flags, so
the JIT specialises one loop per variant and folds away the checks a given core never
takes.

**The gate is zero behaviour change.** Every phase 1–2 suite must stay green: 2,560,000
Harte vectors, Klaus functional, Klaus interrupt, and the full unit suite on both TFMs.
A refactor that is gated on "nothing changed" makes any regression attributable to it
alone — which is the entire reason it is a phase rather than the first commit of phase 4.

Deliberately **not** in this phase: any new opcode, any variant behaviour, any public API
addition beyond what selecting a variant requires.

## Phase 4 — the 65C02 family

Three sub-variants over one shared CMOS baseline.

**Shared CMOS deltas from NMOS:**
- New opcodes and addressing modes: `STZ`, `PHX`/`PLX`/`PHY`/`PLY`, `BRA`, `TRB`/`TSB`,
  `(zp)` indirect, `BIT` immediate and indexed, `INC`/`DEC` accumulator, `JMP (abs,X)`.
- `JMP ($xxFF)` page-wrap bug **fixed**.
- `D` **cleared** on interrupt entry — the opposite of NMOS, which this project's
  interrupt tests currently assert.
- BCD `ADC`/`SBC` produce correct N/V/Z, at the cost of one extra cycle.
- Undocumented NMOS opcodes become NOPs, with a **per-opcode timing table** — they are not
  uniformly one cycle.
- Read-modify-write instructions do a dummy **read** where NMOS does a dummy write.

**Sub-variant deltas:**

| Variant | Adds |
| --- | --- |
| `Synertek65C02` | Base CMOS set only |
| `Rockwell65C02` | `RMB`/`SMB`/`BBR`/`BBS` |
| `Wdc65C02` | Rockwell's set plus `WAI` and `STP` |

**Gates:** Harte `wdc65c02`, `rockwell65c02` and `synertek65c02` — 2,560,000 vectors each,
7,680,000 in total. Plus Klaus 65C02 extended for **WDC and Rockwell only**; Synertek is
carried by Harte alone, because the Klaus program uses bit operations Synertek lacks.

`WAI` and `STP` are gated by Harte and unit tests only. `WAI` halts until an interrupt,
which interacts with the RDY and interrupt machinery from phase 2b and deserves
cycle-exact unit tests of its own.

## Phase 5 — the 6510

A 6502 plus two on-chip registers. These are **CPU** registers, not bus addresses: `$00`
is the data-direction register and `$01` the I/O port, and they are intercepted by the
core before any bus access.

The subtlety is that reads of `$01` combine driven and undriven bits according to `$00`,
and unconnected pins on real hardware decay. **The decay is a deliberate non-goal** —
VICE's own authors describe its timing as guesswork, and this project already takes that
posture toward the unstable NMOS opcodes' magic constants. `bitfade` and `delaytime` are
out of scope and will be recorded as such.

**Gates:** the inherited opcodes need no new suite — once `ICpuVariant` lands, the
existing 6502 Harte and Klaus gates run against the 6510 unchanged. The `$00`/`$01` delta
is certified by VICE's `cpuport` test, executed the same way Klaus's programs are.

## Phase 6 — disassembler and the sim6502 swap

**The disassembler** decodes from any `IBus` for a given variant, driven by the **same
opcode table as the engine**, so the two cannot drift. sim6502's DSL exposes a live
`trace=true`, which has no equivalent in SixtyFiveXX today and is the reason this is a
prerequisite rather than a nicety.

**The adapter** reimplements `SimulatorBackend` against SixtyFiveXX:

- `IExecutionBackend` is **unchanged** — 21 members, and sim6502's own tests gate it.
- `IMemoryMap` → `IBus` through `RefBus`, which already exists and accepts runtime
  polymorphism.
- `ExecuteJsr(address, stopOnAddress, stopOnRts, failOnBrk)` → `RunUntil` plus stack-depth
  tracking **in the adapter**. The core stays a CPU.
- Trace strings → the disassembler.
- `ProcessorType` → `CpuVariant`.

**Gate:** sim6502's own suite green, then `sim6502/Proc/` deleted along with the Aaron
Mell BSD notice in its `NOTICE`.

## Risks

- **`D` cleared on interrupt is a direct contradiction of an existing assertion.** Phase
  2b's `Irq_DoesNotClearDecimalModeOnNmos` pins the NMOS behaviour. The variant split must
  make both true simultaneously; getting it wrong breaks a passing test in a way that
  looks like a regression rather than a variant difference.
- **7.68 M additional vectors** roughly quadruples conformance runtime. The suite already
  takes ~50 s per TFM per set; this needs measuring, and may force per-variant CI stages.
- **The refactor is the highest-risk change in the plan** despite adding no features:
  it touches the hot path of a core certified against millions of vectors, and its gate is
  the absence of change, which is harder to reason about than a new assertion.
- **`WAI` and `STP` have the thinnest coverage** of anything in phase 4 — Harte plus
  whatever unit tests we write, with no Klaus program exercising them.
- **The 6510 port delta rests on a single external test.** If VICE's `cpuport` turns out
  to need more of a C64 than expected, phase 5 loses its only independent oracle and falls
  back to unit tests alone — which this project does not accept as certification.

## Out of scope

- The 65816, and the 16-bit `CpuState` widening it implies.
- Machine personalities, including the C64 — phase 9 in the architecture spec.
- The performance pass — phase 8, deliberately after all cores exist.
- `bitfade` / `delaytime` analog decay behaviour.
- Migrating sim6502's other five backends; only `sim` touches the CPU.
