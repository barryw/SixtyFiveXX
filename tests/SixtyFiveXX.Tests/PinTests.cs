using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class PinTests
{
    [Fact]
    public void Rdy_HaltsTheProcessorOnAReadCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);   // LDA $1234
        ram[0x1234] = 0x42;

        cpu.Tick();                 // opcode fetch
        cpu.SetRdy(false);

        var pcBefore = cpu.State.PC;
        for (var i = 0; i < 10; i++) cpu.Tick();

        Assert.Equal(pcBefore, cpu.State.PC);      // no progress while halted
        Assert.False(cpu.State.A == 0x42);

        cpu.SetRdy(true);
        cpu.Step();

        Assert.Equal(0x42, cpu.State.A);           // resumes and completes
    }

    [Fact]
    public void Rdy_StillDrivesTheAddressBusWhileHalted()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0xAD, 0x34, 0x12);

        cpu.Tick();
        var afterFetch = log.Count;
        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(afterFetch + 2, log.Count);   // one access per halted cycle
        Assert.All(log, a => Assert.False(a.IsWrite));
    }

    [Fact]
    public void Rdy_DoesNotHaltAWriteCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x8D, 0x00, 0x30);   // STA $3000
        cpu.State.A = 0x5A;

        cpu.Tick();   // opcode
        cpu.Tick();   // address low
        cpu.Tick();   // address high
        cpu.SetRdy(false);
        cpu.Tick();   // the write must complete despite RDY being low

        Assert.Equal(0x5A, ram[0x3000]);
    }

    [Fact]
    public void Rdy_CountsHaltedCyclesAgainstTheCycleCounter()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(2, cpu.Cycles);
    }

    [Fact]
    public void Ready_ReportsTheCurrentPinState()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        Assert.True(cpu.Ready);
        cpu.SetRdy(false);
        Assert.False(cpu.Ready);
    }

    [Fact]
    public void So_SetsTheOverflowFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.V = false;

        cpu.SetSo();

        Assert.True(cpu.State.V);
    }

    [Fact]
    public void So_DoesNotDisturbAnyOtherFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.P = Flag.U | Flag.C | Flag.N;

        cpu.SetSo();

        Assert.Equal(Flag.U | Flag.C | Flag.N | Flag.V, cpu.State.P);
    }
}
