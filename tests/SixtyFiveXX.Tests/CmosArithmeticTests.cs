using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// CMOS <c>ADC</c> and <c>SBC</c>: the extra decimal-mode cycle, and the flag and
/// correction rules that differ from NMOS.
/// </summary>
/// <remarks>
/// <para>
/// Every case here was taken from a <c>synertek65c02</c> vector. The rules were derived by
/// fitting candidate models against every decimal vector of <c>$69</c>, <c>$72</c>,
/// <c>$E9</c> and <c>$F2</c> — roughly 20,000 in total — rather than from a datasheet, and
/// two of them contradict the usual summary of the change:
/// </para>
/// <list type="bullet">
/// <item><c>V</c> is <em>not</em> corrected. It comes from the partially corrected high
/// nibble exactly as on NMOS, so "correct N/V/Z" overstates the change. Only N and Z move
/// to the final decimal result.</item>
/// <item><c>SBC</c>'s decimal <em>correction</em> differs too, not just its flags: CMOS
/// adjusts the binary difference by $60 and $06 rather than nibble-wise, which is what makes
/// invalid BCD inputs land on a different value.</item>
/// </list>
/// </remarks>
public class CmosArithmeticTests
{
    private static (Cpu<RefBus, Cmos65C02TestVariant> Cpu, byte[] Ram, List<BusAccess> Log) Machine(
        params byte[] program) => TestMachine.Logged<Cmos65C02TestVariant>(0x0200, program);

    [Fact]
    public void Adc_DecimalCostsAnExtraCycleThatRepeatsTheOperandRead()
    {
        // From "72 59 22": ADC ($59) with D set is six cycles, the last repeating the
        // effective-address read. With D clear the same opcode is five.
        var (cpu, ram, log) = Machine(0x72, 0x59);
        cpu.State.D = true;
        cpu.State.A = 0x12;
        ram[0x0059] = 0x42;
        ram[0x005A] = 0x85;
        ram[0x8542] = 0x34;

        var cycles = cpu.Step();

        Assert.Equal(6, cycles);
        Assert.Equal([0x0200, 0x0201, 0x0059, 0x005A, 0x8542, 0x8542], log.Select(a => a.Address));
        Assert.Equal(0x46, cpu.State.A);   // 12 + 34 in BCD
    }

    [Fact]
    public void Adc_BinaryModeIsUnchangedAndCostsNoExtraCycle()
    {
        var (cpu, ram, log) = Machine(0x72, 0x48);
        cpu.State.D = false;
        cpu.State.A = 0x10;
        ram[0x0048] = 0x6E;
        ram[0x0049] = 0xE6;
        ram[0xE66E] = 0x05;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x15, cpu.State.A);
        Assert.Equal([0x0200, 0x0201, 0x0048, 0x0049, 0xE66E], log.Select(a => a.Address));
    }

    [Fact]
    public void Adc_DecimalTakesNAndZFromTheCorrectedResult()
    {
        // NMOS takes Z from the binary sum and N from the half-corrected high nibble, so a
        // result that is zero only after correction is where the two families diverge.
        var (cpu, ram, _) = Machine(0x72, 0x40);
        cpu.State.D = true;
        cpu.State.A = 0x99;
        ram[0x0040] = 0x00;
        ram[0x0041] = 0x70;
        ram[0x7000] = 0x01;

        cpu.Step();

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cpu.State.Z);      // NMOS would leave Z clear here
        Assert.False(cpu.State.N);
        Assert.True(cpu.State.C);
    }

    [Fact]
    public void Sbc_DecimalCorrectionDiffersFromNmosOnInvalidBcd()
    {
        // From "e9 af" with A=$53 and carry set. The NMOS nibble-wise correction yields
        // $4E; the CMOS $60/$06 adjustment yields $3E, which is what the vectors record.
        var (cpu, _, _) = Machine(0xE9, 0xAF);
        cpu.State.D = true;
        cpu.State.C = true;
        cpu.State.A = 0x53;

        var cycles = cpu.Step();

        Assert.Equal(3, cycles);        // two cycles plus the decimal extra
        Assert.Equal(0x3E, cpu.State.A);
    }

    [Fact]
    public void Sbc_DecimalKeepsCarryAndOverflowBinary()
    {
        var (cpu, _, _) = Machine(0xE9, 0x01);
        cpu.State.D = true;
        cpu.State.C = true;
        cpu.State.A = 0x00;

        cpu.Step();

        Assert.Equal(0x99, cpu.State.A);
        Assert.False(cpu.State.C);      // borrowed
        Assert.False(cpu.State.V);
    }

}
