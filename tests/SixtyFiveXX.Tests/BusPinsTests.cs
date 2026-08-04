using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Covers <see cref="BusPins"/>, <see cref="MicroOps.PinsFor"/> and the <c>Cpu</c> readback
/// that records them each cycle. The conformance harness (phase 7b, task 3) is the real
/// consumer; these tests exist so a wrong or missing classification fails here, in a two-
/// second unit run, rather than deep in a 65816 vector run.
/// </summary>
public class BusPinsTests
{
    /// <summary>
    /// A table with a silent default is the failure mode this guards against —
    /// <c>MicroOpTable.SequencesFor</c> once defaulted an unmapped variant to NMOS. Every
    /// micro-op must either assert a pin or be explicitly recognised as internal; nothing may
    /// fall through to <see cref="BusPins.None"/> by accident.
    /// </summary>
    [Fact]
    public void EveryMicroOpHasAPinClassification()
    {
        foreach (var op in Enum.GetValues<MicroOp>())
        {
            if (op == MicroOp.End) continue;
            var pins = MicroOps.PinsFor(op);
            Assert.True(pins != BusPins.None || MicroOps.IsInternalCycle(op),
                $"{op} has no pin classification and is not an internal cycle.");
        }
    }

    [Fact]
    public void OpcodeFetch_AssertsVdaAndVpa()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);   // NOP

        cpu.Tick();

        Assert.Equal(BusPins.Vda | BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x0200, cpu.LastAddress);
    }

    [Fact]
    public void OperandFetchAtPc_AssertsVpaOnly()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xA9, 0x42);   // LDA #$42

        cpu.Tick();   // opcode fetch
        cpu.Tick();   // ImmExec: read the operand at PC

        Assert.Equal(BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x0201, cpu.LastAddress);
    }

    [Fact]
    public void EffectiveAddressRead_AssertsVdaOnly()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);   // LDA $1234
        ram[0x1234] = 0x99;

        cpu.Tick();   // opcode fetch
        cpu.Tick();   // FetchAddrLo
        cpu.Tick();   // FetchAddrHi
        cpu.Tick();   // ReadExec at $1234

        Assert.Equal(BusPins.Vda, cpu.LastPins);
        Assert.Equal(0x1234, cpu.LastAddress);
    }

    [Fact]
    public void RdyHalt_StillRecordsPinsAndAddressEachCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);   // LDA $1234
        ram[0x1234] = 0x42;

        cpu.Tick();   // opcode fetch
        cpu.Tick();   // FetchAddrLo
        cpu.Tick();   // FetchAddrHi -> _addr = $1234
        cpu.SetRdy(false);
        cpu.Tick();   // halted, re-drives $1234 (the pending ReadExec's address)

        Assert.Equal(BusPins.Vda, cpu.LastPins);
        Assert.Equal(0x1234, cpu.LastAddress);
    }
}
