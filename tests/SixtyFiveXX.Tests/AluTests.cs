using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class AluTests
{
    [Theory]
    [InlineData(0x0F, 0xF0, 0x00, true,  false)]
    [InlineData(0xFF, 0x80, 0x80, false, true)]
    [InlineData(0xCC, 0xAA, 0x88, false, true)]
    public void And_MasksAndSetsFlags(byte a, byte operand, byte expected, bool z, bool n)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x29, operand);   // AND #imm
        cpu.State.A = a;

        TestMachine.StepOne(cpu);

        Assert.Equal(expected, cpu.State.A);
        Assert.Equal(z, cpu.State.Z);
        Assert.Equal(n, cpu.State.N);
    }

    [Fact]
    public void Ora_SetsBits()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x09, 0x0F);
        cpu.State.A = 0xF0;

        TestMachine.StepOne(cpu);

        Assert.Equal(0xFF, cpu.State.A);
        Assert.True(cpu.State.N);
    }

    [Fact]
    public void Eor_TogglesBits()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x49, 0xFF);
        cpu.State.A = 0xAA;

        TestMachine.StepOne(cpu);

        Assert.Equal(0x55, cpu.State.A);
        Assert.False(cpu.State.N);
    }

    [Fact]
    public void Bit_TakesNAndVFromMemoryAndZFromTheMask()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x24, 0x10);   // BIT $10
        ram[0x0010] = 0xC0;                                       // bits 7 and 6 set
        cpu.State.A = 0x01;                                       // no overlap

        TestMachine.StepOne(cpu);

        Assert.True(cpu.State.N);
        Assert.True(cpu.State.V);
        Assert.True(cpu.State.Z);
        Assert.Equal(0x01, cpu.State.A);   // BIT never changes A
    }

    [Theory]
    [InlineData(0x50, 0x40, true,  false, false)]   // greater
    [InlineData(0x40, 0x40, true,  true,  false)]   // equal
    [InlineData(0x30, 0x40, false, false, true)]    // less
    public void Cmp_SetsCarryZeroAndNegative(byte a, byte operand, bool c, bool z, bool n)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xC9, operand);   // CMP #imm
        cpu.State.A = a;

        TestMachine.StepOne(cpu);

        Assert.Equal(c, cpu.State.C);
        Assert.Equal(z, cpu.State.Z);
        Assert.Equal(n, cpu.State.N);
        Assert.Equal(a, cpu.State.A);      // CMP never changes A
    }

    [Fact]
    public void CpxAndCpy_CompareTheirOwnRegisters()
    {
        var (cpuX, _) = TestMachine.Flat(0x0200, 0xE0, 0x10);   // CPX #$10
        cpuX.State.X = 0x10;
        TestMachine.StepOne(cpuX);
        Assert.True(cpuX.State.Z);

        var (cpuY, _) = TestMachine.Flat(0x0200, 0xC0, 0x10);   // CPY #$10
        cpuY.State.Y = 0x20;
        TestMachine.StepOne(cpuY);
        Assert.False(cpuY.State.Z);
        Assert.True(cpuY.State.C);
    }

    [Fact]
    public void AslAccumulator_ShiftsBitSevenIntoCarry()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x0A);
        cpu.State.A = 0x81;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x02, cpu.State.A);
        Assert.True(cpu.State.C);
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void LsrAccumulator_ShiftsBitZeroIntoCarryAndAlwaysClearsNegative()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x4A);
        cpu.State.A = 0x81;

        TestMachine.StepOne(cpu);

        Assert.Equal(0x40, cpu.State.A);
        Assert.True(cpu.State.C);
        Assert.False(cpu.State.N);
    }

    [Fact]
    public void RolAccumulator_RotatesCarryIntoBitZero()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x2A);
        cpu.State.A = 0x80;
        cpu.State.C = true;

        TestMachine.StepOne(cpu);

        Assert.Equal(0x01, cpu.State.A);
        Assert.True(cpu.State.C);
    }

    [Fact]
    public void RorAccumulator_RotatesCarryIntoBitSeven()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6A);
        cpu.State.A = 0x01;
        cpu.State.C = true;

        TestMachine.StepOne(cpu);

        Assert.Equal(0x80, cpu.State.A);
        Assert.True(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    [Fact]
    public void AslMemory_ShiftsInPlaceOverSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x0E, 0x00, 0x30);   // ASL $3000
        ram[0x3000] = 0xC0;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x80, ram[0x3000]);
        Assert.True(cpu.State.C);
        Assert.True(cpu.State.N);
        Assert.Equal(6, cycles);
    }

    [Theory]
    [InlineData(0x00, 0x00, false, 0x00, false, true,  false, false)]
    [InlineData(0x01, 0x01, false, 0x02, false, false, false, false)]
    [InlineData(0xFF, 0x01, false, 0x00, true,  true,  false, false)]
    [InlineData(0x7F, 0x01, false, 0x80, false, false, true,  true)]   // signed overflow
    [InlineData(0x80, 0xFF, false, 0x7F, true,  false, false, true)]   // signed overflow
    [InlineData(0x3F, 0x40, true,  0x80, false, false, true,  true)]   // carry in causes overflow
    public void AdcBinary_ComputesResultAndAllFourFlags(
        byte a, byte operand, bool carryIn, byte expected, bool c, bool z, bool n, bool v)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x69, operand);   // ADC #imm
        cpu.State.A = a;
        cpu.State.C = carryIn;

        TestMachine.StepOne(cpu);

        Assert.Equal(expected, cpu.State.A);
        Assert.Equal(c, cpu.State.C);
        Assert.Equal(z, cpu.State.Z);
        Assert.Equal(n, cpu.State.N);
        Assert.Equal(v, cpu.State.V);
    }

    [Theory]
    [InlineData(0x50, 0x10, true,  0x40, true,  false, false, false)]
    [InlineData(0x50, 0x50, true,  0x00, true,  true,  false, false)]
    [InlineData(0x50, 0x60, true,  0xF0, false, false, true,  false)]   // borrow clears carry
    [InlineData(0x80, 0x01, true,  0x7F, true,  false, false, true)]    // signed overflow
    [InlineData(0x00, 0x00, false, 0xFF, false, false, true,  false)]   // borrow in
    public void SbcBinary_ComputesResultAndAllFourFlags(
        byte a, byte operand, bool carryIn, byte expected, bool c, bool z, bool n, bool v)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xE9, operand);   // SBC #imm
        cpu.State.A = a;
        cpu.State.C = carryIn;

        TestMachine.StepOne(cpu);

        Assert.Equal(expected, cpu.State.A);
        Assert.Equal(c, cpu.State.C);
        Assert.Equal(z, cpu.State.Z);
        Assert.Equal(n, cpu.State.N);
        Assert.Equal(v, cpu.State.V);
    }
}
