using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Discrimination tests for the 65816's arithmetic and logic at both operand widths. The
/// SingleStepTests vectors cover these operations exhaustively; these exist so a width or
/// flag-source mistake fails legibly in a sub-second unit run rather than as one diff among
/// 900,000 per-cycle comparisons.
/// </summary>
public class W65C816AluTests
{
    /// <summary>
    /// A's high byte is the hidden B accumulator. An 8-bit operation must leave it alone —
    /// which <c>A8</c>'s setter now guarantees for every caller (task 2). Fails against a
    /// setter that assigns the whole 16-bit field: A would read $000F, not $120F.
    /// </summary>
    [Fact]
    public void And_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x29;       // AND #
        ram[0xC001] = 0x0F;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.A = 0x12FF;     // B = $12, A = $FF

        cpu.Step();

        Assert.Equal(0x120F, cpu.State.A);
    }

    /// <summary>
    /// With a 16-bit accumulator, N comes from bit 15, not bit 7. Fails against an arm that
    /// calls <c>SetZN</c> instead of <c>SetZN16</c>: $8000's low byte is $00, so N would be
    /// clear and Z would be set.
    /// </summary>
    [Fact]
    public void Ora_SixteenBitMode_TakesNAndZFromTheFullSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x09;       // ORA #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x80;       // operand $8000

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0000;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }
}
