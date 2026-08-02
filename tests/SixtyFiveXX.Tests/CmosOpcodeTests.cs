using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The CMOS-only addressing modes and operations, at the value and cycle-count level.
/// </summary>
/// <remarks>
/// <para>
/// Fast feedback for the table work that follows: the real gate is 2,560,000 Harte vectors
/// per sub-variant, which is not a debugging tool. Every expectation here was taken from
/// those same vectors rather than from a datasheet, so the two cannot disagree.
/// </para>
/// <para>
/// Read-modify-write instructions are asserted on their result and cycle count, not on the
/// direction of their dummy access. CMOS turns NMOS's dummy write into a dummy read without
/// changing the cycle count, and that delta lands with the base-CMOS work — pinning it here
/// would encode the NMOS behaviour and then have to be rewritten.
/// </para>
/// </remarks>
public class CmosOpcodeTests
{
    private static (Cpu<RefBus, Cmos65C02TestVariant> Cpu, byte[] Ram, List<BusAccess> Log) Machine(
        params byte[] program) => TestMachine.Logged<Cmos65C02TestVariant>(0x0200, program);

    // ---- JMP (abs): the page-wrap fix ----

    [Fact]
    public void JmpIndirect_TakesSixCyclesAndStillReadsTheBuggyAddress()
    {
        // Taken from wdc65c02 vector "6c ff 4b": JMP ($4BFF) reads $4BFF, then the NMOS
        // address $4B00, then the correct $4C00 — and keeps the last. The discarded middle
        // read is the part a datasheet reading of "the bug is fixed" would miss.
        var (cpu, ram, log) = Machine(0x6C, 0xFF, 0x4B);
        ram[0x4BFF] = 0xE2;
        ram[0x4B00] = 0x12;   // what NMOS would have taken as the high byte
        ram[0x4C00] = 0xFB;   // what CMOS takes

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal(0xFBE2, cpu.State.PC);
        Assert.Equal(
            [0x0200, 0x0201, 0x0202, 0x4BFF, 0x4B00, 0x4C00],
            log.Select(a => a.Address));
        Assert.DoesNotContain(log, a => a.IsWrite);
    }

    [Fact]
    public void JmpIndirect_ReadsTheSameAddressTwiceWhenThePointerDoesNotWrap()
    {
        // The same sequence with a non-$FF low byte. The buggy and correct addresses
        // coincide, which is why every vector shows six cycles rather than five.
        var (cpu, ram, log) = Machine(0x6C, 0x85, 0x01);
        ram[0x0185] = 0x9C;
        ram[0x0186] = 0xFA;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal(0xFA9C, cpu.State.PC);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0185, 0x0186, 0x0186], log.Select(a => a.Address));
    }

    // ---- JMP (abs,X) ----

    [Fact]
    public void JmpAbsoluteIndexedIndirect_DiscardsAReadAtTheFirstOperandByte()
    {
        // From vector "7c ce 06" with X=$65: base $06CE indexes to $0733 across a page
        // boundary, and the discarded read is still at the first operand byte.
        var (cpu, ram, log) = Machine(0x7C, 0xCE, 0x06);
        cpu.State.X = 0x65;
        ram[0x0733] = 0xA3;
        ram[0x0734] = 0x70;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal(0x70A3, cpu.State.PC);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0201, 0x0733, 0x0734], log.Select(a => a.Address));
    }

    [Fact]
    public void JmpAbsoluteIndexedIndirect_CostsTheSameWithoutAPageCross()
    {
        // No page-cross penalty: the mode is always six cycles.
        var (cpu, ram, log) = Machine(0x7C, 0x00, 0x30);
        cpu.State.X = 0x10;
        ram[0x3010] = 0x34;
        ram[0x3011] = 0x12;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal(0x1234, cpu.State.PC);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0201, 0x3010, 0x3011], log.Select(a => a.Address));
    }

    // ---- (zp) ----

    [Fact]
    public void ZeroPageIndirect_ReadsThePointerFromPageZeroAndTakesFiveCycles()
    {
        // From vector "72 48 08" with D clear: five cycles, pointer at $48/$49.
        var (cpu, ram, log) = Machine(0x72, 0x48);
        cpu.State.A = 0x10;
        cpu.State.D = false;
        ram[0x0048] = 0x6E;
        ram[0x0049] = 0xE6;
        ram[0xE66E] = 0x05;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x15, cpu.State.A);
        Assert.Equal([0x0200, 0x0201, 0x0048, 0x0049, 0xE66E], log.Select(a => a.Address));
    }

    [Fact]
    public void ZeroPageIndirect_WrapsThePointersHighByteWithinPageZero()
    {
        // A pointer at $FF takes its high byte from $00, not $0100 — the same wrap (zp,X)
        // already performs, which is why this mode reuses PtrReadHi.
        var (cpu, ram, log) = Machine(0xB2, 0xFF);
        ram[0x00FF] = 0x34;
        ram[0x0000] = 0x12;
        ram[0x1234] = 0x7E;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x7E, cpu.State.A);
        Assert.Equal([0x0200, 0x0201, 0x00FF, 0x0000, 0x1234], log.Select(a => a.Address));
    }

    [Fact]
    public void ZeroPageIndirect_Store_WritesThroughThePointer()
    {
        var (cpu, ram, _) = Machine(0x92, 0x40);
        cpu.State.A = 0x5A;
        ram[0x0040] = 0x00;
        ram[0x0041] = 0x70;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x5A, ram[0x7000]);
    }

    // ---- STZ ----

    [Theory]
    [InlineData(0x64, 3)]   // zp
    [InlineData(0x9C, 4)]   // abs
    public void Stz_WritesZeroWithoutTouchingTheAccumulator(byte opcode, int expectedCycles)
    {
        var (cpu, ram, _) = opcode == 0x64
            ? Machine(opcode, 0x40)
            : Machine(opcode, 0x00, 0x70);
        var target = opcode == 0x64 ? 0x0040 : 0x7000;
        ram[target] = 0xFF;
        cpu.State.A = 0xAA;

        var cycles = cpu.Step();

        Assert.Equal(expectedCycles, cycles);
        Assert.Equal(0x00, ram[target]);
        Assert.Equal(0xAA, cpu.State.A);
    }

    [Fact]
    public void Stz_AbsoluteX_AlwaysPaysTheFixupCycle()
    {
        // A write never gets the read's conditional page-cross saving.
        var (cpu, ram, _) = Machine(0x9E, 0x00, 0x70);
        cpu.State.X = 0x05;
        ram[0x7005] = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x00, ram[0x7005]);
    }

    // ---- TRB / TSB ----

    [Fact]
    public void Tsb_SetsTheAccumulatorsBitsInMemoryAndTakesZFromTheAnd()
    {
        var (cpu, ram, _) = Machine(0x04, 0x40);
        cpu.State.A = 0x0F;
        ram[0x0040] = 0xF0;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0xFF, ram[0x0040]);
        Assert.True(cpu.State.Z);          // A AND M was zero
        Assert.Equal(0x0F, cpu.State.A);   // A itself is untouched
    }

    [Fact]
    public void Trb_ClearsTheAccumulatorsBitsInMemoryAndTakesZFromTheAnd()
    {
        var (cpu, ram, _) = Machine(0x14, 0x40);
        cpu.State.A = 0x3C;
        ram[0x0040] = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0xC3, ram[0x0040]);
        Assert.False(cpu.State.Z);         // A AND M was non-zero
        Assert.Equal(0x3C, cpu.State.A);
    }

    [Fact]
    public void TrbAndTsb_LeaveNAndVAlone_UnlikeBit()
    {
        // BIT takes N and V from the operand's top two bits; TRB and TSB do not touch them.
        // Getting this wrong is invisible unless a test sets them beforehand.
        var (cpu, ram, _) = Machine(0x1C, 0x00, 0x70);
        cpu.State.A = 0x01;
        cpu.State.N = true;
        cpu.State.V = true;
        ram[0x7000] = 0xC0;   // both top bits set, which BIT would copy into N and V

        cpu.Step();

        Assert.True(cpu.State.N);
        Assert.True(cpu.State.V);
    }

    // ---- BRA ----

    [Fact]
    public void Bra_IsAlwaysTakenRegardlessOfFlags()
    {
        var (cpu, _, _) = Machine(0x80, 0x10);
        cpu.State.P = Flag.U;   // every condition flag clear

        var cycles = cpu.Step();

        Assert.Equal(0x0212, cpu.State.PC);
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Bra_PaysTheExtraCycleOnAPageCross()
    {
        var (cpu, _) = TestMachine.Flat<Cmos65C02TestVariant>(0x02F0, 0x80, 0x40);
        var cycles = cpu.Step();

        Assert.Equal(0x0332, cpu.State.PC);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void Bra_BranchesBackwards()
    {
        var (cpu, _) = TestMachine.Flat<Cmos65C02TestVariant>(0x0210, 0x80, 0xFC);
        cpu.Step();

        Assert.Equal(0x020E, cpu.State.PC);
    }

    // ---- PHX / PHY / PLX / PLY ----

    [Fact]
    public void PhxAndPlx_RoundTripThroughTheStack()
    {
        var (cpu, ram, _) = Machine(0xDA, 0xFA);
        cpu.State.X = 0x42;
        cpu.State.S = 0xFD;

        var pushCycles = cpu.Step();

        Assert.Equal(3, pushCycles);
        Assert.Equal(0x42, ram[0x01FD]);
        Assert.Equal(0xFC, cpu.State.S);

        cpu.State.X = 0x00;
        var pullCycles = cpu.Step();

        Assert.Equal(4, pullCycles);
        Assert.Equal(0x42, cpu.State.X);
        Assert.Equal(0xFD, cpu.State.S);
        Assert.False(cpu.State.Z);
    }

    [Fact]
    public void PhyAndPly_RoundTripThroughTheStack()
    {
        var (cpu, ram, _) = Machine(0x5A, 0x7A);
        cpu.State.Y = 0x00;
        cpu.State.S = 0xFD;

        cpu.Step();
        Assert.Equal(0x00, ram[0x01FD]);

        cpu.State.Y = 0xFF;
        cpu.Step();

        Assert.Equal(0x00, cpu.State.Y);
        Assert.True(cpu.State.Z);    // PLY sets Z and N, unlike PHY
    }

    // ---- INC A / DEC A ----

    [Fact]
    public void IncA_AndDecA_OperateOnTheAccumulatorInTwoCycles()
    {
        var (cpu, _, _) = Machine(0x1A, 0x3A, 0x3A);
        cpu.State.A = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x00, cpu.State.A);   // wraps
        Assert.True(cpu.State.Z);

        cpu.Step();
        Assert.Equal(0xFF, cpu.State.A);
        Assert.True(cpu.State.N);
    }

    // ---- BIT's new forms ----

    [Fact]
    public void BitImmediate_SetsOnlyZ()
    {
        // The one BIT form that does not copy the operand's top two bits into N and V.
        // Every other addressing mode does, which is what makes this worth pinning.
        var (cpu, _, _) = Machine(0x89, 0xC0);
        cpu.State.A = 0x01;
        cpu.State.N = false;
        cpu.State.V = false;

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.True(cpu.State.Z);
        Assert.False(cpu.State.N);
        Assert.False(cpu.State.V);
    }
}
