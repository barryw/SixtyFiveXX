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
var cpu = new Cpu<FlatBus>(new FlatBus(ram));

cpu.Reset();
cpu.Step();              // the 7-cycle reset sequence

cpu.Step();              // one instruction
cpu.Run(1_000_000);      // a million cycles
cpu.RunUntil(c => c.State.PC == 0xC000, maxCycles: 50_000);
```

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
| 65816 | Phase 7 | — |

IRQ and NMI (hardware-correct sampling, edge latching, and BRK/NMI hijacking), the RDY
halt line, and the SO pin are complete, alongside `Reset()` and `BRK`.

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
