# SixtyFiveXX

A clean-room, cycle-stepped emulator for the 65xx processor family, in C#.

Every `Tick()` advances the core by exactly one clock cycle and performs at most one bus
access, including the dummy reads and dummy writes real silicon performs. Cycle counts
are not tabulated after the fact — they fall out of doing the work.

## Install

    dotnet add package SixtyFiveXX

Targets `net8.0` and `net10.0`. Zero dependencies.

## Use

```csharp
var ram = new byte[0x10000];
var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));

cpu.Reset();
cpu.Step();              // the 7-cycle reset sequence

cpu.Step();              // one instruction
cpu.Run(1_000_000);      // a million cycles
cpu.RunUntil(c => c.State.PC == 0xC000, maxCycles: 50_000);
```

The variant is a type parameter too, so a core carries no runtime test for which
processor it is. Swap `Mos6502Variant` for `Mos6510Variant`, `Synertek65C02Variant`,
`Rockwell65C02Variant` or `Wdc65C02Variant`.

## Disassembly

```csharp
var instruction = Disassembler.Decode<FlatBus, Mos6502Variant>(cpu.Bus, 0xC000);

instruction.Mnemonic;    // "LDA"
instruction.Operand;     // "$1234,X"
instruction.Length;      // 3
instruction.ToString();  // "LDA $1234,X"
```

Driven by the same opcode table the engine runs from, so mnemonic and operand text cannot
drift from what the engine executes. For the five 8-bit cores, adding an opcode makes it
decodable in the same commit that makes it executable, without exception. That does not yet
hold for the 65816: 12 of its 15 addressing modes have no operand-format case here, so 20 of
phase 7b's 32 implemented opcodes throw `NotSupportedException` on decode, and `LDA #`
decodes at a fixed 2 bytes regardless of `m`, which is wrong once the accumulator is 16
bits. Disassembler support for the 65816 is phase 7e's job. Walk memory by `Length`.
Branches show the address they land on rather than the displacement they encode, and `BRK`
is two bytes because its second byte is fetched and discarded rather than executed.

`TBus` is a `struct` type parameter so the JIT specializes the core and inlines every
memory access. Implement your own `struct` bus for address decoding, or wrap an
existing `IBus` reference in `RefBus` and pay one virtual call per access.

## Status

| Variant | State | Certified against |
| --- | --- | --- |
| 6502, documented opcodes | Complete | Harte SingleStepTests, per-cycle |
| 6502, undocumented opcodes | Complete | Harte SingleStepTests, per-cycle |
| 65C02 Synertek | Complete | Harte SingleStepTests, per-cycle |
| 65C02 Rockwell | Complete | Harte SingleStepTests + Klaus 65C02 extended |
| 65C02 WDC | Complete | Harte SingleStepTests + Klaus 65C02 extended |
| 6510 | Complete | The 6502 suites for the inherited opcodes + VICE `cpuport/test1` for the `$00`/`$01` port |
| 65816 | Phase 7b: 32 of 256 opcodes | Harte SingleStepTests/65816, per-cycle, including bus-qualifier pins |

IRQ and NMI (hardware-correct sampling, edge latching, and BRK/NMI hijacking), the RDY
halt line, and the SO pin are complete, alongside `Reset()` and `BRK`.

The 65816 is being built in five phases; two are done. Phase 7a widened `CpuState` to carry
the 65816's register file on **every** variant — 16-bit `A`, `X`, `Y` and `S`, plus `DP`,
`DBR`, `PBR` and `E` — and added `IBus.Internal(int)` for the cycles that drive an address
without accessing memory, which no 8-bit core has. The 8-bit cores use the low bytes and are
unchanged: the full conformance suite passes identically either side of the widening, and
throughput is unaffected.

Phase 7b landed the addressing engine: `LDA` and `STA` are decodable and cycle-correct across
all fifteen 65816 addressing modes, plus `XCE`, `REP` and `SEP` — 32 opcodes in total,
certified per-cycle in both emulation and native mode against 640,000 SingleStepTests
vectors, including the full eight-character bus-qualifier pin string asserted on every cycle.

**This is not a complete core.** 98 of the 65816's 256 opcodes are implemented — phase 7b's 32,
plus phase 7c's in-progress bulk work, which has so far added `ORA`, `AND`, `EOR` and `CMP` in
all fifteen addressing forms each, and `CPX` and `CPY` in three each. Every other opcode throws
`UndefinedOpcodeException`. Phases 7c and 7d add the rest.

**The state widening is a breaking change**: `CpuState.A`, `X`, `Y` and `S` are now `ushort`
rather than `byte`.

See [`docs/superpowers/specs/`](https://github.com/barryw/SixtyFiveXX/tree/main/docs/superpowers/specs)
for the design and
[`docs/superpowers/plans/`](https://github.com/barryw/SixtyFiveXX/tree/main/docs/superpowers/plans)
for the implementation plans.

## Conformance vectors

Vectors from [SingleStepTests/65x02](https://github.com/SingleStepTests/65x02) (MIT) are
downloaded on first use and cached under `tests/SixtyFiveXX.Conformance/.harte-cache/`.
They are never committed. One set per core, so a full run needs roughly **3.8 GB**. To
run offline — or to avoid the download entirely — clone that repository and set
`SIXTYFIVEXX_HARTE_DIR` to point at it.

The 65816 draws from a **different repository**,
[SingleStepTests/65816](https://github.com/SingleStepTests/65816) (MIT), also downloaded on
first use and never committed. Files are named `{opcode:x2}.e.json` (emulation mode) and
`{opcode:x2}.n.json` (native mode), 10,000 vectors each; the full set adds roughly **2.9 GB**
on top of the 3.8 GB above. `SIXTYFIVEXX_HARTE_DIR` works for this set too, but the layout it
expects is a level deeper: the variable must point at the *parent* of a `65816` checkout, not
at the checkout itself, because vectors are read from
`$SIXTYFIVEXX_HARTE_DIR/65816/v1/{opcode:x2}.{e,n}.json`.

`WAI` and `STP` are the only two opcodes with no vectors: SingleStepTests ships empty
files for them, because an instruction that halts cannot be expressed as a
before-and-after pair. They are covered by unit tests instead, and the conformance run
names them in its output rather than passing over them silently.

**The 6510 has no vector set**, so its certification is a weaker claim than the other
cores' and is worth stating plainly. SingleStepTests has no `6510` suite anywhere, so the
inherited instruction set is covered by running the 6502's vectors against the 6510 core —
minus the 12,658 of 2,560,000 that use `$00` or `$01` as ordinary memory and therefore
describe a 6502 rather than a 6510 — and the port itself by VICE's `cpuport/test1`, 136
bytes that check bit 7 only. Unit tests cover the rest of the port.

Klaus Dormann's interrupt test also needs `64tass` installed and
`tests/SixtyFiveXX.Conformance/klaus/build.sh` run once to produce its binary; without
that, `dotnet test tests/SixtyFiveXX.Conformance` fails outright on a fresh clone.

## Licence

MIT. No ROM images, PLA dumps, or emulator source from other projects are included or
distributed. A GPL-3.0 **test program** — Klaus Dormann's interrupt test — is committed
under `tests/SixtyFiveXX.Conformance/klaus/`; it is assembled and executed, not linked
against or derived from, so its licence does not reach this project's code. See
[`tests/SixtyFiveXX.Conformance/klaus/README.md`](https://github.com/barryw/SixtyFiveXX/blob/main/tests/SixtyFiveXX.Conformance/klaus/README.md)
for the full argument.
