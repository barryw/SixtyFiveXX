using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class CombinationOpTests
{
    [Fact]
    public void Slo_ShiftsMemoryLeftThenOrsIntoAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x07, 0x10);   // SLO $10
        ram[0x0010] = 0x81;
        cpu.State.A = 0x02;

        var cycles = cpu.Step();

        Assert.Equal(0x02, ram[0x0010]);   // $81 << 1 = $02, carry out
        Assert.Equal(0x02, cpu.State.A);   // $02 | $02
        Assert.True(cpu.State.C);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Rla_RotatesMemoryLeftThroughCarryThenAnds()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x27, 0x10);   // RLA $10
        ram[0x0010] = 0x80;
        cpu.State.A = 0xFF;
        cpu.State.C = true;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // ($80 << 1) | carry-in
        Assert.Equal(0x01, cpu.State.A);
        Assert.True(cpu.State.C);          // carry out from bit 7
    }

    [Fact]
    public void Sre_ShiftsMemoryRightThenEors()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x47, 0x10);   // SRE $10
        ram[0x0010] = 0x03;
        cpu.State.A = 0xFF;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // $03 >> 1
        Assert.Equal(0xFE, cpu.State.A);   // $FF ^ $01
        Assert.True(cpu.State.C);          // bit 0 was set
    }

    [Fact]
    public void Rra_RotatesMemoryRightThenAddsWithCarry()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x67, 0x10);   // RRA $10
        ram[0x0010] = 0x02;
        cpu.State.A = 0x10;
        cpu.State.C = false;

        cpu.Step();

        Assert.Equal(0x01, ram[0x0010]);   // $02 >> 1, no carry in
        Assert.Equal(0x11, cpu.State.A);   // $10 + $01 + 0
        Assert.False(cpu.State.C);
    }

    [Fact]
    public void Rra_HonoursDecimalModeInItsAddHalf()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x67, 0x10);   // RRA $10
        ram[0x0010] = 0x02;                                       // >> 1 = $01
        cpu.State.A = 0x09;
        cpu.State.C = false;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x10, cpu.State.A);   // BCD 9 + 1 = 10
    }

    [Theory]
    [InlineData(0x03, 8)]  // SLO (zp,X)
    [InlineData(0x07, 5)]  // SLO zp
    [InlineData(0x0F, 6)]  // SLO abs
    [InlineData(0x13, 8)]  // SLO (zp),Y  — always pays the fixup
    [InlineData(0x17, 6)]  // SLO zp,X
    [InlineData(0x1B, 7)]  // SLO abs,Y   — always pays the fixup
    [InlineData(0x1F, 7)]  // SLO abs,X   — always pays the fixup
    public void SloAddressingModes_TakeTheDocumentedCycles(byte opcode, int expected)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, opcode, 0x10, 0x30);
        ram[0x0010] = 0x00;
        ram[0x0011] = 0x30;

        Assert.Equal(expected, cpu.Step());
    }

    [Fact]
    public void Dcp_DecrementsMemoryThenComparesAgainstAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xC7, 0x10);   // DCP $10
        ram[0x0010] = 0x43;
        cpu.State.A = 0x42;

        cpu.Step();

        Assert.Equal(0x42, ram[0x0010]);
        Assert.True(cpu.State.Z);          // A == decremented memory
        Assert.True(cpu.State.C);
        Assert.Equal(0x42, cpu.State.A);   // A is never modified
    }

    [Fact]
    public void Isc_IncrementsMemoryThenSubtractsFromAccumulator()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xE7, 0x10);   // ISC $10
        ram[0x0010] = 0x04;
        cpu.State.A = 0x10;
        cpu.State.C = true;                                       // no borrow

        cpu.Step();

        Assert.Equal(0x05, ram[0x0010]);
        Assert.Equal(0x0B, cpu.State.A);   // $10 - $05
        Assert.True(cpu.State.C);
    }

    [Fact]
    public void Isc_HonoursDecimalModeInItsSubtractHalf()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xE7, 0x10);   // ISC $10
        ram[0x0010] = 0x04;                                       // +1 = $05
        cpu.State.A = 0x10;
        cpu.State.C = true;
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x05, cpu.State.A);   // BCD 10 - 5 = 5
    }
}
