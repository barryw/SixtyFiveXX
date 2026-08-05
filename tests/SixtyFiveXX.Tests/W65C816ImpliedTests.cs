using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's implied-mode opcodes — the accumulator forms, the transfers, XBA and the index
/// increments. None of them fetches an operand, so none declares a <c>Width</c>; each tests the
/// flag its own result width depends on.
/// <para>
/// Every test that means to discriminate <c>m</c> from <c>x</c> sets BOTH to opposed values.
/// <c>Cpu</c>'s constructor does not call <c>Reset()</c>, so <c>P == $00</c> and both flags read
/// clear by default — a test that sets only one leaves the two indistinguishable, which is a hole
/// phase 7c found the hard way.
/// </para>
/// </summary>
public class W65C816ImpliedTests
{
    /// <summary>
    /// A 16-bit ASL on the accumulator shifts bit 14 into bit 15 and takes carry from bit 15.
    /// Fails against an 8-bit shift: A's high byte would be untouched and C would come from bit 7.
    /// </summary>
    [Fact]
    public void AslAccumulator_SixteenBit_ShiftsTheFullAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0A;       // ASL A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.XFlag = true;   // opposed, so a width read from x would be visible
        cpu.State.A = 0x4001;

        cpu.Step();

        Assert.Equal(0x8002, cpu.State.A);
        Assert.False(cpu.State.C);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// An 8-bit accumulator operation must not disturb A's high byte — the hidden B accumulator.
    /// </summary>
    [Fact]
    public void AslAccumulator_EightBitMode_PreservesTheHiddenBAccumulator()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0A;       // ASL A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.XFlag = false;  // opposed
        cpu.State.A = 0x1221;

        cpu.Step();

        Assert.Equal(0x1242, cpu.State.A);
    }

    /// <summary>
    /// A 16-bit INC A wraps at sixteen bits, not eight.
    /// </summary>
    [Fact]
    public void IncAccumulator_SixteenBit_WrapsAtSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x1A;       // INC A

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = true;   // opposed
        cpu.State.A = 0xFFFF;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.A);
        Assert.True(cpu.State.Z);
    }

    /// <summary>
    /// TAX is sized by x, not m. Flags are opposed so a width read from the wrong one is visible:
    /// with m=1 and x=0 an m-sized TAX would move only the low byte.
    /// </summary>
    [Fact]
    public void Tax_IsSizedByTheXFlagNotTheMFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAA;       // TAX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator
        cpu.State.XFlag = false;  // 16-bit index — this is what must govern
        cpu.State.A = 0x1234;
        cpu.State.X = 0x0000;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.X);
    }

    /// <summary>
    /// With an 8-bit index, TAX moves only A's low byte and X's high byte is cleared — XH is $00
    /// whenever x is set.
    /// </summary>
    [Fact]
    public void Tax_EightBitIndex_MovesOnlyTheLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAA;       // TAX

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // opposed
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x0034, cpu.State.X);
    }

    /// <summary>
    /// TCD moves all sixteen bits regardless of m and x. Both flags are set to the widths that
    /// would truncate if either governed.
    /// </summary>
    [Fact]
    public void Tcd_IsAlwaysSixteenBit()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x5B;       // TCD

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator — must NOT narrow the transfer
        cpu.State.XFlag = true;   // 8-bit index — likewise
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.DP);
    }

    /// <summary>
    /// TXS sets no flags, on the 65816 as on the 8-bit cores. N and Z are pre-set to values a
    /// flag-setting implementation would overwrite.
    /// </summary>
    [Fact]
    public void Txs_SetsNoFlags()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x9A;       // TXS

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;
        cpu.State.X = 0x0000;     // would set Z if TXS set flags
        cpu.State.Z = false;
        cpu.State.N = true;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.S);
        Assert.False(cpu.State.Z);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// <c>TXS</c> moves all sixteen bits even when <c>x</c> selects an 8-bit index, because the
    /// destination <c>S</c> has no narrow form — research document §13.4, which refutes the
    /// natural-looking assumption that <c>TXS</c> is sized by <c>x</c> like its <c>TSX</c>
    /// counterpart. Twenty thousand <c>$9A</c> vectors agree, but nothing pinned it below the
    /// conformance suite until this test: <see cref="Txs_SetsNoFlags"/> above uses a 16-bit index
    /// and so cannot tell the two rules apart. An x-sized <c>TXS</c> leaves <c>S</c> at
    /// <c>$0034</c>.
    /// </summary>
    [Fact]
    public void Txs_IsAlwaysSixteenBit_EvenWithAnEightBitIndex()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x9A;       // TXS

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // opposed, so neither width flag can be read by accident
        cpu.State.XFlag = true;   // 8-bit index — must NOT narrow the transfer
        cpu.State.X = 0x1234;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.S);
    }

    /// <summary>
    /// XBA swaps A's two halves and sets N and Z from the new low byte as an 8-bit result, even
    /// with a 16-bit accumulator (research document §13.5).
    /// </summary>
    [Fact]
    public void Xba_SwapsTheHalvesAndSetsFlagsFromTheNewLowByte()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xEB;       // XBA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.A = 0x0080;

        cpu.Step();

        Assert.Equal(0x8000, cpu.State.A);
        Assert.True(cpu.State.Z);    // new low byte is $00
        Assert.False(cpu.State.N);
    }

    /// <summary>
    /// XBA is 3 cycles, not the 2 every other implied opcode on this part takes — research
    /// document §13.5, datasheet Table 5-7 row 19b, whose cycles 2 and 3 are both IO at
    /// PBR,PC+1. This is the unit-level guard on the sequence; vector "eb e 1" is the
    /// silicon-level one.
    /// </summary>
    [Fact]
    public void Xba_TakesThreeCycles()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xEB;       // XBA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;

        var before = cpu.Cycles;
        cpu.Step();

        Assert.Equal(3L, cpu.Cycles - before);
    }
}
