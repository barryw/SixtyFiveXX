# SixtyFiveXX

A clean-room, cycle-stepped emulator for the 65xx processor family, in C#.

Every `Tick()` advances the core by exactly one clock cycle and performs at most one bus
access, including the dummy reads and dummy writes real silicon performs. Cycle counts
are not tabulated after the fact — they fall out of doing the work.

## Status

| Variant | State | Certified against |
| --- | --- | --- |
| 6502, documented opcodes | Complete | Harte SingleStepTests, per-cycle |
| 6502, undocumented opcodes | Complete | Harte SingleStepTests, per-cycle |
| 65C02 (Rockwell, Synertek, WDC) | Phase 4 | — |
| 6510 | Phase 5 | — |
| 65816 | Phase 7 | — |

IRQ, NMI, RDY and SO are Phase 2 — the core currently models `Reset()` and `BRK` only.

See `docs/superpowers/specs/` for the design and `docs/superpowers/plans/` for the
implementation plans.

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

## Conformance vectors

Vectors from [SingleStepTests/65x02](https://github.com/SingleStepTests/65x02) (MIT) are
downloaded on first use and cached under `tests/SixtyFiveXX.Conformance/.harte-cache/`.
They are never committed. To run offline, clone that repository and set
`SIXTYFIVEXX_HARTE_DIR` to point at it.

## Licence

MIT. No ROM images, PLA dumps, or emulator source from other projects are included or
distributed.
