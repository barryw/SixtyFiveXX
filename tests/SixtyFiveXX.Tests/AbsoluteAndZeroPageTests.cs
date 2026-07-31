using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class AbsoluteAndZeroPageTests
{
    [Fact]
    public void LdaZeroPage_ReadsAndTakesThreeCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA5, 0x42);
        ram[0x0042] = 0x37;

        var cycles = cpu.Step();

        Assert.Equal(0x37, cpu.State.A);
        Assert.Equal(3, cycles);
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void LdaAbsolute_ReadsAndTakesFourCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);
        ram[0x1234] = 0x99;

        var cycles = cpu.Step();

        Assert.Equal(0x99, cpu.State.A);
        Assert.Equal(4, cycles);
        Assert.Equal(0x0203, cpu.State.PC);
    }

    [Fact]
    public void LdaAbsolute_ProducesTheExactBusSequence()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xAD, 0x34, 0x12);
        ram[0x1234] = 0x99;

        cpu.Step();

        Assert.Equal(
        [
            new BusAccess(0x0200, 0xAD, false),
            new BusAccess(0x0201, 0x34, false),
            new BusAccess(0x0202, 0x12, false),
            new BusAccess(0x1234, 0x99, false),
        ], log);
    }

    [Fact]
    public void StaAbsolute_WritesAndTakesFourCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x8D, 0x00, 0x30);
        cpu.State.A = 0x5A;

        var cycles = cpu.Step();

        Assert.Equal(0x5A, ram[0x3000]);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void IncZeroPage_TakesFiveCyclesAndDummyWritesTheOriginalValue()
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, 0xE6, 0x10);
        ram[0x0010] = 0x41;

        var cycles = cpu.Step();

        Assert.Equal(0x42, ram[0x0010]);
        Assert.Equal(5, cycles);
        Assert.Equal(
        [
            new BusAccess(0x0200, 0xE6, false),
            new BusAccess(0x0201, 0x10, false),
            new BusAccess(0x0010, 0x41, false),
            new BusAccess(0x0010, 0x41, true),   // NMOS dummy write of the unmodified value
            new BusAccess(0x0010, 0x42, true),
        ], log);
    }

    [Fact]
    public void IncAbsolute_TakesSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xEE, 0x00, 0x40);
        ram[0x4000] = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x00, ram[0x4000]);
        Assert.True(cpu.State.Z);
        Assert.Equal(6, cycles);
    }

    [Fact]
    public void DecZeroPage_WrapsAndSetsNegative()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xC6, 0x10);
        ram[0x0010] = 0x00;

        cpu.Step();

        Assert.Equal(0xFF, ram[0x0010]);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }

    [Fact]
    public void Inx_WrapsFromFfToZero()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xE8);
        cpu.State.X = 0xFF;

        cpu.Step();

        Assert.Equal(0x00, cpu.State.X);
        Assert.True(cpu.State.Z);
    }

    [Fact]
    public void Dey_WrapsFromZeroToFf()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x88);
        cpu.State.Y = 0x00;

        cpu.Step();

        Assert.Equal(0xFF, cpu.State.Y);
        Assert.True(cpu.State.N);
    }
}
