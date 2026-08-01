using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class UndocumentedNopTests
{
    [Theory]
    [InlineData(0x1A)] [InlineData(0x3A)] [InlineData(0x5A)]
    [InlineData(0x7A)] [InlineData(0xDA)] [InlineData(0xFA)]
    public void ImpliedNops_TakeTwoCyclesAndAdvancePcByOne(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode);

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x0201, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x80)] [InlineData(0x82)] [InlineData(0x89)]
    [InlineData(0xC2)] [InlineData(0xE2)]
    public void ImmediateNops_TakeTwoCyclesAndConsumeTheOperand(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x55);

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x04)] [InlineData(0x44)] [InlineData(0x64)]
    public void ZeroPageNops_TakeThreeCyclesAndReadTheAddress(byte opcode)
    {
        var (cpu, ram, log) = TestMachine.Logged(0x0200, opcode, 0x42);
        ram[0x0042] = 0x7F;

        var cycles = cpu.Step();

        Assert.Equal(3, cycles);
        Assert.Equal(0x0202, cpu.State.PC);
        Assert.Contains(log, a => a.Address == 0x0042 && !a.IsWrite);
    }

    [Theory]
    [InlineData(0x14)] [InlineData(0x34)] [InlineData(0x54)]
    [InlineData(0x74)] [InlineData(0xD4)] [InlineData(0xF4)]
    public void ZeroPageXNops_TakeFourCycles(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x42);
        cpu.State.X = 0x10;

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void AbsoluteNop_TakesFourCycles()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x0C, 0x00, 0x30);

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x0203, cpu.State.PC);
    }

    [Theory]
    [InlineData(0x1C)] [InlineData(0x3C)] [InlineData(0x5C)]
    [InlineData(0x7C)] [InlineData(0xDC)] [InlineData(0xFC)]
    public void AbsoluteXNops_TakeFourCyclesWithoutAPageCross(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x00, 0x30);
        cpu.State.X = 0x10;

        Assert.Equal(4, cpu.Step());
    }

    [Theory]
    [InlineData(0x1C)] [InlineData(0x3C)] [InlineData(0x5C)]
    [InlineData(0x7C)] [InlineData(0xDC)] [InlineData(0xFC)]
    public void AbsoluteXNops_TakeFiveCyclesAcrossAPage(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0xF0, 0x30);
        cpu.State.X = 0x20;

        Assert.Equal(5, cpu.Step());
    }

    [Fact]
    public void UndocumentedNops_DoNotDisturbRegistersOrFlags()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x04, 0x42);
        cpu.State.A = 0x11;
        cpu.State.X = 0x22;
        cpu.State.Y = 0x33;
        cpu.State.P = Flag.U | Flag.C | Flag.N;

        cpu.Step();

        Assert.Equal(0x11, cpu.State.A);
        Assert.Equal(0x22, cpu.State.X);
        Assert.Equal(0x33, cpu.State.Y);
        Assert.Equal(Flag.U | Flag.C | Flag.N, cpu.State.P);
    }
}
