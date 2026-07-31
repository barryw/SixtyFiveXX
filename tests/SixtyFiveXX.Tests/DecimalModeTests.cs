using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class DecimalModeTests
{
    private static Cpu<FlatBus> Adc(byte a, byte operand, bool carryIn)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x69, operand);
        cpu.State.A = a;
        cpu.State.C = carryIn;
        cpu.State.D = true;
        TestMachine.StepOne(cpu);
        return cpu;
    }

    private static Cpu<FlatBus> Sbc(byte a, byte operand, bool carryIn)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xE9, operand);
        cpu.State.A = a;
        cpu.State.C = carryIn;
        cpu.State.D = true;
        TestMachine.StepOne(cpu);
        return cpu;
    }

    [Theory]
    [InlineData(0x00, 0x00, false, 0x00, false)]
    [InlineData(0x09, 0x01, false, 0x10, false)]   // 9 + 1 = 10
    [InlineData(0x50, 0x50, false, 0x00, true)]    // 50 + 50 = 100, carry out
    [InlineData(0x99, 0x01, false, 0x00, true)]    // 99 + 1 = 100
    [InlineData(0x12, 0x34, false, 0x46, false)]
    [InlineData(0x58, 0x46, true,  0x05, true)]    // 58 + 46 + 1 = 105
    public void AdcDecimal_ProducesBcdResultAndCarry(
        byte a, byte operand, bool carryIn, byte expectedA, bool expectedC)
    {
        var cpu = Adc(a, operand, carryIn);

        Assert.Equal(expectedA, cpu.State.A);
        Assert.Equal(expectedC, cpu.State.C);
    }

    [Fact]
    public void AdcDecimal_TakesZeroFromTheBinaryResultNotTheBcdResult()
    {
        // 0x99 + 0x01 in BCD is 0x00 with carry, but the binary sum 0x9A is non-zero,
        // and NMOS parts take Z from the binary result. This is the classic divergence.
        var cpu = Adc(0x99, 0x01, carryIn: false);

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cpu.State.C);
        Assert.False(cpu.State.Z);
    }

    [Fact]
    public void AdcDecimal_LeavesZeroSetWhenTheBinarySumIsAlsoZero()
    {
        var cpu = Adc(0x00, 0x00, carryIn: false);

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cpu.State.Z);
    }

    [Theory]
    [InlineData(0x00, 0x00, true,  0x00, true)]
    [InlineData(0x50, 0x25, true,  0x25, true)]    // 50 - 25 = 25
    [InlineData(0x00, 0x01, true,  0x99, false)]   // 0 - 1 borrows
    [InlineData(0x10, 0x05, true,  0x05, true)]
    [InlineData(0x99, 0x99, true,  0x00, true)]
    public void SbcDecimal_ProducesBcdResultAndBorrow(
        byte a, byte operand, bool carryIn, byte expectedA, bool expectedC)
    {
        var cpu = Sbc(a, operand, carryIn);

        Assert.Equal(expectedA, cpu.State.A);
        Assert.Equal(expectedC, cpu.State.C);
    }

    [Fact]
    public void SbcDecimal_TakesEveryFlagFromTheBinaryResult()
    {
        // 0x00 - 0x01 in BCD gives A = 0x99, but N, Z, V and C all come from the
        // binary result 0xFF: negative, non-zero, no overflow, borrow.
        var cpu = Sbc(0x00, 0x01, carryIn: true);

        Assert.Equal(0x99, cpu.State.A);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
        Assert.False(cpu.State.V);
        Assert.False(cpu.State.C);
    }

    [Fact]
    public void DecimalFlag_DoesNotAffectNonArithmeticInstructions()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xC9, 0x05);   // CMP #$05
        cpu.State.A = 0x10;
        cpu.State.D = true;

        TestMachine.StepOne(cpu);

        Assert.True(cpu.State.C);      // compare is always binary
        Assert.False(cpu.State.Z);
    }
}
