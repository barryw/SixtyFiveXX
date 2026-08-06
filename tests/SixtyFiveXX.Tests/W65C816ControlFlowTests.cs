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
/// <para>
/// Phase 7d tasks 7 and 8 appended the rest of the part's control flow — the five jumps, three
/// calls and three returns (research document §14.6), then <c>PEA</c>, <c>PEI</c> and <c>PER</c>
/// (§14.7) — for the same reason rather than a new one: what is pinned here is the handful of
/// rules a vector failure would report as one index out of ten thousand. Clark's own worked
/// examples, the emulation-mode page-one wrap and which instructions it reaches, and — the one
/// value no vector comparison can supply — the base <c>PER</c> measures its displacement from.
/// </para>
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

    // ================================================================ task 7: jumps, calls, returns
    //
    // Research document §14.6, which transcribes WDC datasheet Table 5-7 rows 1b, 1c, 2a, 2b,
    // 3a, 3b, 4b, 4c, 22g, 22h and 22i, and Bruce Clark's §5.1.2, §5.4, §5.5, §6.2.2.1,
    // §6.2.2.2 and §6.3.2.

    private const byte JmpAbs = 0x4C;
    private const byte JmpInd = 0x6C;
    private const byte JmpIndX = 0x7C;
    private const byte JmlLong = 0x5C;
    private const byte JmlInd = 0xDC;
    private const byte Jsr = 0x20;
    private const byte JsrIndX = 0xFC;
    private const byte Jsl = 0x22;
    private const byte Rti = 0x40;
    private const byte Rts = 0x60;
    private const byte Rtl = 0x6B;

    // ---------------------------------------------------------------- cycle counts

    /// <summary>
    /// §14.8's cycle column for all eleven, and §14.6's row headers: <c>3</c>, <c>5</c>,
    /// <c>6</c>, <c>4</c>, <c>6</c> for the jumps; <c>6</c>, <c>8</c>, <c>8</c> for the calls;
    /// <c>7-e</c>, <c>6</c>, <c>6</c> for the returns. <c>RTI</c> is the only one of the eleven
    /// whose count depends on <c>e</c> at all — the program bank is pulled in native mode only.
    /// </summary>
    [Theory]
    [InlineData(JmpAbs, false, 3)]
    [InlineData(JmpAbs, true, 3)]
    [InlineData(JmpInd, false, 5)]
    [InlineData(JmpInd, true, 5)]
    [InlineData(JmpIndX, false, 6)]
    [InlineData(JmpIndX, true, 6)]
    [InlineData(JmlLong, false, 4)]
    [InlineData(JmlLong, true, 4)]
    [InlineData(JmlInd, false, 6)]
    [InlineData(JmlInd, true, 6)]
    [InlineData(Jsr, false, 6)]
    [InlineData(Jsr, true, 6)]
    [InlineData(JsrIndX, false, 8)]
    [InlineData(JsrIndX, true, 8)]
    [InlineData(Jsl, false, 8)]
    [InlineData(Jsl, true, 8)]
    [InlineData(Rti, false, 7)]
    [InlineData(Rti, true, 6)]
    [InlineData(Rts, false, 6)]
    [InlineData(Rts, true, 6)]
    [InlineData(Rtl, false, 6)]
    [InlineData(Rtl, true, 6)]
    public void EachControlTransfer_CostsItsDocumentedCycles(byte opcode, bool emulation, int cycles)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation, opcode, 0x34, 0x56, 0x78);

        Assert.Equal(cycles, cpu.Step());
    }

    // ---------------------------------------------------------------- the jumps

    /// <summary>Row 1b: three cycles, and the destination is the operand within the same bank.</summary>
    [Fact]
    public void JmpAbsolute_StaysInTheProgramBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, JmpAbs, 0xCD, 0xAB);

        Assert.Equal(3, cpu.Step());
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    /// <summary>
    /// <b>The assertion that catches a copy of the eight-bit <c>JmpIndHi</c>.</b> Clark §5.4,
    /// verbatim: "on the 65C816, as on the 65C02, (absolute) addressing does not wrap at a page
    /// boundary, i.e. for a JMP ($12FF) the low byte of the destination address is taken from
    /// $12FF and the high byte of the destination address is taken from $1300." The NMOS core
    /// takes it from <c>$1200</c>; this one must not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JmpIndirect_DoesNotReproduceTheNmosPageWrapBug(bool emulation)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation, JmpInd, 0xFF, 0x12);
        ram[0x0012FF] = 0xCD;        // the low byte
        ram[0x001300] = 0xAB;        // the high byte, one past — NOT $001200
        ram[0x001200] = 0xFF;        // what the NMOS bug would read instead

        Assert.Equal(5, cpu.Step());
        Assert.Equal(0xABCD, cpu.State.PC);
    }

    /// <summary>
    /// Clark §5.4's own worked example, which shows what <em>does</em> wrap: "If the K register
    /// is $12 and $000000 contains $34, $00FFFF contains $56, then JMP ($FFFF) jumps to
    /// $123456". The pointer wraps at the bank 0 boundary and nowhere else — and the program
    /// bank is untouched by an indirect <c>JMP</c>.
    /// </summary>
    [Fact]
    public void JmpIndirect_WrapsAtTheBankZeroBoundaryAndKeepsTheProgramBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, JmpInd, 0xFF, 0xFF);
        ram[0x00FFFF] = 0x56;
        ram[0x000000] = 0x34;

        cpu.Step();

        Assert.Equal(0x3456, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    /// <summary>
    /// §14.6 answer 1: <c>$6C</c> takes its pointer from <b>bank 0 regardless of <c>PBR</c></b>
    /// (Clark §5.1.2). A pointer read through the program bank would find the decoy here.
    /// </summary>
    [Fact]
    public void JmpIndirect_ReadsItsPointerFromBankZero()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, JmpInd, 0x00, 0x30);
        ram[0x003000] = 0xCD;
        ram[0x003001] = 0xAB;
        ram[0x7E3000] = 0x11;        // the decoy, in the program bank
        ram[0x7E3001] = 0x22;

        cpu.Step();

        Assert.Equal(0xABCD, cpu.State.PC);
    }

    /// <summary>
    /// §14.6 answer 1: <c>$7C</c> is the other way round — its pointer comes from <b>bank K</b>
    /// (Clark §5.5, "K | $HHLL+X"). Both this and the test above must be present: one alone
    /// passes against a core that reads every jump pointer from the same bank.
    /// </summary>
    [Fact]
    public void JmpAbsoluteIndexedIndirect_ReadsItsPointerFromTheProgramBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, JmpIndX, 0x00, 0x30);
        cpu.State.XFlag = false;
        cpu.State.X = 0x0004;
        ram[0x7E3004] = 0xCD;
        ram[0x7E3005] = 0xAB;
        ram[0x003004] = 0x11;        // the decoy, in bank 0
        ram[0x003005] = 0x22;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
    }

    /// <summary>
    /// Clark §5.5's worked example: <c>X = $000A</c>, <c>JMP ($FFFE,X)</c> reads <c>$120008</c>
    /// — the indexed pointer truncated to sixteen bits, staying inside bank K.
    /// </summary>
    [Fact]
    public void JmpAbsoluteIndexedIndirect_WrapsInsideTheProgramBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, JmpIndX, 0xFE, 0xFF);
        cpu.State.XFlag = false;
        cpu.State.X = 0x000A;
        ram[0x120008] = 0xCD;
        ram[0x120009] = 0xAB;

        cpu.Step();

        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    /// <summary>
    /// <c>JML $llhhbb</c> — row 4b. Four cycles, and the fourth operand byte becomes the
    /// program bank: the only jump whose destination bank comes from the instruction stream.
    /// </summary>
    [Fact]
    public void JmlLong_LoadsTheProgramBankFromItsOperand()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, JmlLong, 0xCD, 0xAB, 0x7E);

        Assert.Equal(4, cpu.Step());
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
    }

    /// <summary>
    /// <c>JML [$nnnn]</c> — row 3a. Six cycles, a three-byte pointer in bank 0, and the
    /// destination bank comes from the pointer's own third byte, not from <c>PBR</c>: the row's
    /// next-opcode cell reads <c>NEW PBR,PC</c>.
    /// </summary>
    [Fact]
    public void JmlIndirectLong_LoadsTheProgramBankFromThePointer()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, JmlInd, 0x00, 0x30);
        ram[0x003000] = 0xCD;
        ram[0x003001] = 0xAB;
        ram[0x003002] = 0x7E;
        ram[0x123000] = 0x11;        // the decoy, in the program bank
        ram[0x123001] = 0x22;
        ram[0x123002] = 0x33;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
    }

    // ---------------------------------------------------------------- the calls

    /// <summary>
    /// Clark §6.2.2.1's own worked example, verbatim: with <c>S = $01FF</c>, a <c>JSR $ABCD</c>
    /// at <c>$123456</c> "stores $34 at $0001FF and $58 at $0001FE, then jumps to $12ABCD,
    /// leaving S = $01FD". Two bytes pushed, high first, and the address pushed is the
    /// instruction's own plus 2 — one less than the next instruction. <c>PBR</c> is untouched.
    /// </summary>
    [Fact]
    public void Jsr_PushesTwoBytesOfTheLastByteAddressAndLeavesTheProgramBankAlone()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x3456, emulation: false, Jsr, 0xCD, 0xAB);
        cpu.State.S = 0x01FF;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0x34, ram[0x0001FF]);
        Assert.Equal(0x58, ram[0x0001FE]);
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
        Assert.Equal(0x01FD, cpu.State.S);
    }

    /// <summary>
    /// Clark §6.2.2.1 for <c>JSL</c>, verbatim: "if the JSL instruction (i.e. the $22 opcode) is
    /// at $12FFFD, then the bytes pushed are (in order): $12, $00, and $00, rather than $13,
    /// $00, and $00." Three bytes — the <b>old</b> program bank first, then the address plus 3,
    /// which wraps inside that bank rather than carrying into the pushed one.
    /// </summary>
    [Fact]
    public void Jsl_PushesTheOldProgramBankThenTheWrappedReturnAddress()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0xFFFD, emulation: false, Jsl, 0xCD, 0xAB, 0x7E);
        cpu.State.S = 0x01FF;

        Assert.Equal(8, cpu.Step());
        Assert.Equal(0x12, ram[0x0001FF]);
        Assert.Equal(0x00, ram[0x0001FE]);
        Assert.Equal(0x00, ram[0x0001FD]);
        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
        Assert.Equal(0x01FC, cpu.State.S);
    }

    /// <summary>
    /// §14.6 answer 3, and the shape the whole sequence hangs on: row 2b puts <c>JSR (abs,X)</c>'s
    /// two pushes at cycles <b>3 and 4</b>, before cycle 5 fetches <c>AAH</c>. No other
    /// instruction in this phase interleaves a push into the middle of operand fetching.
    /// §14.9's gap 8 records that the datasheet row is the only source for the ordering — Clark
    /// gives the cycle count and says nothing about the order — which is why it is pinned here
    /// cycle by cycle rather than only through the final state.
    /// </summary>
    [Fact]
    public void JsrAbsoluteIndexedIndirect_PushesBeforeItFetchesTheHighOperandByte()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x7E, 0x2000, emulation: false, JsrIndX, 0x00, 0x30);
        cpu.State.S = 0x01FF;
        cpu.State.XFlag = false;
        cpu.State.X = 0x0004;
        ram[0x7E3004] = 0xCD;
        ram[0x7E3005] = 0xAB;

        cpu.Tick();                                       // 1: opcode
        Assert.Equal(0x7E2000, cpu.LastAddress);

        cpu.Tick();                                       // 2: AAL
        Assert.Equal(0x7E2001, cpu.LastAddress);

        cpu.Tick();                                       // 3: PCH pushed, BEFORE AAH is read
        Assert.Equal(0x0001FF, cpu.LastAddress);
        Assert.Equal(0x20, ram[0x0001FF]);

        cpu.Tick();                                       // 4: PCL
        Assert.Equal(0x0001FE, cpu.LastAddress);
        Assert.Equal(0x02, ram[0x0001FE]);

        cpu.Tick();                                       // 5: AAH, only now
        Assert.Equal(0x7E2002, cpu.LastAddress);

        cpu.Tick();                                       // 6: the indexing internal cycle
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x7E2002, cpu.LastAddress);

        cpu.Tick();                                       // 7: pointer low, in bank K
        Assert.Equal(0x7E3004, cpu.LastAddress);

        cpu.Tick();                                       // 8: pointer high
        Assert.Equal(0x7E3005, cpu.LastAddress);

        Assert.Equal(0xABCD, cpu.State.PC);
        Assert.Equal(0x01FD, cpu.State.S);
    }

    /// <summary>
    /// <c>JSL</c>'s cycle 5 is an internal cycle at a <b>stack</b> address — <c>0,S</c>, the byte
    /// it has just pushed — not at <c>PBR,PC</c> like every other internal cycle in this phase
    /// bar the block moves'. Row 4c, and measured: <c>22 e 61</c>'s fifth entry is
    /// <c>$000100</c> with the pin string <c>---r</c>.
    /// </summary>
    [Fact]
    public void Jsl_SpendsItsFifthCycleAtTheStackAddressItJustWrote()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Jsl, 0xCD, 0xAB, 0x7E);
        cpu.State.S = 0x01FF;

        for (var i = 0; i < 4; i++) cpu.Tick();
        Assert.Equal(0x0001FF, cpu.LastAddress);          // 4: the program-bank push

        cpu.Tick();                                       // 5: an IO at the same address
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x0001FF, cpu.LastAddress);
    }

    // ---------------------------------------------------------------- the returns

    /// <summary>
    /// §14.6 answer 5, Clark §6.2.2.2: <c>RTS</c> "pulls the low byte, then the high byte of the
    /// program counter from the stack, then increments the program counter". Two bytes, plus
    /// one, and the program bank is not touched.
    /// </summary>
    [Fact]
    public void Rts_PullsTwoBytesAndAddsOne()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Rts);
        cpu.State.S = 0x01FD;
        ram[0x0001FE] = 0xCD;
        ram[0x0001FF] = 0xAB;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0xABCE, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// <c>RTL</c> pulls three: <c>PCL</c>, <c>PCH</c>, then the program bank — and adds one to
    /// the program counter only. Clark §6.2.2.2, verbatim: "RTL … pulls the low byte, then the
    /// high byte of the program counter from the stack, then increments the program counter,
    /// then pulls the K register."
    /// </summary>
    [Fact]
    public void Rtl_PullsThreeBytesAndAddsOneToTheProgramCounterOnly()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Rtl);
        cpu.State.S = 0x01FC;
        ram[0x0001FD] = 0xCD;
        ram[0x0001FE] = 0xAB;
        ram[0x0001FF] = 0x7E;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0xABCE, cpu.State.PC);
        Assert.Equal(0x7E, cpu.State.PBR);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// <b><c>RTL</c>'s increment does not carry into the bank.</b> Clark §6.2.2.2, verbatim: "if
    /// $FF, $FF, and $12 are pulled from the stack, the instruction at $120000 (rather than
    /// $130000) will be executed next." Zero vector coverage — no <c>6b</c> vector in either
    /// mode pulls <c>$FFFF</c> — so this test is the only thing that certifies it.
    /// </summary>
    [Fact]
    public void Rtl_IncrementWrapsInsideThePulledBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x33, 0x2000, emulation: false, Rtl);
        cpu.State.S = 0x01FC;
        ram[0x0001FD] = 0xFF;
        ram[0x0001FE] = 0xFF;
        ram[0x0001FF] = 0x12;

        cpu.Step();

        Assert.Equal(0x0000, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    /// <summary>
    /// Clark §6.3.2's own worked example, verbatim: "S = $01FB, e = 0, $0001FC..FF = $08 $12 $34
    /// $56 → jumps to $563412, S = $01FF, P = $08". Four bytes in native mode — <c>P</c>, then
    /// the program counter low and high, then the program bank (§14.6 answer 6, <b>yes</b>, and
    /// <b>before</b> the return address) — and, unlike <c>RTS</c> and <c>RTL</c>, <b>no</b> "+1":
    /// "Note that unlike RTS (and RTL), the program counter is not incremented after it is
    /// pulled from the stack."
    /// </summary>
    [Fact]
    public void RtiNative_PullsFourBytesRestoresTheProgramBankAndAddsNothing()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x33, 0x2000, emulation: false, Rti);
        cpu.State.S = 0x01FB;
        ram[0x0001FC] = 0x08;
        ram[0x0001FD] = 0x12;
        ram[0x0001FE] = 0x34;
        ram[0x0001FF] = 0x56;

        Assert.Equal(7, cpu.Step());
        Assert.Equal(0x3412, cpu.State.PC);
        Assert.Equal(0x56, cpu.State.PBR);
        Assert.Equal(0x08, cpu.State.P);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// Emulation mode pulls three, not four, and leaves <c>PBR</c> alone — datasheet note 7 on
    /// row 22g's seventh cycle, Clark §6.3.2's "In emulation mode, the P register is pulled,
    /// then the 16-bit program counter is pulled."
    /// </summary>
    [Fact]
    public void RtiEmulation_PullsThreeBytesAndLeavesTheProgramBankAlone()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x33, 0x2000, emulation: true, Rti);
        cpu.State.S = 0x01FC;
        ram[0x0001FD] = 0x08;
        ram[0x0001FE] = 0x34;
        ram[0x0001FF] = 0x12;
        ram[0x000100] = 0x99;        // what a fourth pull would take the bank from

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0x1234, cpu.State.PC);
        Assert.Equal(0x33, cpu.State.PBR);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// <b>Defect 1, on the second instruction that can hit it.</b> The shared
    /// <c>MicroOp.PullP</c> masks <c>~Flag.B</c>, which is the same bit as <c>~Flag.X</c> — on a
    /// native 65816 that silently clears the index-width flag. <c>RTI</c> restores <c>P</c>
    /// verbatim instead: measured in §14.6's terms, all 10,000 <c>40.n</c> vectors' final
    /// <c>P</c> equals the pulled byte exactly.
    /// <para>
    /// The pulled byte sets <c>x</c> (bit 4) and clears <c>m</c> (bit 5) — opposed, because a
    /// core that confused the two flags would pass a test that set both the same way — and the
    /// core starts with the opposite pair.
    /// </para>
    /// </summary>
    [Fact]
    public void RtiNative_RestoresTheIndexWidthFlagRatherThanClearingIt()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x33, 0x2000, emulation: false, Rti);
        cpu.State.S = 0x01FB;
        cpu.State.M = true;                 // m set, x clear: the opposite of what is pulled
        cpu.State.XFlag = false;
        cpu.State.X = 0x1234;
        cpu.State.Y = 0x5678;
        ram[0x0001FC] = 0x10;               // x = 1, m = 0
        ram[0x0001FD] = 0x00;
        ram[0x0001FE] = 0x40;
        ram[0x0001FF] = 0x00;

        cpu.Step();

        Assert.Equal(0x10, cpu.State.P);
        Assert.True(cpu.State.XFlag);
        Assert.False(cpu.State.M);

        // Setting x forces XH = YH = $00 the same instant SEP does — the rule §14.1 measured
        // for PLP, applied here because RTI writes the same flag the same way.
        Assert.Equal(0x0034, cpu.State.X);
        Assert.Equal(0x0078, cpu.State.Y);
    }

    // ---------------------------------------------------------------- the emulation-mode wrap

    /// <summary>
    /// Clark §5.22: "For all interrupts and 'old' instructions, when the e flag is 1, the
    /// address of the data for an 8-bit push is <c>0,1,SL</c> … Otherwise … <c>0,S</c>."
    /// <c>JSR</c> is an old instruction and wraps inside page one: from <c>S = $0100</c> the
    /// second push lands at <c>$0001FF</c>, not <c>$0000FF</c>. Measured — <c>20 e 1023</c>.
    /// <para>
    /// <c>JSR (abs,X)</c> wraps too, which the old/new reading alone does not predict: the
    /// addressing mode is new to the 65816 but the instruction is not. Measured — <c>fc e 458</c>
    /// starts at <c>SL = $00</c> and writes <c>$000100</c> then <c>$0001FF</c>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Jsr)]
    [InlineData(JsrIndX)]
    public void JsrInEmulationMode_WrapsItsPushesInsidePageOne(byte opcode)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: true, opcode, 0x00, 0x30);
        cpu.State.S = 0x0100;

        cpu.Step();

        Assert.Equal(0x20, ram[0x000100]);
        Assert.Equal(0x02, ram[0x0001FF]);
        Assert.Equal(0x00, ram[0x0000FF]);
        Assert.Equal(0x01FE, cpu.State.S);
    }

    /// <summary>
    /// <c>JSL</c> is new to the 65816 and does <b>not</b> wrap: from <c>S = $0100</c> its second
    /// and third pushes land below page one entirely. Clark §5.22's "Otherwise … <c>0,S</c>",
    /// and measured — <c>22 e 61</c> writes <c>$000100</c>, <c>$0000FF</c>, <c>$0000FE</c> and
    /// still ends with <c>S = $01FD</c>, because emulation mode has no storage for <c>SH</c>.
    /// </summary>
    [Fact]
    public void JslInEmulationMode_PushesBelowPageOneAndStillSettlesS()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: true, Jsl, 0xCD, 0xAB, 0x7E);
        cpu.State.S = 0x0100;

        cpu.Step();

        Assert.Equal(0x12, ram[0x000100]);
        Assert.Equal(0x20, ram[0x0000FF]);
        Assert.Equal(0x03, ram[0x0000FE]);   // JSL pushes its own address plus 3, not plus 2
        Assert.Equal(0x01FD, cpu.State.S);
    }

    /// <summary>
    /// The pull side of the same rule. <c>RTS</c> and <c>RTI</c> are old and wrap — from
    /// <c>S = $01FF</c> the first pull is at <c>$000100</c> (measured, <c>60 e 121</c> and
    /// <c>40 e 50</c>) — while <c>RTL</c> is new and reads straight on into page two (measured,
    /// <c>6b e 104</c>).
    /// </summary>
    [Theory]
    [InlineData(Rts, 0x000100)]
    [InlineData(Rti, 0x000100)]
    [InlineData(Rtl, 0x000200)]
    public void ReturnsInEmulationMode_WrapOnlyIfTheyAreOldInstructions(byte opcode, int firstPull)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: true, opcode);
        cpu.State.S = 0x01FF;

        cpu.Tick();
        cpu.Tick();
        cpu.Tick();
        cpu.Tick();                          // cycle 4: the first stack read

        Assert.Equal(firstPull, cpu.LastAddress);
    }

    // ------------------------------------------------- PEA, PEI and PER (research §14.7)

    private const byte Pea = 0xF4;
    private const byte Pei = 0xD4;
    private const byte Per = 0x62;

    /// <summary>
    /// <c>PEA</c> pushes its own two operand bytes, high first, and pushes <b>two of them with
    /// <c>m = 1</c></b> — which is the assertion that makes "sixteen bits whatever the flags say"
    /// (Clark §6.8.1) a real claim rather than a restatement of the default. <c>m</c> and
    /// <c>x</c> are set to opposed values so a core that confused the two aliased bits would
    /// fail rather than pass by accident. Five cycles, row 22d.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Pea_PushesBothOperandBytesWhateverTheWidthFlagsSay(bool m, bool x)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Pea, 0x34, 0x12);
        cpu.State.S = 0x01FF;
        cpu.State.M = m;
        cpu.State.XFlag = x;

        Assert.Equal(5, cpu.Step());
        Assert.Equal(0x12, ram[0x0001FF]);
        Assert.Equal(0x34, ram[0x0001FE]);
        Assert.Equal(0x01FD, cpu.State.S);
        Assert.Equal(0x2003, cpu.State.PC);
    }

    /// <summary>
    /// Clark §6.8.1, verbatim: "PEA #$1234 … simply pushes the value $1234, but does not access
    /// memory location $1234 (in any bank)". Asserted from the bus log, because the value pushed
    /// is identical either way — only the accesses tell an immediate apart from an absolute.
    /// </summary>
    [Fact]
    public void Pea_NeverReadsTheAddressItsOperandLooksLike()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Pea, 0x34, 0x12);

        cpu.Step();

        Assert.DoesNotContain(ram.Log, a => (a.Address & 0xFFFF) == 0x1234);
    }

    /// <summary>
    /// Clark §6.8.1: <c>PEI</c> "pushes the same 16-bit value that (assuming the m flag is 0)
    /// LDA $12 loads into the accumulator, rather that the value that LDA ($12) loads" — the word
    /// read from the direct page in <b>bank 0</b>, not the value that word points at. Both decoys
    /// are laid: one where a dereference would read, one where a <c>DBR</c>-relative read would.
    /// Six cycles here, <c>DL</c> being <c>$00</c>.
    /// </summary>
    [Fact]
    public void Pei_PushesTheDirectPageWordAndNotWhatItPointsAt()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Pei, 0x40);
        cpu.State.S = 0x01FF;
        cpu.State.M = true;                  // 8-bit accumulator: PEI still pushes 16 bits
        cpu.State.XFlag = false;
        cpu.State.DP = 0x0100;
        cpu.State.DBR = 0x7E;
        ram[0x000140] = 0xCD;                // 0,D+DO
        ram[0x000141] = 0xAB;                // 0,D+DO+1
        ram[0x00ABCD] = 0x99;                // the decoy a dereference would push
        ram[0x00ABCE] = 0x88;
        ram[0x7E0140] = 0x11;                // the decoy a DBR-relative read would take
        ram[0x7E0141] = 0x22;

        Assert.Equal(6, cpu.Step());
        Assert.Equal(0xAB, ram[0x0001FF]);
        Assert.Equal(0xCD, ram[0x0001FE]);
        Assert.Equal(0x01FD, cpu.State.S);
        Assert.Equal(0x2002, cpu.State.PC);
    }

    /// <summary>
    /// <c>PEI</c> is the only opcode in this phase carrying a <c>w</c> term — Clark's
    /// <c>D4 2 6+w dir PEI</c>, and Table 5-7 row 22e's note 2 cycle, taken when <c>DL</c> is not
    /// <c>$00</c>. Research document §14.8: the phase's only direct-page instruction, and so its
    /// only <c>w</c>.
    /// </summary>
    [Theory]
    [InlineData(0x0100, 6)]
    [InlineData(0x0101, 7)]
    public void Pei_CostsTheDirectPagePenaltyOnlyWhenTheLowByteOfDIsNonZero(int dp, int cycles)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: false, Pei, 0x40);
        cpu.State.DP = (ushort)dp;

        Assert.Equal(cycles, cpu.Step());
    }

    /// <summary>
    /// <b>The one value in this task that no test can copy from the implementation.</b> Clark
    /// §5.14: "PER adds the immediate data to the address of the next instruction. This is the
    /// same formula that relative16 addressing uses", and §5.18's relative16 is
    /// <c>K : PC+3+$HHLL</c> with <c>PC</c> the opcode's own address. So the base is the
    /// instruction's address plus its three bytes — computed here from <c>At</c> and the literal
    /// 3, so that a core measuring from the opcode, from the last operand byte, or from anywhere
    /// else fails by exactly the off-by-one it made. Six cycles, row 22f.
    /// </summary>
    [Theory]
    [InlineData(0x0000)]                     // pushes the address of the next instruction itself
    [InlineData(0x0007)]
    [InlineData(0xFFFD)]                     // -3: lands back on the opcode
    public void Per_PushesTheNextInstructionsAddressPlusTheDisplacement(int displacement)
    {
        const ushort At = 0x2000;

        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, At, emulation: false,
            Per, (byte)displacement, (byte)(displacement >> 8));
        cpu.State.S = 0x01FF;
        cpu.State.M = true;                  // 8-bit accumulator: PER still pushes 16 bits
        cpu.State.XFlag = false;

        var expected = (ushort)(At + 3 + displacement);

        Assert.Equal(6, cpu.Step());
        Assert.Equal(expected >> 8, ram[0x0001FF]);
        Assert.Equal(expected & 0xFF, ram[0x0001FE]);
        Assert.Equal(0x01FD, cpu.State.S);
        Assert.Equal(0x12, cpu.State.PBR);   // nothing is jumped to and no bank is pushed
    }

    /// <summary>
    /// The sum wraps inside the program bank and never carries into <c>PBR</c> — Clark §5.1.2,
    /// "the Program Counter … is confined to bank K". A <c>PER</c> at <c>$12:FFFE</c> has its next
    /// instruction at <c>$12:0001</c> already, before any displacement is added.
    /// </summary>
    [Fact]
    public void Per_WrapsInsideTheProgramBank()
    {
        const ushort At = 0xFFFE;
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, At, emulation: false, Per, 0x10, 0x00);
        cpu.State.S = 0x01FF;

        var expected = (ushort)((At + 3 + 0x0010) & 0xFFFF);   // $0011, not $010011

        cpu.Step();

        Assert.Equal(expected >> 8, ram[0x0001FF]);
        Assert.Equal(expected & 0xFF, ram[0x0001FE]);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    /// <summary>
    /// All three are <b>new</b> to the 65816, so Clark §5.22's "for all interrupts and 'old'
    /// instructions" does not reach them and their pushes run straight out of page one:
    /// from <c>S = $0100</c> the second byte lands at <c>$0000FF</c>, not <c>$0001FF</c>. The
    /// counterweight is <see cref="JsrInEmulationMode_WrapsItsPushesInsidePageOne"/>, where an
    /// old instruction at the same <c>S</c> does wrap. <c>S</c> still settles back into page one,
    /// emulation mode having no storage for <c>SH</c>.
    /// </summary>
    [Theory]
    [InlineData(Pea)]
    [InlineData(Pei)]
    [InlineData(Per)]
    public void TheStackAddressPushesInEmulationMode_DoNotWrapInsidePageOne(byte opcode)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, 0x12, 0x2000, emulation: true, opcode, 0x40, 0x00);
        cpu.State.S = 0x0100;

        cpu.Step();

        Assert.Equal(2, ram.Log.Count(a => a.Write));
        Assert.Contains(ram.Log, a => a.Write && a.Address == 0x000100);
        Assert.Contains(ram.Log, a => a.Write && a.Address == 0x0000FF);
        Assert.Equal(0x01FE, cpu.State.S);
    }
}
