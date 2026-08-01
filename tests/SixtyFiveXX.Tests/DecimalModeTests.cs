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
        cpu.Step();
        return cpu;
    }

    private static Cpu<FlatBus> Sbc(byte a, byte operand, bool carryIn)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xE9, operand);
        cpu.State.A = a;
        cpu.State.C = carryIn;
        cpu.State.D = true;
        cpu.Step();
        return cpu;
    }

    private static Cpu<FlatBus> Arr(byte a, byte operand, bool carryIn)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x6B, operand);
        cpu.State.A = a;
        cpu.State.C = carryIn;
        cpu.State.D = true;
        cpu.Step();
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

    // ARR ($6B) is the single hardest-to-verify block in the codebase: its decimal-mode
    // flags do not correspond to any documented instruction (see Arr() in
    // Cpu.Exec.cs). It is green under 10,000 Harte vectors, so the values below are
    // pinned from running the vector-certified implementation, not derived
    // independently — this is a regression guard, not a new correctness claim.
    [Fact]
    public void ArrDecimal_MatchesTheVectorCertifiedBehaviour()
    {
        // No correction: A & operand is $00, nothing to adjust.
        var noCorrection = Arr(a: 0x00, operand: 0x00, carryIn: false);
        Assert.Equal(0x00, noCorrection.State.A);
        Assert.False(noCorrection.State.C);
        Assert.False(noCorrection.State.N);
        Assert.True(noCorrection.State.Z);
        Assert.False(noCorrection.State.V);

        // Low-nibble correction only: A & operand is $25, low nibble needs adjusting,
        // high nibble does not.
        var lowNibble = Arr(a: 0xFF, operand: 0x25, carryIn: false);
        Assert.Equal(0x18, lowNibble.State.A);
        Assert.False(lowNibble.State.C);
        Assert.False(lowNibble.State.N);
        Assert.False(lowNibble.State.Z);
        Assert.False(lowNibble.State.V);

        // Both nibbles correct and carry in: A & operand is $5F.
        var bothNibbles = Arr(a: 0xFF, operand: 0x5F, carryIn: true);
        Assert.Equal(0x05, bothNibbles.State.A);
        Assert.True(bothNibbles.State.C);
        Assert.True(bothNibbles.State.N);
        Assert.False(bothNibbles.State.Z);
        Assert.True(bothNibbles.State.V);
    }

    [Fact]
    public void DecimalFlag_DoesNotAffectNonArithmeticInstructions()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xC9, 0x05);   // CMP #$05
        cpu.State.A = 0x10;
        cpu.State.D = true;

        cpu.Step();

        Assert.True(cpu.State.C);      // compare is always binary
        Assert.False(cpu.State.Z);
    }
}
