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

    /// <summary>
    /// 16-bit binary add: V comes from bit 15, C from bit 16. Fails against an 8-bit
    /// <c>Adc</c> reached with a 16-bit operand.
    /// </summary>
    [Fact]
    public void Adc_SixteenBitBinary_TakesOverflowFromBitFifteen()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x69;       // ADC #
        ram[0xC001] = 0x01;
        ram[0xC002] = 0x00;       // operand $0001

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.D = false;
        cpu.State.C = false;
        cpu.State.A = 0x7FFF;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.V);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.C);
    }

    /// <summary>
    /// 8-bit add on the 65816 must not disturb the hidden B accumulator, which is a real risk
    /// here because <c>Adc</c> writes its result through <c>A8</c> internally rather than at the
    /// call site. Fails against an <c>A8</c> setter that assigns the whole 16-bit field.
    /// </summary>
    [Fact]
    public void Adc_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x69;       // ADC #
        ram[0xC001] = 0x01;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.D = false;
        cpu.State.C = false;
        cpu.State.A = 0x1210;     // B = $12

        cpu.Step();

        Assert.Equal(0x1211, cpu.State.A);
    }

    /// <summary>
    /// 16-bit subtract: C is the absence of a borrow out of bit 16.
    /// </summary>
    [Fact]
    public void Sbc_SixteenBitBinary_ClearsCarryOnABorrow()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE9;       // SBC #
        ram[0xC001] = 0x01;
        ram[0xC002] = 0x00;       // operand $0001

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.D = false;
        cpu.State.C = true;       // no incoming borrow
        cpu.State.A = 0x0000;

        cpu.Step();

        Assert.Equal(0xFFFF, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// Clark's Example 2, verbatim and in full — the only 16-bit decimal result any surveyed
    /// source states, and so the one citable check on the whole 16-bit decimal algorithm
    /// (research document §12.1). It also pins <c>SBC</c>'s decimal <c>N</c> at 16 bits to the
    /// <em>corrected</em> result rather than the binary intermediate: Clark's Example 1 is the
    /// same operands with <c>d = 0</c> and gives $DFFE, whose bit 15 is 1, while this one gives
    /// $7998 and <c>n = 0</c>. Fails against an implementation that leaves N binary — which is
    /// what the task brief's hypothesis did, and what vector <c>e9 n 49</c> independently
    /// rejected.
    /// </summary>
    [Fact]
    public void Sbc_SixteenBitDecimal_MatchesClarksWorkedExample()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE9;       // SBC #
        ram[0xC001] = 0x03;
        ram[0xC002] = 0x20;       // operand $2003

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // m = 0
        cpu.State.D = true;       // d = 1
        cpu.State.C = true;       // c = 1
        cpu.State.A = 0x0001;

        cpu.Step();

        Assert.Equal(0x7998, cpu.State.A);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.Z);
        Assert.False(cpu.State.C);
    }

    /// <summary>
    /// The 65816's 8-bit decimal <c>SBC</c> corrects nibble-wise, the way NMOS does — <em>not</em>
    /// by the $60/$06 adjustment of the binary difference that <c>SbcCmos</c> uses. Measured, and
    /// contrary to Clark's §6 preamble, which groups 8-bit results with the 65C02's: research
    /// document §12.1's "Measured" block, established by vector <c>e9 e 15</c> and 29 others.
    /// <para>
    /// $B0 - $4D - 1 in decimal mode: the two algorithms agree on every flag and disagree only on
    /// the accumulator. Nibble-wise borrows $06 out of the low digit and one out of the high,
    /// giving $6C; the CMOS $60/$06 form leaves the binary $62 alone (it did not go negative) and
    /// subtracts $06 for the low-nibble borrow, giving $5C. Delegating this branch to
    /// <c>SbcCmos</c> — the obvious refactor, and what the task brief proposed — fails here.
    /// The $12 high byte doubles as a hidden-B accumulator check.
    /// </para>
    /// </summary>
    [Fact]
    public void Sbc_EightBitDecimal_CorrectsNibbleWiseNotLikeTheSixtyFiveCTwo()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xE9;       // SBC #
        ram[0xC001] = 0x4D;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.D = true;
        cpu.State.C = false;      // an incoming borrow
        cpu.State.A = 0x12B0;

        cpu.Step();

        Assert.Equal(0x126C, cpu.State.A);
        Assert.True(cpu.State.C);
        Assert.False(cpu.State.N);
    }

    /// <summary>
    /// Every addressing mode of BIT but immediate copies the operand's top two bits into N and
    /// V. At sixteen bits those are bits 15 and 14, not 7 and 6. Fails against an arm that reads
    /// $80/$40 out of a 16-bit operand.
    /// </summary>
    [Fact]
    public void Bit_SixteenBit_TakesNFromBitFifteenAndVFromBitFourteen()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x2C;       // BIT abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // AA = $2000
        ram[0x002000] = 0x00;     // operand low
        ram[0x002001] = 0x40;     // operand high -> $4000: bit 15 clear, bit 14 set

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0xFFFF;

        cpu.Step();

        Assert.False(cpu.State.N);
        Assert.True(cpu.State.V);
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// The immediate form sets Z alone, at either width — the behaviour this codebase already
    /// models as <c>Op.BitImm</c> for the 65C02. N and V are pre-set to values the operand would
    /// overwrite if the wrong arm ran, so an <c>Op.Bit</c> mis-wiring fails here.
    /// </summary>
    [Fact]
    public void BitImmediate_SixteenBit_SetsOnlyZ()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x89;       // BIT #
        ram[0xC001] = 0x00;
        ram[0xC002] = 0xC0;       // operand $C000 — bits 15 AND 14 set, so Op.Bit would flip
                                  // BOTH N and V. $8000 would leave V clear either way, making
                                  // that assertion non-discriminating; this is the operand that
                                  // makes all three assertions do work.

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0000;
        cpu.State.N = false;
        cpu.State.V = false;

        cpu.Step();

        Assert.True(cpu.State.Z);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.V);
    }
}
