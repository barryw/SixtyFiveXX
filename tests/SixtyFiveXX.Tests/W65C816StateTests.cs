using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

public class W65C816StateTests
{
    /// <summary>
    /// Hardware resets the 65816 into EMULATION mode, not native. CpuState.E defaults to
    /// false, which is native, so the reset sequence must set it explicitly — and with it
    /// the invariants emulation mode forces: m and x set, XH and YH cleared, SH = $01.
    /// </summary>
    [Fact]
    public void Reset_EntersEmulationModeWithItsInvariants()
    {
        var ram = new byte[0x10000];
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0xC0;

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.X = 0x1234;
        cpu.State.Y = 0x5678;
        cpu.State.S = 0x0000;

        cpu.Reset();
        cpu.Step();

        Assert.True(cpu.State.E);
        Assert.True(cpu.State.M);
        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x0034, cpu.State.X);       // XH forced to $00
        Assert.Equal(0x0078, cpu.State.Y);
        Assert.Equal(0x01, cpu.State.S >> 8);    // SH forced to $01
        Assert.Equal(0xC000, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);       // reset clears the program bank
        Assert.Equal(0x00, cpu.State.DBR);
        Assert.Equal(0x0000, cpu.State.DP);
    }

    [Fact]
    public void UnimplementedOpcode_Throws()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0xEA;                      // NOP — not in phase 7b's slice

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.PC = 0xC000;

        Assert.Throws<UndefinedOpcodeException>(() => cpu.Step());
    }
}
