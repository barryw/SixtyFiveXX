using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class ImmediateOddballTests
{
    [Theory]
    [InlineData(0x0B)]
    [InlineData(0x2B)]
    public void Anc_AndsThenCopiesBitSevenIntoCarry(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0xFF);
        cpu.State.A = 0x80;

        var cycles = cpu.Step();

        Assert.Equal(0x80, cpu.State.A);
        Assert.True(cpu.State.N);
        Assert.True(cpu.State.C);      // carry mirrors bit 7
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Anc_ClearsCarryWhenBitSevenIsClear()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x0B, 0x7F);
        cpu.State.A = 0xFF;
        cpu.State.C = true;

        cpu.Step();

        Assert.Equal(0x7F, cpu.State.A);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.C);
    }

    [Fact]
    public void Alr_AndsThenShiftsRight()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x4B, 0xFF);   // ALR #$FF
        cpu.State.A = 0x03;

        var cycles = cpu.Step();

        Assert.Equal(0x01, cpu.State.A);   // ($03 & $FF) >> 1
        Assert.True(cpu.State.C);          // bit 0 before the shift
        Assert.False(cpu.State.N);         // LSR always clears N
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Arr_TakesCarryFromBitSixAndOverflowFromBitsSixAndFive()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6B, 0xFF);   // ARR #$FF
        cpu.State.A = 0xFF;
        cpu.State.C = false;

        var cycles = cpu.Step();

        Assert.Equal(0x7F, cpu.State.A);   // ($FF & $FF) >> 1, carry-in 0 into bit 7
        Assert.True(cpu.State.C);          // bit 6 of the result is set
        Assert.False(cpu.State.V);         // bit 6 XOR bit 5 = 1 XOR 1 = 0
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Arr_SetsOverflowWhenBitsSixAndFiveDiffer()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6B, 0xFF);
        cpu.State.A = 0x40;                // result $20: bit 6 clear, bit 5 set
        cpu.State.C = false;

        cpu.Step();

        Assert.Equal(0x20, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.V);
    }

    [Fact]
    public void Sbx_SubtractsImmediateFromAAndXWritingToX()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x05);   // SBX #$05
        cpu.State.A = 0xFF;
        cpu.State.X = 0x0F;

        var cycles = cpu.Step();

        Assert.Equal(0x0A, cpu.State.X);   // ($FF & $0F) - $05
        Assert.Equal(0xFF, cpu.State.A);   // A is untouched
        Assert.True(cpu.State.C);          // no borrow
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Sbx_ClearsCarryOnBorrow()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x10);
        cpu.State.A = 0xFF;
        cpu.State.X = 0x05;

        cpu.Step();

        Assert.Equal(0xF5, cpu.State.X);   // $05 - $10 wraps
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    [Fact]
    public void Sbx_IgnoresDecimalMode()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xCB, 0x01);
        cpu.State.A = 0xFF;
        cpu.State.X = 0x10;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x0F, cpu.State.X);   // binary, not BCD
    }

    [Fact]
    public void SbcEb_BehavesIdenticallyToSbcE9()
    {
        var (a, _) = TestMachine.Flat(0x0200, 0xE9, 0x10);
        a.State.A = 0x50; a.State.C = true;
        a.Step();

        var (b, _) = TestMachine.Flat(0x0200, 0xEB, 0x10);
        b.State.A = 0x50; b.State.C = true;
        var cycles = b.Step();

        Assert.Equal(a.State.A, b.State.A);
        Assert.Equal(a.State.P, b.State.P);
        Assert.Equal(2, cycles);
    }
}
