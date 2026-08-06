using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's stack: sixteen bits wide in native mode, anywhere in bank 0, and forced into
/// page one in emulation mode. Every assertion here discriminates a rule that no vector in
/// this phase's own files necessarily reaches.
/// </summary>
public class W65C816StackTests
{
    /// <summary>
    /// A native-mode push lands at S itself, not at $0100 + SL. With S outside page one, the
    /// eight-bit formula and the sixteen-bit one give different addresses, which is the whole
    /// point of the plumbing this task adds.
    /// </summary>
    [Fact]
    public void NativePush_LandsAtTheFullSixteenBitStackPointer()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;             // 8-bit accumulator
        cpu.State.XFlag = false;        // opposed, so a width read from x would be visible
        cpu.State.S = 0x1FFF;
        cpu.State.A = 0x0042;

        cpu.Step();

        Assert.Equal(0x42, ram[0x001FFF]);
        Assert.Equal(0x1FFE, cpu.State.S);
    }

    /// <summary>
    /// An emulation-mode push stays in page one and wraps within it: S = $00 pushes to $0100
    /// and leaves S at $01FF, never $00FF and never $0000.
    /// </summary>
    [Fact]
    public void EmulationPush_WrapsWithinPageOne()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x0100;
        cpu.State.A = 0x0042;

        cpu.Step();

        Assert.Equal(0x42, ram[0x000100]);
        Assert.Equal(0x01FF, cpu.State.S);
    }

    /// <summary>
    /// Clark §5.1.1's old/new split, which research document §14.1 does not carry across to the
    /// stack: in emulation mode an "old" pull — one the 65C02 also has — wraps inside page one,
    /// so PLP at S = $01FF reads $000100. Vector <c>28 e 311</c>.
    /// </summary>
    [Fact]
    public void EmulationPull_OldInstruction_WrapsWithinPageOne()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x28;             // PLP — available on the 65C02, so "old"
        ram[0x000100] = Flag.C;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x01FF;

        cpu.Step();

        Assert.True(cpu.State.C);
        Assert.Equal(0x0100, cpu.State.S);
    }

    /// <summary>
    /// The other half of the same rule, and the one the phase 7d brief and §14.1 both get wrong:
    /// a "new" pull does NOT wrap, so PLB at S = $01FF reads $000200. Vector <c>ab e 75</c>,
    /// which starts at S = $27FF and reads exactly there.
    /// </summary>
    [Fact]
    public void EmulationPull_NewInstruction_DoesNotWrap()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAB;             // PLB — 65816 only, so "new"
        ram[0x000200] = 0x48;
        ram[0x000100] = 0x99;           // what a wrapping pull would have read instead

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x01FF;

        cpu.Step();

        Assert.Equal(0x48, cpu.State.DBR);
        Assert.Equal(0x0100, cpu.State.S);   // SH snaps back to $01 once the access is done
    }

    /// <summary>
    /// The discriminating push, vector <c>0b e 435</c>: PHD from S = $0100 writes its high byte
    /// at $000100 and its low byte at $0000FF — below page one entirely — and still ends with
    /// S = $01FE. Emulation mode has no storage for SH, but the address adder still carries out
    /// of SL.
    /// </summary>
    [Fact]
    public void EmulationPush_NewInstruction_CarriesOutOfPageOne()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0B;             // PHD

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x0100;
        cpu.State.DP = 0x1234;

        cpu.Step();

        Assert.Equal(0x12, ram[0x000100]);
        Assert.Equal(0x34, ram[0x0000FF]);
        Assert.Equal(0x01FE, cpu.State.S);
    }

    /// <summary>
    /// A 16-bit PHA pushes the high byte first, so the low byte ends up at the lower address.
    /// </summary>
    [Fact]
    public void SixteenBitPush_PushesHighByteFirst()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x48;             // PHA

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;            // 16-bit accumulator
        cpu.State.XFlag = true;         // opposed
        cpu.State.S = 0x1FFF;
        cpu.State.A = 0x1234;

        cpu.Step();

        Assert.Equal(0x12, ram[0x001FFF]);
        Assert.Equal(0x34, ram[0x001FFE]);
        Assert.Equal(0x1FFD, cpu.State.S);
    }

    /// <summary>
    /// Defect 1, carried since phase 7b. In native mode bit 4 of P is the index-width select,
    /// not the break flag, so PLP must load it verbatim. The shared MicroOp.PullP masks
    /// ~Flag.B — the same bit — and would silently clear x here.
    /// </summary>
    [Fact]
    public void Plp_NativeMode_DoesNotClearTheIndexWidthFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x28;             // PLP
        ram[0x1FFF] = Flag.X;           // x set, everything else clear

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.S = 0x1FFE;

        cpu.Step();

        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x1FFF, cpu.State.S);
    }

    /// <summary>
    /// PLP that sets x must narrow the index registers the same instant SEP does, or a
    /// following indexed instruction reads a high byte that cannot exist at x = 1.
    /// </summary>
    [Fact]
    public void Plp_SettingIndexWidth_NarrowsXAndY()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x28;             // PLP
        ram[0x1FFF] = Flag.X;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.S = 0x1FFE;
        cpu.State.X = 0xBEEF;
        cpu.State.Y = 0xCAFE;

        cpu.Step();

        Assert.Equal(0x00EF, cpu.State.X);
        Assert.Equal(0x00FE, cpu.State.Y);
    }

    /// <summary>
    /// PHP pushes P verbatim, all eight bits, in native mode — research document §14.1's
    /// measured block, 10,000 of 10,000 <c>08 n</c> vectors. Nothing is forced on the way out,
    /// which is the half of the rule the vectors cannot see in emulation mode (there, bits 4
    /// and 5 are already set in every vector's initial state).
    /// </summary>
    [Fact]
    public void Php_NativeMode_PushesPVerbatim()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x08;             // PHP

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;            // bit 5 clear
        cpu.State.XFlag = false;        // bit 4 clear, opposed to nothing — both must survive
        cpu.State.S = 0x1FFF;
        cpu.State.C = true;

        cpu.Step();

        Assert.Equal(Flag.C, ram[0x001FFF]);
        Assert.Equal(0x1FFE, cpu.State.S);
    }

    /// <summary>
    /// PHK pushes the program bank, PHB the data bank, and PLB loads the data bank and sets
    /// N and Z from it as an eight-bit result. One byte each, regardless of m and x.
    /// </summary>
    [Fact]
    public void Phk_Phb_Plb_MoveOneByteEach()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x4B;           // PHK
        ram[0x120001] = 0x8B;           // PHB
        ram[0x120002] = 0xAB;           // PLB

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;          // Make() defaults PC to $C000; these opcodes are at $120000
        cpu.State.DBR = 0x80;
        cpu.State.S = 0x1FFF;

        cpu.Step();                      // PHK
        Assert.Equal(0x12, ram[0x001FFF]);

        cpu.Step();                      // PHB
        Assert.Equal(0x80, ram[0x001FFE]);

        cpu.Step();                      // PLB — pulls the $80 it just pushed
        Assert.Equal(0x80, cpu.State.DBR);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// PHD and PLD move all sixteen bits of the direct register whatever m and x say, and
    /// PLD's flags come from the sixteen-bit result.
    /// </summary>
    [Fact]
    public void Phd_Pld_AreAlwaysSixteenBits()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0B;             // PHD
        ram[0xC001] = 0x2B;             // PLD

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;             // both narrow — PHD must ignore them
        cpu.State.XFlag = true;
        cpu.State.S = 0x1FFF;
        cpu.State.DP = 0x8000;

        cpu.Step();                      // PHD
        Assert.Equal(0x80, ram[0x001FFF]);
        Assert.Equal(0x00, ram[0x001FFE]);
        Assert.Equal(0x1FFD, cpu.State.S);

        cpu.State.DP = 0x0000;
        cpu.Step();                      // PLD
        Assert.Equal(0x8000, cpu.State.DP);
        Assert.True(cpu.State.N);
    }

    /// <summary>
    /// Cycle counts, from research document §14.1's table: a push is <c>4-m</c>/<c>4-x</c> or a
    /// flat 3 or 4, a pull one more in every case. Asserted here rather than left to the vector
    /// files because a sequence one cycle short surfaces there as an
    /// <c>UndefinedOpcodeException</c> naming the wrong opcode entirely.
    /// </summary>
    [Theory]
    [InlineData(0x48, true, 3)]      // PHA, m = 1
    [InlineData(0x48, false, 4)]     // PHA, m = 0
    [InlineData(0x08, true, 3)]      // PHP, flat
    [InlineData(0x08, false, 3)]
    [InlineData(0x8B, false, 3)]     // PHB, flat
    [InlineData(0x4B, false, 3)]     // PHK, flat
    [InlineData(0x0B, true, 4)]      // PHD, flat 4 — D has no narrow form
    [InlineData(0x68, true, 4)]      // PLA, m = 1
    [InlineData(0x68, false, 5)]     // PLA, m = 0
    [InlineData(0x28, false, 4)]     // PLP, flat
    [InlineData(0xAB, false, 4)]     // PLB, flat
    [InlineData(0x2B, true, 5)]      // PLD, flat 5
    public void PushAndPullCycleCounts(byte opcode, bool narrowA, int expected)
    {
        var ram = new BankedBus();
        ram[0xC000] = opcode;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = narrowA;
        cpu.State.XFlag = !narrowA;     // opposed, so an arm reading the wrong flag is visible
        cpu.State.S = 0x1FFF;

        Assert.Equal(expected, cpu.Step());
    }

    /// <summary>
    /// The index-sized pushes and pulls take their width from <c>x</c>, not <c>m</c> — the
    /// discrimination the opposed flags above exist for, stated directly.
    /// </summary>
    [Theory]
    [InlineData(0xDA, true, 3)]      // PHX, x = 1
    [InlineData(0xDA, false, 4)]     // PHX, x = 0
    [InlineData(0x5A, true, 3)]      // PHY
    [InlineData(0x5A, false, 4)]
    [InlineData(0xFA, true, 4)]      // PLX, x = 1
    [InlineData(0xFA, false, 5)]     // PLX, x = 0
    [InlineData(0x7A, true, 4)]      // PLY
    [InlineData(0x7A, false, 5)]
    public void IndexPushAndPullCycleCountsFollowX(byte opcode, bool narrowIndex, int expected)
    {
        var ram = new BankedBus();
        ram[0xC000] = opcode;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = narrowIndex;
        cpu.State.M = !narrowIndex;     // opposed
        cpu.State.S = 0x1FFF;

        Assert.Equal(expected, cpu.Step());
    }

    /// <summary>
    /// Both internal cycles of a pull drive <c>PBR,PC+1</c>, not a stack address — research
    /// document §14.1's row 22b, and measured directly from <c>28 n 1</c>, whose cycles 2 and 3
    /// are both at <c>$291013</c> while <c>S</c> is <c>$2FE2</c>. The phase 7d brief specified a
    /// stack address for the second of the two; the row and the vectors both say otherwise.
    /// </summary>
    [Fact]
    public void PullInternalCycles_DriveTheProgramCounter_NotTheStack()
    {
        var ram = new BankedBus();
        ram[0x291012] = 0x28;           // PLP

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = true;         // opposed
        cpu.State.PBR = 0x29;
        cpu.State.PC = 0x1012;
        cpu.State.S = 0x2FE2;

        cpu.Tick();                     // opcode fetch at PBR,PC
        cpu.Tick();                     // internal cycle 2
        Assert.Equal(0x291013, cpu.LastAddress);
        Assert.Equal(BusPins.None, cpu.LastPins);

        cpu.Tick();                     // internal cycle 3
        Assert.Equal(0x291013, cpu.LastAddress);
        Assert.Equal(BusPins.None, cpu.LastPins);

        cpu.Tick();                     // the pull itself, at 0,S+1
        Assert.Equal(0x002FE3, cpu.LastAddress);
        Assert.Equal(BusPins.Vda, cpu.LastPins);
    }

    /// <summary>
    /// A push's one internal cycle drives <c>PBR,PC+1</c> too, and the write that follows is a
    /// data access — <c>VDA</c> alone, never <c>MLB</c> or <c>VPB</c> (§14.1: both are inactive
    /// on every cycle of rows 22b and 22c).
    /// </summary>
    [Fact]
    public void PushInternalCycleAndWrite_DriveTheRightAddressesAndPins()
    {
        var ram = new BankedBus();
        ram[0xED_C5F7] = 0x08;          // PHP

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.XFlag = true;         // opposed
        cpu.State.PBR = 0xED;
        cpu.State.PC = 0xC5F7;
        cpu.State.S = 0xF483;

        cpu.Tick();                     // opcode fetch
        cpu.Tick();                     // internal cycle 2
        Assert.Equal(0xEDC5F8, cpu.LastAddress);
        Assert.Equal(BusPins.None, cpu.LastPins);

        cpu.Tick();                     // the write, at 0,S
        Assert.Equal(0x00F483, cpu.LastAddress);
        Assert.Equal(BusPins.Vda, cpu.LastPins);
        Assert.True(cpu.AtInstructionBoundary);
    }
}
