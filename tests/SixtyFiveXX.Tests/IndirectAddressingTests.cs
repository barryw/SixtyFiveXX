using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class IndirectAddressingTests
{
    [Fact]
    public void LdaIndexedIndirect_TakesSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA1, 0x20);   // LDA ($20,X)
        cpu.State.X = 0x04;
        ram[0x0024] = 0x00;      // pointer low
        ram[0x0025] = 0x40;      // pointer high  → $4000
        ram[0x4000] = 0x6E;

        var cycles = cpu.Step();

        Assert.Equal(0x6E, cpu.State.A);
        Assert.Equal(6, cycles);
    }

    [Fact]
    public void LdaIndexedIndirect_ProducesTheExactBusSequence()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xA1, 0x20);
        cpu.State.X = 0x04;
        ram[0x0024] = 0x00;
        ram[0x0025] = 0x40;
        ram[0x4000] = 0x6E;

        cpu.Step();

        Assert.Equal(
        [
            new BusAccess(0x0200, 0xA1, false),
            new BusAccess(0x0201, 0x20, false),
            new BusAccess(0x0020, 0x00, false),   // dummy read at the unindexed pointer
            new BusAccess(0x0024, 0x00, false),
            new BusAccess(0x0025, 0x40, false),
            new BusAccess(0x4000, 0x6E, false),
        ], log);
    }

    [Fact]
    public void IndexedIndirect_WrapsThePointerWithinPageZero()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA1, 0xFF);   // LDA ($FF,X)
        cpu.State.X = 0x00;
        ram[0x00FF] = 0x34;
        ram[0x0000] = 0x12;      // high byte wraps to $0000, not $0100
        ram[0x0100] = 0xEE;      // must NOT be used
        ram[0x1234] = 0x9D;

        cpu.Step();

        Assert.Equal(0x9D, cpu.State.A);
    }

    [Fact]
    public void LdaIndirectIndexed_TakesFiveCyclesWithoutAPageCross()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB1, 0x30);   // LDA ($30),Y
        cpu.State.Y = 0x10;
        ram[0x0030] = 0x00;
        ram[0x0031] = 0x50;      // → $5000
        ram[0x5010] = 0x1F;

        var cycles = cpu.Step();

        Assert.Equal(0x1F, cpu.State.A);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void LdaIndirectIndexed_TakesSixCyclesAcrossAPage()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xB1, 0x30);
        cpu.State.Y = 0x10;
        ram[0x0030] = 0xF8;
        ram[0x0031] = 0x50;      // → $50F8; +$10 crosses into $51xx
        ram[0x5008] = 0xAA;      // unfixed address
        ram[0x5108] = 0xBB;      // corrected address

        var cycles = cpu.Step();

        Assert.Equal(0xBB, cpu.State.A);
        Assert.Equal(6, cycles);
        Assert.Equal(
        [
            new BusAccess(0x0200, 0xB1, false),
            new BusAccess(0x0201, 0x30, false),
            new BusAccess(0x0030, 0xF8, false),
            new BusAccess(0x0031, 0x50, false),
            new BusAccess(0x5008, 0xAA, false),   // dummy read at the unfixed address
            new BusAccess(0x5108, 0xBB, false),
        ], log);
    }

    [Fact]
    public void StaIndirectIndexed_AlwaysTakesSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x91, 0x30);   // STA ($30),Y
        cpu.State.Y = 0x01;
        cpu.State.A = 0x7C;
        ram[0x0030] = 0x00;
        ram[0x0031] = 0x60;

        var cycles = cpu.Step();

        Assert.Equal(0x7C, ram[0x6001]);
        Assert.Equal(6, cycles);
    }

    [Fact]
    public void IndirectIndexed_WrapsThePointerWithinPageZero()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB1, 0xFF);   // LDA ($FF),Y
        cpu.State.Y = 0x00;
        ram[0x00FF] = 0x78;
        ram[0x0000] = 0x56;      // high byte wraps to $0000
        ram[0x0100] = 0xEE;      // must NOT be used
        ram[0x5678] = 0x4B;

        cpu.Step();

        Assert.Equal(0x4B, cpu.State.A);
    }
}
