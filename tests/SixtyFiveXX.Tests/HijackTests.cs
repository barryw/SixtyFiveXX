using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class HijackTests
{
    private static (Cpu<FlatBus> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, program);
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;   // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;   // NMI     -> $8000
        cpu.State.P = Flag.U;
        cpu.State.S = 0xFD;
        return (cpu, ram);
    }

    [Fact]
    public void Brk_VectorsThroughFffeWhenNoNmiArrives()
    {
        var (cpu, _) = Machine(0x00);   // BRK

        var cycles = cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_HijacksABrkInProgress()
    {
        var (cpu, ram) = Machine(0x00);   // BRK

        // Drive BRK's first four cycles, then assert NMI before the vector read.
        cpu.Tick();   // opcode fetch
        cpu.Tick();   // BrkPad
        cpu.Tick();   // push PCH
        cpu.Tick();   // push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // push P
        cpu.Tick();   // vector low
        cpu.Tick();   // vector high

        Assert.Equal(0x8000, cpu.State.PC);          // NMI's vector, not BRK's
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);  // but BRK's pushed B is still set
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void HijackingConsumesTheNmiLatch()
    {
        var (cpu, _) = Machine(0x00, 0xEA, 0xEA);

        cpu.Tick(); cpu.Tick(); cpu.Tick(); cpu.Tick();
        cpu.SetNmi(true);
        cpu.Tick(); cpu.Tick(); cpu.Tick();
        Assert.Equal(0x8000, cpu.State.PC);

        // The latch was consumed, so the next boundary must not dispatch again.
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void NmiArrivingAfterTheVectorReadDoesNotHijack()
    {
        var (cpu, _) = Machine(0x00, 0xEA);

        for (var i = 0; i < 6; i++) cpu.Tick();   // through the vector-low read
        cpu.SetNmi(true);
        cpu.Tick();                                // vector high

        Assert.Equal(0x9000, cpu.State.PC);        // BRK's own vector stood
    }

    [Fact]
    public void NmiArrivingDuringItsOwnSequenceSurvivesToFireAgain()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA, 0xEA);
        cpu.SetNmi(true);

        // Drive a genuine NMI dispatch (not a hijack) up to its own PushPInt, then latch a
        // second edge before this sequence's own VectorLo runs.
        cpu.Tick();   // NOP opcode fetch
        cpu.Tick();   // NOP ImpliedExec; poll sees the pending NMI
        cpu.Tick();   // interrupt dispatch's dummy PC read; enters IrqEntry, vector = NmiVector
        cpu.Tick();   // IntDummy
        cpu.Tick();   // PushPch
        cpu.Tick();   // PushPcl
        cpu.Tick();   // PushPInt
        cpu.SetNmi(false);
        cpu.SetNmi(true);   // a fresh edge, latched while this NMI's own sequence is still running

        cpu.Step();   // let the sequence finish: VectorLo must not "hijack" itself and must
                       // not consume the new latch, since _vector is already NmiVector

        Assert.Equal(0x8000, cpu.State.PC);   // the first NMI dispatched normally
        Assert.True(cpu.AtInstructionBoundary);

        // The re-latched edge must survive to fire again. Finishing the first sequence above
        // is one Step() call; the poll it leaves behind is already hot (VectorHi's own poll
        // saw the still-pending latch), so the second dispatch fires on the very next fetch —
        // a second, distinct Step() call, not folded into the first.
        cpu.State.PC = 0x0201;
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);   // second NMI dispatch, not the NOP at $0201
    }

    [Fact]
    public void Reset_IsNotDivertedByAPendingNmi()
    {
        var (cpu, ram) = Machine(0xEA);
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x70;   // RESET -> $7000, distinct from IRQ/BRK and NMI
        cpu.SetNmi(true);                          // latch an NMI before reset

        cpu.Reset();
        var cycles = cpu.Step();

        // RESET outranks NMI on real hardware: PC must load from $FFFC, not get hijacked
        // to $FFFA. Whether the latch itself survives a reset is a separate question this
        // project has not settled, so that is deliberately not asserted here.
        Assert.Equal(0x7000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_HijacksAGenuineIrqInProgress()
    {
        var (cpu, ram) = Machine(0xEA);   // NOP; IRQ fires at the next boundary
        cpu.SetIrq(true);

        // Drive a genuine hardware IRQ dispatch (PushPInt, not BRK's PushPBrk) up to its
        // PushPcl, then assert NMI before the vector read.
        cpu.Tick();   // NOP opcode fetch
        cpu.Tick();   // NOP ImpliedExec; poll sees the asserted IRQ
        cpu.Tick();   // interrupt dispatch's dummy PC read; enters IrqEntry, vector = IrqVector
        cpu.Tick();   // IntDummy
        cpu.Tick();   // push PCH
        cpu.Tick();   // push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // push P
        cpu.Tick();   // vector low
        cpu.Tick();   // vector high

        Assert.Equal(0x8000, cpu.State.PC);       // NMI's vector, not IRQ's $9000
        Assert.Equal(0, ram[0x01FB] & Flag.B);    // IRQ's own pushed B is clear, not BRK's
        Assert.True(cpu.AtInstructionBoundary);
    }
}
