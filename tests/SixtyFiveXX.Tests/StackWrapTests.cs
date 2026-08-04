using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The stack pointer is 8 bits on every core before the 65816, and it wraps at 8 bits.
/// Widening <c>CpuState.S</c> to a <c>ushort</c> puts that at risk: a bare <c>S--</c> on a
/// 16-bit field takes $00 to $FFFF rather than $FF, and the push then lands at the wrong
/// address. Measured, not hypothesised — bypassing the byte shim reproduces exactly that.
/// </summary>
public class StackWrapTests
{
    [Fact]
    public void Push_WrapsTheStackPointerAtEightBits()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0x48;                 // PHA

        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;
        cpu.State.S = 0x00;
        cpu.State.A = 0x42;

        cpu.Step();

        Assert.Equal(0x42, ram[0x0100]);    // written at $0100 + S, with S still $00
        Assert.Equal(0xFF, cpu.State.S);    // then wrapped to $FF, not $FFFF
    }

    [Fact]
    public void Pull_WrapsTheStackPointerAtEightBits()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0x68;                 // PLA
        ram[0x0100] = 0x37;

        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;
        cpu.State.S = 0xFF;

        cpu.Step();

        Assert.Equal(0x37, cpu.State.A);    // read from $0100 + (S + 1), wrapping to $00
        Assert.Equal(0x00, cpu.State.S);
    }
}
