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

    /// <summary>
    /// WDC datasheet reset initialisation table (p. 15, §2.25; research document §10): the
    /// P-register row pins <c>D</c> to 0, while <c>N</c>, <c>V</c> and <c>Z</c> are marked
    /// "not initialized" — as is <c>A</c>, in the register row. Distinct from
    /// <see cref="Reset_EntersEmulationModeWithItsInvariants"/>, which covers the invariants
    /// emulation mode itself forces (<c>E</c>, <c>M</c>, <c>X</c>, <c>XH</c>/<c>YH</c>,
    /// <c>SH</c>); <c>D</c> is not part of that invariant; it is pinned separately.
    /// </summary>
    [Fact]
    public void Reset_ClearsDecimalModeButLeavesNVZAndAUntouched()
    {
        var ram = new byte[0x10000];
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0xC0;

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.D = true;
        cpu.State.N = true;
        cpu.State.V = true;
        cpu.State.Z = true;
        cpu.State.A = 0x1234;

        cpu.Reset();
        cpu.Step();

        Assert.False(cpu.State.D);
        Assert.True(cpu.State.N);
        Assert.True(cpu.State.V);
        Assert.True(cpu.State.Z);
        Assert.Equal(0x1234, cpu.State.A);
    }

    /// <summary>
    /// <see cref="Reset_EntersEmulationModeWithItsInvariants"/> only checks the stack
    /// pointer after all seven reset cycles finish. This pins the invariant at each
    /// individual decrement of an actual stack-touching operation instead — proving the S8
    /// setter itself wraps at the page-one boundary (research document §7), not merely that
    /// the net three-decrement result happens to land there. Reset's own dummy stack reads
    /// are the only 65816 stack-touching micro-op sequence this phase implements; no
    /// push/pull opcode exists yet (<c>Emit816</c> is still empty).
    /// </summary>
    [Fact]
    public void S8_WrapsAtEachCycleOfAnActualStackOperationWhileEIsSet()
    {
        var ram = new byte[0x10000];
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0xC0;

        var cpu = new Cpu<FlatBus, W65C816Variant>(new FlatBus(ram));
        cpu.State.S = 0x0100;                    // S8 = $00: the next decrement must wrap

        cpu.Reset();
        cpu.Tick();                              // 1st IntDummy
        cpu.Tick();                              // 2nd IntDummy

        cpu.Tick();                              // 1st StackDummyReadDec: S8-- from $00
        Assert.True(cpu.State.E);
        Assert.Equal(0x01FF, cpu.State.S);       // wraps to $FF, stays in page 1

        cpu.Tick();                              // 2nd: S8-- from $FF
        Assert.Equal(0x01FE, cpu.State.S);

        cpu.Tick();                              // 3rd: S8-- from $FE
        Assert.Equal(0x01FD, cpu.State.S);
    }
}
