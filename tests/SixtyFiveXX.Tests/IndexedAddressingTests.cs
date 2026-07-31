using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class IndexedAddressingTests
{
    [Fact]
    public void LdaAbsoluteX_TakesFourCyclesWithoutAPageCross()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBD, 0x00, 0x30);   // LDA $3000,X
        cpu.State.X = 0x10;
        ram[0x3010] = 0x77;

        var cycles = cpu.Step();

        Assert.Equal(0x77, cpu.State.A);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void LdaAbsoluteX_TakesFiveCyclesAcrossAPageAndReadsTheUnfixedAddressFirst()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xBD, 0xF0, 0x30);   // LDA $30F0,X
        cpu.State.X = 0x20;
        ram[0x3010] = 0xAA;   // the unfixed (wrong) address: $30F0 + $20 keeps the old high byte
        ram[0x3110] = 0xBB;   // the corrected address

        var cycles = cpu.Step();

        Assert.Equal(0xBB, cpu.State.A);
        Assert.Equal(5, cycles);
        Assert.Equal(
        [
            new BusAccess(0x0200, 0xBD, false),
            new BusAccess(0x0201, 0xF0, false),
            new BusAccess(0x0202, 0x30, false),
            new BusAccess(0x3010, 0xAA, false),   // dummy read at the unfixed address
            new BusAccess(0x3110, 0xBB, false),
        ], log);
    }

    [Fact]
    public void StaAbsoluteX_AlwaysTakesFiveCyclesAndAlwaysDummyReads()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0x9D, 0x00, 0x30);   // STA $3000,X
        cpu.State.X = 0x10;
        cpu.State.A = 0x5A;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(
        [
            new BusAccess(0x0200, 0x9D, false),
            new BusAccess(0x0201, 0x00, false),
            new BusAccess(0x0202, 0x30, false),
            new BusAccess(0x3010, 0x00, false),   // dummy read even without a page cross
            new BusAccess(0x3010, 0x5A, true),
        ], log);
    }

    [Fact]
    public void IncAbsoluteX_AlwaysTakesSevenCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xFE, 0x00, 0x30);   // INC $3000,X
        cpu.State.X = 0x05;
        ram[0x3005] = 0x10;

        var cycles = cpu.Step();

        Assert.Equal(0x11, ram[0x3005]);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void LdaAbsoluteY_TakesFiveCyclesAcrossAPage()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB9, 0xFF, 0x20);   // LDA $20FF,Y
        cpu.State.Y = 0x01;
        ram[0x2100] = 0x3C;

        var cycles = cpu.Step();

        Assert.Equal(0x3C, cpu.State.A);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void LdaZeroPageX_WrapsWithinPageZeroAndDummyReadsTheUnindexedAddress()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xB5, 0xF0);   // LDA $F0,X
        cpu.State.X = 0x20;
        ram[0x0010] = 0x64;    // ($F0 + $20) & $FF = $10
        ram[0x0110] = 0xFF;    // must NOT be read

        var cycles = cpu.Step();

        Assert.Equal(0x64, cpu.State.A);
        Assert.Equal(4, cycles);
        Assert.Equal(
        [
            new BusAccess(0x0200, 0xB5, false),
            new BusAccess(0x0201, 0xF0, false),
            new BusAccess(0x00F0, 0x00, false),   // dummy read at the unindexed address
            new BusAccess(0x0010, 0x64, false),
        ], log);
    }

    [Fact]
    public void LdxZeroPageY_IndexesByY()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB6, 0x80);   // LDX $80,Y
        cpu.State.Y = 0x04;
        ram[0x0084] = 0x2B;

        var cycles = cpu.Step();

        Assert.Equal(0x2B, cpu.State.X);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void IndexedAddressing_WrapsAtTheTopOfMemory()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBD, 0xFF, 0xFF);   // LDA $FFFF,X
        cpu.State.X = 0x01;
        ram[0x0000] = 0x5E;

        var cycles = cpu.Step();

        Assert.Equal(0x5E, cpu.State.A);
        Assert.Equal(5, cycles);
    }
}
