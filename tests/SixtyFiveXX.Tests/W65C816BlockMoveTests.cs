using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// <c>MVN</c> (<c>$54</c>) and <c>MVP</c> (<c>$44</c>) — the only instructions in this engine
/// that move <c>PC</c> backwards. Every assertion below is written against research document
/// §14.3, which transcribes WDC datasheet Table 5-7 rows 9a/9b and Bruce Clark's §5.19/§6.6 and
/// then measures <c>54.n.json</c> and <c>44.n.json</c> directly.
/// <para>
/// The vectors cover the cycle-by-cycle behaviour abundantly — 40,000 of them — but they are
/// truncated at 100 cycles and so never show a <em>complete</em> long move, and they carry no
/// interrupt stimulus at all. Both gaps are closed here: <see cref="PcRewinds_UntilTheCountIsExhausted"/>
/// runs a move to completion across two <c>Step()</c> calls, and
/// <see cref="AnInterruptCanLandBetweenIterations"/> asserts the property that makes the rewind
/// the right implementation rather than an internal loop.
/// </para>
/// </summary>
public class W65C816BlockMoveTests
{
    private const byte Mvn = 0x54;
    private const byte Mvp = 0x44;

    private const byte SrcBank = 0x7F;
    private const byte DstBank = 0x7E;

    /// <summary>
    /// Lays a block move at <c>$00C000</c> with the destination bank byte first and the source
    /// bank byte second — research document §14.3: "the destination bank byte comes first in the
    /// instruction stream and the source bank byte second", which is the reverse of the
    /// assembler's <c>MVN #source,#dest</c> syntax.
    /// </summary>
    private static Cpu<RefBus, W65C816Variant> Machine(BankedBus ram, byte opcode)
    {
        ram[0x00C000] = opcode;
        ram[0x00C001] = DstBank;
        ram[0x00C002] = SrcBank;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.DBR = 0x12;           // neither bank operand, so a stale DBR would show
        return cpu;
    }

    /// <summary>
    /// One iteration moves one byte from <c>SBA,X</c> to <c>DBA,Y</c> and costs seven cycles —
    /// §14.3's rows 9a/9b, "1 opcode, 3 bytes, 7 cycles", and its measured
    /// <c>54 e 9990</c>, the smallest complete vector in the corpus.
    /// </summary>
    [Fact]
    public void OneIteration_MovesOneByteAndCostsSevenCycles()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x7F0010] = 0xA5;

        cpu.State.A = 0x0000;           // bytes to move minus one: exactly one byte
        cpu.State.X = 0x0010;
        cpu.State.Y = 0x0020;

        Assert.Equal(7, cpu.Step());
        Assert.Equal(0xA5, ram[0x7E0020]);
    }

    /// <summary>
    /// <c>DBR</c> ends as the destination bank — the instruction's <em>second</em> byte.
    /// Datasheet §7.18, verbatim in §14.3: "The MVN and MVP instructions change the Data Bank
    /// Register to the value of the second byte of the instruction (destination bank address)."
    /// </summary>
    [Fact]
    public void DataBankRegister_BecomesTheDestinationBank()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        cpu.State.A = 0x0000;

        cpu.Step();

        Assert.Equal(DstBank, cpu.State.DBR);
    }

    /// <summary>
    /// <c>MVN</c> increments both index registers and <c>MVP</c> decrements both — §14.3, from
    /// Table 5-7's own annotation ("x,y Increment" for MVN, "x,y Decrement" for MVP) and
    /// confirmed there by the direction of the measured addresses, not only by the register
    /// deltas. The accumulator decrements for both.
    /// </summary>
    [Theory]
    [InlineData(Mvn, +1)]
    [InlineData(Mvp, -1)]
    public void IndexRegistersMoveInTheOpcodesOwnDirection(byte opcode, int delta)
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, opcode);
        cpu.State.A = 0x0007;
        cpu.State.X = 0x0100;
        cpu.State.Y = 0x0200;

        cpu.Step();

        Assert.Equal(0x0100 + delta, cpu.State.X);
        Assert.Equal(0x0200 + delta, cpu.State.Y);
        Assert.Equal(0x0006, cpu.State.A);
    }

    /// <summary>
    /// The read is at <c>SBA,X</c> and the write at <c>DBA,Y</c>, both with the index register as
    /// it stood <em>before</em> the iteration's update — §14.3's transcribed cycles 4 and 5, and
    /// the measured <c>54 n 1</c>, whose first two source reads are <c>$6D0018</c> and
    /// <c>$6D0019</c>.
    /// </summary>
    [Fact]
    public void ConsecutiveIterations_WalkTheSourceAndDestinationAddresses()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x7F0010] = 0x11;
        ram[0x7F0011] = 0x22;

        cpu.State.A = 0x0001;           // two bytes
        cpu.State.X = 0x0010;
        cpu.State.Y = 0x0020;

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x11, ram[0x7E0020]);
        Assert.Equal(0x22, ram[0x7E0021]);
    }

    /// <summary>
    /// <c>PC</c> is rewound to the instruction's own address while the count is not exhausted and
    /// left at the next instruction when it is — Clark §6.6, quoted in §14.3: "the program counter
    /// will be the address of the next instruction … if the accumulator is $FFFF, and … the
    /// address of the MVN or MVP if the accumulator is not $FFFF (i.e. the instruction jumps to
    /// itself if the accumulator is not $FFFF)". The rewind is what makes the second
    /// <see cref="Cpu{TBus,TVariant}.Step"/> re-execute the same instruction.
    /// </summary>
    [Fact]
    public void PcRewinds_UntilTheCountIsExhausted()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x7F0010] = 0x11;
        ram[0x7F0011] = 0x22;

        cpu.State.A = 0x0001;           // two bytes: one rewind, then the exit
        cpu.State.X = 0x0010;
        cpu.State.Y = 0x0020;

        Assert.Equal(7, cpu.Step());
        Assert.Equal(0xC000, cpu.State.PC);      // rewound onto its own opcode
        Assert.Equal(0x0000, cpu.State.A);

        Assert.Equal(7, cpu.Step());
        Assert.Equal(0xC003, cpu.State.PC);      // past the three-byte instruction
        Assert.Equal(0xFFFF, cpu.State.A);
    }

    /// <summary>
    /// The count is a full sixteen-bit decrement whatever <c>m</c> says, and the <c>B</c>
    /// accumulator is clobbered — Clark §6.6's three unqualified "(16-bit)"s, and §14.3's
    /// measurement of <c>54 n 1</c>, which has <c>m = 1</c> and takes <c>A</c> from <c>$EF9B</c>
    /// to <c>$EF8D</c>. Both width flags are set to opposed values so a decrement that consulted
    /// <c>x</c> instead of <c>m</c> would not pass by accident.
    /// </summary>
    [Fact]
    public void TheCountIsSixteenBitEvenWhenMSelectsEightBits()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        cpu.State.M = true;             // 8-bit accumulator …
        cpu.State.XFlag = false;        // … and opposed index width
        cpu.State.A = 0x0100;

        cpu.Step();

        Assert.Equal(0x00FF, cpu.State.A);
    }

    /// <summary>
    /// With <c>x = 1</c> the index registers are eight bits and wrap within the low byte:
    /// measured in §14.3's own terms by <c>54 n 63</c>, whose source reads run
    /// <c>…$B300FF</c> then <c>$B30000</c> and whose <c>X</c> ends at <c>$01</c> from
    /// <c>$F3</c> after fourteen increments. Both width flags opposed again.
    /// </summary>
    [Fact]
    public void IndexRegistersAreEightBitAndWrapInsideTheLowByte_WhenXIsSet()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x7F00FF] = 0x5A;

        cpu.State.XFlag = true;         // 8-bit indexes …
        cpu.State.M = false;            // … and opposed accumulator width
        cpu.State.A = 0x0001;
        cpu.State.X = 0x00FF;
        cpu.State.Y = 0x00FF;

        cpu.Step();

        Assert.Equal(0x5A, ram[0x7E00FF]);
        Assert.Equal(0x0000, cpu.State.X);
        Assert.Equal(0x0000, cpu.State.Y);
    }

    /// <summary>
    /// With <c>x = 0</c> both addresses are sixteen bits and wrap at the bank boundary rather
    /// than carrying into the next bank — Clark §5.1.2, quoted in §14.3: "source,destination
    /// addressing … wraps at both the source and destination bank boundaries", and measured
    /// there in <c>54 n 4275</c>, whose destination writes run <c>$C9FFFF</c> then
    /// <c>$C90000</c>.
    /// </summary>
    [Fact]
    public void AddressesWrapWithinTheirBank_WhenXIsClear()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x7FFFFF] = 0x3C;
        ram[0x7F0000] = 0x4D;

        cpu.State.XFlag = false;
        cpu.State.M = true;             // opposed
        cpu.State.A = 0x0001;           // two bytes, so the wrap is exercised
        cpu.State.X = 0xFFFF;
        cpu.State.Y = 0xFFFF;

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x3C, ram[0x7EFFFF]);
        Assert.Equal(0x4D, ram[0x7E0000]);      // wrapped within the destination bank
        Assert.Equal(0x0001, cpu.State.X);
        Assert.Equal(0x0001, cpu.State.Y);
    }

    /// <summary>
    /// A block move can be interrupted between iterations — Clark §6.6, quoted in §14.3: "MVN and
    /// MVP can be interrupted by IRQ and NMI before the move is complete (unlike every other
    /// instruction …); however, they can only be interrupted every seventh cycle." That falls out
    /// of the rewind for free: the instruction really ends every seventh cycle, so the ordinary
    /// instruction-boundary interrupt poll fires, and the pushed <c>PC</c> is the block move's own
    /// address — so <c>RTI</c> resumes the move where it left off. Nothing here is vector-covered:
    /// the SingleStepTests corpus carries no interrupt-line stimulus at all (§14.2's gap 1).
    /// </summary>
    [Fact]
    public void AnInterruptCanLandBetweenIterations()
    {
        var ram = new BankedBus();
        var cpu = Machine(ram, Mvn);
        ram[0x00FFEE] = 0x00;           // native IRQ vector -> $9000
        ram[0x00FFEF] = 0x90;

        cpu.State.A = 0x00FF;           // 256 bytes: far more than one iteration
        cpu.State.X = 0x0010;
        cpu.State.Y = 0x0020;
        cpu.State.I = false;
        cpu.State.S = 0x1FFF;

        cpu.Step();                     // one iteration, with IRQ low throughout
        Assert.Equal(0xC000, cpu.State.PC);

        cpu.SetIrq(true);
        cpu.Step();                     // another iteration; the poll on its last cycle sees IRQ
        Assert.Equal(0xC000, cpu.State.PC);

        cpu.Step();                     // the interrupt, not a third iteration
        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);

        // The pushed return address is the block move's own, so RTI resumes the move.
        Assert.Equal(0xC0, ram[0x001FFE]);
        Assert.Equal(0x00, ram[0x001FFD]);
    }
}
