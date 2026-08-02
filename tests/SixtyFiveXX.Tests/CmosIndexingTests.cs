using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The CMOS indexing and read-modify-write deltas.
/// </summary>
/// <remarks>
/// <para>
/// None of these follows from the design spec's summary of the CMOS changes, and all were
/// measured from the <c>synertek65c02</c> vectors across hundreds of cases each:
/// </para>
/// <list type="bullet">
/// <item>The page-cross dummy read moves to the last operand byte, where NMOS reads the
/// mis-indexed address. Reads, writes and read-modify-writes alike.</item>
/// <item>Writes still always pay the fixup cycle, so only the dummy's address changes.</item>
/// <item>Shift and rotate <c>abs,X</c> became six cycles without a page cross and seven
/// with — but <c>INC</c>/<c>DEC abs,X</c> stayed at seven regardless.</item>
/// <item>The read-modify-write middle cycle is a dummy read where NMOS writes back.</item>
/// </list>
/// <para>
/// Each test pins the bus addresses, not just the cycle count. A model that got the count
/// right and the addresses wrong would pass a count-only test and then fail thousands of
/// vectors — which is exactly the trap the <c>JMP (abs)</c> work ran into.
/// </para>
/// </remarks>
public class CmosIndexingTests
{
    private static (Cpu<RefBus, Cmos65C02TestVariant> Cpu, byte[] Ram, List<BusAccess> Log) Machine(
        params byte[] program) => TestMachine.Logged<Cmos65C02TestVariant>(0x0200, program);

    // ---- Indexed reads ----

    [Fact]
    public void ReadAbsoluteX_NoPageCross_IsFourCyclesWithNoDummy()
    {
        var (cpu, ram, log) = Machine(0x1D, 0x00, 0x30);   // ORA $3000,X
        cpu.State.X = 0x10;
        ram[0x3010] = 0x0F;

        var cycles = cpu.Step();

        Assert.Equal(4, cycles);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x3010], log.Select(a => a.Address));
    }

    [Fact]
    public void ReadAbsoluteX_PageCross_DiscardsAReadAtTheLastOperandByte()
    {
        // NMOS would read the mis-indexed $30F0 here. CMOS reads the operand byte at $0202.
        var (cpu, ram, log) = Machine(0x1D, 0xF0, 0x30);   // ORA $30F0,X
        cpu.State.X = 0x20;
        ram[0x3110] = 0x0F;
        ram[0x30F0] = 0xAA;   // the NMOS dummy target, which must NOT be read

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0202, 0x3110], log.Select(a => a.Address));
        Assert.Equal(0x0F, cpu.State.A);
    }

    [Fact]
    public void ReadIndirectIndexed_PageCross_DiscardsAReadAtTheZeroPageOperand()
    {
        // The same rule one byte earlier: (zp),Y has a single operand byte, at $0201.
        var (cpu, ram, log) = Machine(0xB1, 0x40);         // LDA ($40),Y
        cpu.State.Y = 0x20;
        ram[0x0040] = 0xF0;
        ram[0x0041] = 0x30;
        ram[0x3110] = 0x7E;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal([0x0200, 0x0201, 0x0040, 0x0041, 0x0201, 0x3110], log.Select(a => a.Address));
        Assert.Equal(0x7E, cpu.State.A);
    }

    // ---- Indexed writes ----

    [Fact]
    public void WriteAbsoluteX_AlwaysPaysTheFixup_ButReadsTheOperandByte()
    {
        // Five cycles with or without a cross, as on NMOS. Only the dummy's address moved.
        var (cpu, _, log) = Machine(0x9D, 0x00, 0x30);     // STA $3000,X
        cpu.State.X = 0x10;
        cpu.State.A = 0x5A;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0202, 0x3010], log.Select(a => a.Address));
        Assert.True(log[^1].IsWrite);
    }

    // ---- Read-modify-write ----

    [Fact]
    public void Rmw_MiddleCycleIsAReadNotAWrite()
    {
        // NMOS writes the unmodified value back on this cycle. A bus with write side
        // effects sees a completely different transaction, so the direction matters beyond
        // the cycle count — which is identical either way.
        var (cpu, ram, log) = Machine(0x06, 0x40);         // ASL $40
        ram[0x0040] = 0x21;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x42, ram[0x0040]);
        Assert.Equal([0x0200, 0x0201, 0x0040, 0x0040, 0x0040], log.Select(a => a.Address));
        Assert.Equal([false, false, false, false, true], log.Select(a => a.IsWrite));
    }

    [Fact]
    public void ShiftAbsoluteX_NoPageCross_IsSixCycles()
    {
        // NMOS always pays seven here. CMOS made the fixup conditional for the shifts.
        var (cpu, ram, log) = Machine(0x1E, 0x00, 0x30);   // ASL $3000,X
        cpu.State.X = 0x10;
        ram[0x3010] = 0x21;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal(0x42, ram[0x3010]);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x3010, 0x3010, 0x3010], log.Select(a => a.Address));
    }

    [Fact]
    public void ShiftAbsoluteX_PageCross_IsSevenCycles()
    {
        var (cpu, ram, log) = Machine(0x1E, 0xF0, 0x30);   // ASL $30F0,X
        cpu.State.X = 0x20;
        ram[0x3110] = 0x21;

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x42, ram[0x3110]);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0202, 0x3110, 0x3110, 0x3110], log.Select(a => a.Address));
    }

    [Fact]
    public void IncAbsoluteX_IsSevenCyclesEvenWithoutAPageCross()
    {
        // The split that no prose description of the CMOS change predicts: INC and DEC
        // abs,X kept NMOS's unconditional fixup while the shifts did not.
        var (cpu, ram, log) = Machine(0xFE, 0x00, 0x30);   // INC $3000,X
        cpu.State.X = 0x10;
        ram[0x3010] = 0x41;

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x42, ram[0x3010]);
        Assert.Equal([0x0200, 0x0201, 0x0202, 0x0202, 0x3010, 0x3010, 0x3010], log.Select(a => a.Address));
    }

    [Fact]
    public void IncAbsoluteX_IsStillSevenCyclesWithAPageCross()
    {
        var (cpu, ram, _) = Machine(0xFE, 0xF0, 0x30);     // INC $30F0,X
        cpu.State.X = 0x20;
        ram[0x3110] = 0x41;

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x42, ram[0x3110]);
    }
}
