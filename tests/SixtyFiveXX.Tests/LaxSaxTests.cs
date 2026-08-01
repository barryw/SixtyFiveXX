using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class LaxSaxTests
{
    [Fact]
    public void Lax_LoadsBothAccumulatorAndXFromMemory()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA7, 0x10);   // LAX $10
        ram[0x0010] = 0x9C;

        var cycles = cpu.Step();

        Assert.Equal(0x9C, cpu.State.A);
        Assert.Equal(0x9C, cpu.State.X);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Lax_SetsZeroWhenLoadingZero()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA7, 0x10);
        ram[0x0010] = 0x00;
        cpu.State.A = 0xFF;

        cpu.Step();

        Assert.Equal(0x00, cpu.State.A);
        Assert.Equal(0x00, cpu.State.X);
        Assert.True(cpu.State.Z);
    }

    [Fact]
    public void LaxZeroPageY_IndexesByY()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xB7, 0x80);   // LAX $80,Y
        cpu.State.Y = 0x04;
        ram[0x0084] = 0x2B;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x2B, cpu.State.A);
        Assert.Equal(0x2B, cpu.State.X);
    }

    [Fact]
    public void LaxAbsoluteY_PaysThePageCrossPenalty()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBF, 0xFF, 0x20);   // LAX $20FF,Y
        cpu.State.Y = 0x01;
        ram[0x2100] = 0x3C;

        Assert.Equal(5, cpu.Step());
        Assert.Equal(0x3C, cpu.State.A);
    }

    [Fact]
    public void Sax_StoresTheAndOfAccumulatorAndX()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x87, 0x10);   // SAX $10
        cpu.State.A = 0xF0;
        cpu.State.X = 0x3C;

        var cycles = cpu.Step();

        Assert.Equal(0x30, ram[0x0010]);   // $F0 & $3C
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Sax_DoesNotTouchAnyFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x87, 0x10);
        cpu.State.A = 0x00;
        cpu.State.X = 0x00;                 // result is zero
        cpu.State.P = Flag.U;

        cpu.Step();

        Assert.Equal(Flag.U, cpu.State.P);   // Z must NOT be set
    }

    [Fact]
    public void SaxZeroPageY_IndexesByY()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x97, 0x80);   // SAX $80,Y
        cpu.State.Y = 0x04;
        cpu.State.A = 0xFF;
        cpu.State.X = 0x0F;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0F, ram[0x0084]);
    }
}
