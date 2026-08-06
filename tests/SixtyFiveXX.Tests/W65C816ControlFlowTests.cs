using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The ten 65816 branches — the eight conditional ones, <c>BRA</c> and <c>BRL</c>. Every
/// assertion below is written against research document §14.5, which transcribes WDC datasheet
/// Table 5-7 rows 20 and 21 and Bruce Clark's §4, §5.1.2, §6.2.1.1 and §6.2.1.2.
/// <para>
/// Two properties here are <b>not</b> shared with the five eight-bit cores and are the reason
/// this file exists rather than an extra <c>[Theory]</c> row in <c>BranchTests</c>:
/// </para>
/// <list type="number">
/// <item>The taken-branch page-cross cycle is emulation-mode-only (§14.5 answer 1 — datasheet
/// Note 6, "in 6502 emulation mode (E=1)", and Clark's <c>2+t+t*e*p</c>, whose page-cross term
/// is multiplied by <c>e</c>). In native mode a taken branch is a flat three cycles wherever it
/// lands.</item>
/// <item>The displacement add wraps inside the program bank and never carries into <c>PBR</c>
/// (§14.5 answer 3 — Clark §5.1.2 and §4's two worked examples, one per width). The vectors
/// cover this well — 5,078 of the 200,000 have a destination outside <c>$0000</c>-<c>$FFFF</c>
/// before the wrap — so what these tests add is not coverage but locality: they pin the rule at
/// Clark's own worked addresses, where a failure names the boundary instead of naming one
/// vector out of ten thousand.</item>
/// </list>
/// </summary>
public class W65C816ControlFlowTests
{
    private const byte Bpl = 0x10;
    private const byte Bmi = 0x30;
    private const byte Bvc = 0x50;
    private const byte Bvs = 0x70;
    private const byte Bcc = 0x90;
    private const byte Bcs = 0xB0;
    private const byte Bne = 0xD0;
    private const byte Beq = 0xF0;
    private const byte Bra = 0x80;
    private const byte Brl = 0x82;

    /// <summary>
    /// Lays an instruction at <paramref name="pbr"/>:<paramref name="pc"/> in a real 24-bit
    /// space — a 16-bit-masking bus cannot tell a bank-wrapping formula from a bank-carrying
    /// one, which is the whole point of the wrap tests below.
    /// </summary>
    private static Cpu<RefBus, W65C816Variant> Machine(
        BankedBus ram, byte pbr, ushort pc, bool emulation, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            ram[(pbr << 16) | ((pc + i) & 0xFFFF)] = bytes[i];

        var cpu = Banked816TestMachine.Make(ram, pc);
        cpu.State.PBR = pbr;
        cpu.State.E = emulation;
        return cpu;
    }

    // ---------------------------------------------------------------- cycle counts

    /// <summary>
    /// Not taken: two cycles and fall through, in both modes. Row 20's cycles 1 and 2, with
    /// Note 5's "add 1 cycle if branch is taken" not applying.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UntakenBranch_CostsTwoCyclesAndFallsThrough(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation, Bne, 0x10);
        cpu.State.Z = true;                     // equal, so BNE is not taken

        Assert.Equal(2, cpu.Step());
        Assert.Equal(0x2002, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
    }

    /// <summary>Taken inside a page: three cycles in both modes. Row 20's cycle 2a, Note 5.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TakenBranchWithinAPage_CostsThreeCycles(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation, Bne, 0x10);

        Assert.Equal(3, cpu.Step());
        Assert.Equal(0x2012, cpu.State.PC);      // $2002 + $10
    }

    /// <summary>
    /// <b>The behaviour that separates this core from the other five.</b> A taken branch that
    /// crosses a page costs a fourth cycle in emulation mode and does not in native mode —
    /// §14.5 answer 1, datasheet Note 6 and Clark's <c>2+t+t*e*p</c>. The destination is
    /// identical in both modes; only the cycle count differs.
    /// </summary>
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public void TakenBranchAcrossAPage_CostsTheExtraCycleOnlyInEmulationMode(
        bool emulation, int expected)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x20F0, emulation, Bne, 0x20);

        Assert.Equal(expected, cpu.Step());
        Assert.Equal(0x2112, cpu.State.PC);      // $20F2 + $20, page $20 -> page $21
    }

    /// <summary>Backwards across a page is the same rule with a negative displacement.</summary>
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public void TakenBackwardBranchAcrossAPage_CostsTheExtraCycleOnlyInEmulationMode(
        bool emulation, int expected)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2105, emulation, Bne, 0x80);   // -128

        Assert.Equal(expected, cpu.Step());
        Assert.Equal(0x2087, cpu.State.PC);      // $2107 - $80
    }

    /// <summary>
    /// What "page cross" is measured against, which the datasheet does not say and Clark
    /// §6.2.1.1 does: the destination page against the page of the <em>next</em> instruction,
    /// not the page of the opcode. His own example — <c>LABEL BRA LABEL+2</c> "always takes 3
    /// cycles, no matter where the BRA instruction is located in memory". Here the opcode sits
    /// in page $20 and the destination in page $21, and there is still no penalty in either
    /// mode, because the base and the destination are the same address.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BranchToTheNextInstruction_NeverPaysThePageCrossCycle(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x20FE, emulation, Bra, 0x00);

        Assert.Equal(3, cpu.Step());
        Assert.Equal(0x2100, cpu.State.PC);
    }

    /// <summary>
    /// The page is measured against the byte after the branch (<c>$2100</c>, low byte
    /// <c>$00</c>), which this does not cross, even though the opcode itself sits at
    /// <c>$20FE</c> (low byte <c>$FE</c>). A page-cross test that measured against the
    /// opcode's own address instead would call this a cross and add the emulation-mode
    /// page-cross cycle — the mutant this case exists to catch.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BranchNearTheTopOfThePage_StillNeverPaysThePageCrossCycle(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x20FE, emulation, Bra, 0x02);

        Assert.Equal(3, cpu.Step());
        Assert.Equal(0x2102, cpu.State.PC);      // $2100 + 2, no cross
    }

    // ---------------------------------------------------------------- the bus cycles

    /// <summary>
    /// Row 20's address column: both internal cycles drive <c>PBR,PC+1</c> — the offset byte's
    /// own address — and neither drives the destination or the un-fixed program counter. The
    /// eight-bit cores do something different on both counts (their cycle 3 drives the byte
    /// <em>after</em> the branch and their cycle 4 the un-fixed PC), which is why these are the
    /// 65816's own micro-ops rather than the shared ones.
    /// </summary>
    [Fact]
    public void BothInternalCycles_DriveTheOffsetBytesAddressWithNoPinsAsserted()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x20F0, emulation: true, Bne, 0x20);

        cpu.Tick();
        Assert.Equal(BusPins.Vda | BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x7E20F0, cpu.LastAddress);

        cpu.Tick();
        Assert.Equal(BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x7E20F1, cpu.LastAddress);

        cpu.Tick();
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x7E20F1, cpu.LastAddress);

        cpu.Tick();
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x7E20F1, cpu.LastAddress);
    }

    /// <summary>
    /// <c>BRL</c>'s row 21: its one internal cycle is the fourth, driving <c>PBR,PC+2</c> —
    /// the high displacement byte's address, the same "last operand byte" rule row 20 follows.
    /// </summary>
    [Fact]
    public void BrlsInternalCycle_DrivesTheHighDisplacementBytesAddress()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, Brl, 0x00, 0x10);

        cpu.Tick();
        cpu.Tick();
        cpu.Tick();
        Assert.Equal(BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x7E2002, cpu.LastAddress);

        cpu.Tick();
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x7E2002, cpu.LastAddress);
    }

    // ---------------------------------------------------------------- the bank boundary

    /// <summary>
    /// §14.5 answer 3: the displacement add wraps inside the program bank. Forwards off the
    /// top of bank $13 lands at the bottom of bank $13, never bank $14.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardBranchOffTheTopOfTheBank_WrapsAndLeavesPbrUnchanged(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x13, 0xFFFE, emulation, Bra, 0x04);

        cpu.Step();

        Assert.Equal(0x0004, cpu.State.PC);      // $0000 + 4, PC having wrapped past $FFFF
        Assert.Equal(0x13, cpu.State.PBR);
    }

    /// <summary>Backwards off the bottom of the bank, the same rule mirrored.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackwardBranchOffTheBottomOfTheBank_WrapsAndLeavesPbrUnchanged(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x13, 0x0000, emulation, Bra, 0xFC);   // -4

        cpu.Step();

        Assert.Equal(0xFFFE, cpu.State.PC);      // $0002 - 4
        Assert.Equal(0x13, cpu.State.PBR);
    }

    /// <summary>
    /// Clark §4's <c>rel16</c> worked example, verbatim: "a BRL $2000 at $13E000 will branch to
    /// $132000 rather than $142000". The displacement is <c>$2000 - ($E000 + 3) = $3FFD</c>,
    /// which carries out of sixteen bits and must be discarded rather than added to <c>PBR</c>.
    /// </summary>
    [Fact]
    public void Brl_WrapsAtTheBankBoundary()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x13, 0xE000, emulation: false, Brl, 0xFD, 0x3F);

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x2000, cpu.State.PC);
        Assert.Equal(0x13, cpu.State.PBR);
    }

    // ---------------------------------------------------------------- BRA and BRL

    /// <summary>
    /// <c>BRA</c> branches whatever the flags say, and costs the taken branch's three cycles
    /// either way. Both extremes of <c>P</c> are exercised because a condition wired to the
    /// wrong flag would still pass with only one of them.
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void Bra_IsUnconditional(int p)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, Bra, 0x10);
        cpu.State.P = (byte)p;

        Assert.Equal(3, cpu.Step());
        Assert.Equal(0x2012, cpu.State.PC);
    }

    /// <summary>
    /// §14.5 answer 2: <c>BRL</c> is three bytes and a flat four cycles in both modes — no
    /// not-taken case and no page-cross penalty, so no conditional cycle of any kind. Row 21's
    /// header prints one figure rather than a list, and no note marker appears on the row.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Brl_IsAlwaysFourCycles_EvenAcrossAPage(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x20F0, emulation, Brl, 0x20, 0x00);

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x2113, cpu.State.PC);      // $20F3 + $0020, page $20 -> page $21
    }

    /// <summary>
    /// The sixteen-bit displacement is signed and reaches backwards as far as it reaches
    /// forwards — §5.18's <c>K : PC+3+$HHLL</c>, measured from the byte after the instruction.
    /// </summary>
    [Fact]
    public void Brl_ReachesBackwardsWithASignedSixteenBitDisplacement()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0xA000, emulation: false, Brl, 0x00, 0x80);   // -$8000

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x2003, cpu.State.PC);      // $A003 - $8000
    }

    /// <summary>
    /// <c>BRL</c>'s full forward reach, the other end of the same signed range.
    /// </summary>
    [Fact]
    public void Brl_ReachesForwardsWithASignedSixteenBitDisplacement()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, Brl, 0xFF, 0x7F);   // +$7FFF

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0xA002, cpu.State.PC);      // $2003 + $7FFF
    }

    // ---------------------------------------------------------------- the eight conditions

    /// <summary>
    /// Each conditional opcode against the flag it tests, both ways. <c>Cpu.IsBranchTaken</c>
    /// is shared with the eight-bit cores and already certified there; what this covers is the
    /// 65816 table's own opcode-to-operation mapping, where a transposed pair would otherwise
    /// only surface four minutes into a conformance run.
    /// </summary>
    [Theory]
    [InlineData(Bpl, 0x80, false)]
    [InlineData(Bpl, 0x00, true)]
    [InlineData(Bmi, 0x80, true)]
    [InlineData(Bmi, 0x00, false)]
    [InlineData(Bvc, 0x40, false)]
    [InlineData(Bvc, 0x00, true)]
    [InlineData(Bvs, 0x40, true)]
    [InlineData(Bvs, 0x00, false)]
    [InlineData(Bcc, 0x01, false)]
    [InlineData(Bcc, 0x00, true)]
    [InlineData(Bcs, 0x01, true)]
    [InlineData(Bcs, 0x00, false)]
    [InlineData(Bne, 0x02, false)]
    [InlineData(Bne, 0x00, true)]
    [InlineData(Beq, 0x02, true)]
    [InlineData(Beq, 0x00, false)]
    public void EachConditionalBranch_TestsItsOwnFlag(byte opcode, int p, bool taken)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, opcode, 0x10);
        cpu.State.P = (byte)p;

        Assert.Equal(taken ? 3 : 2, cpu.Step());
        Assert.Equal(taken ? 0x2012 : 0x2002, cpu.State.PC);
    }
}
