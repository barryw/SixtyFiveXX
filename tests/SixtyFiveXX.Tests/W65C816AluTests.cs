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

    /// <summary>
    /// The discriminating test for the whole width mechanism: <c>CPX</c>'s width must come from
    /// <c>x</c>, not <c>m</c>. Set up so the two answers differ in PC and cycle count rather than
    /// in flags — an 8-bit CPX would compare $34 against X's low byte $34 and set Z just the same,
    /// but would consume one operand byte instead of two and leave PC at $C002.
    /// </summary>
    [Fact]
    public void Cpx_TakesItsWidthFromTheXFlagNotTheMFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE0;       // CPX #
        ram[0xC001] = 0x34;
        ram[0xC002] = 0x12;       // operand $1234 when x = 0

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator — the flag a wrong implementation would read
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.X = 0x1234;

        cpu.Step();

        Assert.Equal(0xC003, cpu.State.PC);
        Assert.True(cpu.State.Z);
        Assert.True(cpu.State.C);
    }

    /// <summary>
    /// A 16-bit compare's carry is the absence of a 17-bit borrow, and N comes from bit 15.
    /// Fails against a <c>Compare</c> that narrows the difference to 8 bits: $1000 - $2000 has a
    /// low byte of $00, so Z would be set and C would be computed from the wrong subtraction.
    /// </summary>
    [Fact]
    public void Cmp_SixteenBit_TakesCarryAndNFromTheFullDifference()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xC9;       // CMP #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // operand $2000

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x1000;

        cpu.Step();

        Assert.False(cpu.State.C);
        Assert.False(cpu.State.Z);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// With an 8-bit index, only the low byte participates. X's high byte is $00 by the
    /// continuously held invariant whenever x is set, so this pins the narrowing on the operand
    /// side: a 16-bit compare against a 1-byte operand would read the following instruction byte
    /// as the operand's high half and clear Z.
    /// </summary>
    [Fact]
    public void Cpx_EightBitIndex_ComparesOnlyTheLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE0;       // CPX #
        ram[0xC001] = 0x34;
        ram[0xC002] = 0x99;       // decoy: would become the high half of a 16-bit operand

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.X = 0x0034;

        cpu.Step();

        Assert.Equal(0xC002, cpu.State.PC);
        Assert.True(cpu.State.Z);
    }
}
