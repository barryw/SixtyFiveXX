using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// A CMOS variant that exists only to drive the shared 65C02 interrupt sequences before any
/// public 65C02 variant struct does. It reports a real <see cref="CpuVariant"/>, so it
/// resolves the same micro-op table a released core will.
/// </summary>
internal readonly struct Cmos65C02TestVariant : ICpuVariant
{
    public static CpuVariant Variant => CpuVariant.Synertek65C02;
}

/// <summary>
/// The two CMOS interrupt deltas: <c>D</c> is cleared on entry, and there is no NMI hijack.
/// </summary>
/// <remarks>
/// These run against a 65C02 opcode table that is still being built, which is deliberate —
/// a hardware interrupt never fetches an opcode (see <c>Cpu.FetchOpcode</c>, which dispatches
/// instead of fetching), so the IRQ sequence is fully exercisable with almost nothing defined.
/// The BRK cases need only <c>$00</c> itself. The NMOS equivalents live in
/// <c>InterruptTests</c> and <c>HijackTests</c> and must keep passing unchanged: both
/// behaviours are correct, for different variants.
/// </remarks>
public class CmosInterruptTests
{
    private static (Cpu<FlatBus, Cmos65C02TestVariant> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat<Cmos65C02TestVariant>(0x0200, program);
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;   // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;   // NMI     -> $8000
        cpu.State.P = Flag.U;                      // I clear, interrupts enabled
        cpu.State.S = 0xFD;
        return (cpu, ram);
    }

    [Fact]
    public void TheIrqSequenceUsesTheCmosPushOnACmosVariant()
    {
        // Reads the emitted sequence directly, so a failure points at the table builder
        // rather than at whichever behavioural test happened to notice. Index 3 is the P
        // push: IntDummy, PushPch, PushPcl, then this.
        var cmos = MicroOpTable.For<Cmos65C02TestVariant>();
        var nmos = MicroOpTable.For<Variants.Mos6502Variant>();

        Assert.Equal(MicroOp.PushPIntCmos, cmos.Ops[cmos.IrqEntry + 3]);
        Assert.Equal(MicroOp.PushPInt, nmos.Ops[nmos.IrqEntry + 3]);
    }

    [Fact]
    public void TheBrkSequenceUsesTheCmosPushOnACmosVariant()
    {
        var cmos = MicroOpTable.For<Cmos65C02TestVariant>();
        var nmos = MicroOpTable.For<Variants.Mos6502Variant>();

        // BrkPad, PushPch, PushPcl, then the P push.
        Assert.Equal(MicroOp.PushPBrkCmos, cmos.Ops[cmos.Entry[0x00] + 3]);
        Assert.Equal(MicroOp.PushPBrk, nmos.Ops[nmos.Entry[0x00] + 3]);
    }

    [Fact]
    public void Irq_ClearsDecimalMode()
    {
        // The exact counterpart of InterruptTests.Irq_DoesNotClearDecimalModeOnNmos. Both
        // are correct; the variant is the only difference.
        var (cpu, _) = Machine(0xEA);
        cpu.State.D = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.False(cpu.State.D);
    }

    [Fact]
    public void Irq_PushesStatusWithDecimalStillSet()
    {
        // The push happens before D is cleared, so the stacked copy keeps D and RTI restores
        // it. Clearing first would corrupt the caller's D on every CMOS interrupt, and the
        // test above could not tell the difference.
        var (cpu, ram) = Machine(0xEA);
        cpu.State.D = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(Flag.D, ram[0x01FB] & Flag.D);
        Assert.False(cpu.State.D);
    }

    [Fact]
    public void Brk_ClearsDecimalMode()
    {
        var (cpu, ram) = Machine(0x00);
        cpu.State.D = true;

        cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.False(cpu.State.D);
        Assert.Equal(Flag.D, ram[0x01FB] & Flag.D);   // still set in the pushed copy
    }

    [Fact]
    public void Brk_IsNotHijackedByAPendingNmi()
    {
        // Same edge HijackTests.Nmi_HijacksABrkInProgress uses on NMOS — latch after four
        // ticks, immediately before the P push that commits the vector. NMOS lands at
        // $8000; CMOS removed the anomaly, so this must land at BRK's own $9000.
        var (cpu, ram) = Machine(0x00);

        cpu.Tick();   // 1: opcode fetch
        cpu.Tick();   // 2: BrkPad
        cpu.Tick();   // 3: push PCH
        cpu.Tick();   // 4: push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // 5: push P — where NMOS would redirect
        cpu.Tick();   // 6: vector low
        cpu.Tick();   // 7: vector high

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void ANmiThatDidNotHijackIsStillPendingAndFiresAfterOneInstruction()
    {
        // The latch must survive rather than be silently dropped: not hijacking is not the
        // same as discarding. One handler instruction runs first, because an interrupt
        // sequence does not poll on its own final cycle.
        var (cpu, ram) = Machine(0x00);
        ram[0x9000] = 0xEA;   // the BRK handler's first instruction

        cpu.Tick(); cpu.Tick(); cpu.Tick(); cpu.Tick();
        cpu.SetNmi(true);
        cpu.Tick(); cpu.Tick(); cpu.Tick();

        Assert.Equal(0x9000, cpu.State.PC);   // BRK's vector stood

        cpu.Step();
        Assert.Equal(0x9001, cpu.State.PC);   // the handler instruction runs

        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);   // only now the NMI it never consumed
    }

    [Fact]
    public void Irq_IsNotHijackedByAPendingNmi()
    {
        // The hardware-interrupt path, not BRK's. Mirrors
        // HijackTests.Nmi_HijacksAGenuineIrqInProgress, which asserts the NMOS opposite.
        var (cpu, ram) = Machine(0xEA);
        cpu.SetIrq(true);

        cpu.Tick();   // NOP opcode fetch
        cpu.Tick();   // NOP ImpliedExec; poll sees the asserted IRQ
        cpu.Tick();   // dispatch's dummy PC read; enters IrqEntry, vector = IrqVector
        cpu.Tick();   // IntDummy
        cpu.Tick();   // push PCH
        cpu.Tick();   // push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // push P
        cpu.Tick();   // vector low
        cpu.Tick();   // vector high

        Assert.Equal(0x9000, cpu.State.PC);       // IRQ's own vector, not NMI's $8000
        Assert.Equal(0, ram[0x01FB] & Flag.B);    // and B is clear, as for any hardware IRQ
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void Irq_StillTakesSevenCyclesAndPushesThreeBytes()
    {
        // The CMOS deltas are confined to the P push. Sequence length, stack depth and the
        // pushed return address must be identical to NMOS — see
        // InterruptTests.Irq_IsTakenAtTheNextInstructionBoundary, which asserts the same
        // numbers for the 6502.
        var (cpu, ram) = Machine(0xEA, 0xEA);
        cpu.SetIrq(true);

        cpu.Step();
        Assert.Equal(0x0201, cpu.State.PC);

        var cycles = cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
        Assert.True(cpu.State.I);
        Assert.Equal(0xFA, cpu.State.S);
        Assert.Equal(0x02, ram[0x01FD]);
        Assert.Equal(0x01, ram[0x01FC]);
    }
}
