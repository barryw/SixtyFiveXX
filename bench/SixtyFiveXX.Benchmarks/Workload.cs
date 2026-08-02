using SixtyFiveXX.Variants;

namespace SixtyFiveXX.Benchmarks;

/// <summary>
/// A short 6502 loop that exercises a representative mix of addressing modes so the
/// benchmark measures dispatch rather than one hot opcode.
/// </summary>
public static class Workload
{
    /// <summary>Builds a CPU running the benchmark loop, ready to tick.</summary>
    public static Cpu<FlatBus, Mos6502Variant> Build()
    {
        var ram = new byte[0x10000];

        byte[] program =
        [
            0xA9, 0x01,        // LDA #$01          immediate
            0x85, 0x10,        // STA $10           zero page write
            0xA5, 0x10,        // LDA $10           zero page read
            0xAD, 0x00, 0x30,  // LDA $3000         absolute read
            0xBD, 0x00, 0x30,  // LDA $3000,X       indexed read
            0x9D, 0x00, 0x31,  // STA $3100,X       indexed write
            0xA1, 0x20,        // LDA ($20,X)       indexed indirect
            0xB1, 0x22,        // LDA ($22),Y       indirect indexed
            0xEE, 0x00, 0x32,  // INC $3200         read-modify-write
            0x69, 0x05,        // ADC #$05
            0xE8,              // INX
            0xC8,              // INY
            0xD0, 0xE4,        // BNE -28           back to the top
            0x4C, 0x00, 0x02,  // JMP $0200         restart when the branch falls through
        ];
        program.CopyTo(ram, 0x0200);

        // Pointers for the indirect modes, chosen so they stay inside RAM.
        ram[0x0020] = 0x00; ram[0x0021] = 0x34;
        ram[0x0022] = 0x00; ram[0x0023] = 0x35;

        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = 0x0200;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;
        return cpu;
    }
}
