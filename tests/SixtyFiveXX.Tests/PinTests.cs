using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

public class PinTests
{
    /// <summary>
    /// Final-review finding 1: RDY halting a pending 65816 internal cycle (research document
    /// §9's <c>IO</c> rows — <c>MicroOps.IsInternalCycle</c>) used to fall through to the same
    /// <c>ReadBus</c> every other halted cycle uses, turning a cycle hardware performs no access
    /// on at all into a real bus read — while <see cref="Cpu{TBus,TVariant}.LastPins"/> kept
    /// reporting <c>BusPins.None</c> for it regardless, self-contradictory. Reachable only
    /// through the public <see cref="Cpu{TBus,TVariant}.SetRdy"/> API; no SingleStepTests vector
    /// drives RDY, so conformance could not see it. LDA sr,S ($A3) is used because its second
    /// cycle, <c>StackRelativePenalty</c>, is an unconditional internal cycle (research document
    /// §9, "Stack Relative — row 23") — no DL-dependent skip to route around, unlike the
    /// direct-page forms. <see cref="TestMachine.Logged{TVariant}"/>'s <c>LoggingBus</c> does
    /// not override <c>IBus.Internal</c>, so it only grows the log on an actual
    /// <c>Read</c>/<c>Write</c> call — which is exactly what a real memory access would be and
    /// an internal cycle must not be. Verified to fail against the unfixed code: the log grows
    /// by one and <c>ram[$0000]</c> ends up read, even though the halted cycle's own micro-op is
    /// classified <c>BusPins.None</c>.
    /// </summary>
    [Fact]
    public void Rdy_HaltedInternalCycle_PerformsNoRealBusAccess()
    {
        var (cpu, _, log) = TestMachine.Logged<W65C816Variant>(0xC000, 0xA3, 0x00);   // LDA sr,S ; SO=$00
        cpu.State.E = false;
        cpu.State.S = 0x01FF;

        cpu.Tick();   // opcode fetch
        cpu.Tick();   // FetchSrOffset: reads the SO operand at PC
        var beforeHalt = log.Count;
        var cyclesBeforeHalt = cpu.Cycles;

        cpu.SetRdy(false);
        cpu.Tick();   // StackRelativePenalty pending: an internal cycle, not a read

        Assert.Equal(beforeHalt, log.Count);            // no bus access happened
        Assert.Equal(cyclesBeforeHalt + 1, cpu.Cycles);  // the clock still advanced
        Assert.Equal(BusPins.None, cpu.LastPins);
    }

    [Fact]
    public void Rdy_HaltsTheProcessorOnAReadCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);   // LDA $1234
        ram[0x1234] = 0x42;

        cpu.Tick();                 // opcode fetch
        cpu.SetRdy(false);

        var pcBefore = cpu.State.PC;
        for (var i = 0; i < 10; i++) cpu.Tick();

        Assert.Equal(pcBefore, cpu.State.PC);      // no progress while halted
        Assert.False(cpu.AtInstructionBoundary);   // still mid-instruction, not fast-forwarded
        Assert.False(cpu.State.A == 0x42);

        cpu.SetRdy(true);
        cpu.Step();

        Assert.Equal(0x42, cpu.State.A);           // resumes and completes
    }

    [Fact]
    public void Rdy_StillDrivesTheAddressBusWhileHalted()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0xAD, 0x34, 0x12);

        cpu.Tick();
        var afterFetch = log.Count;
        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(afterFetch + 2, log.Count);   // one access per halted cycle
        Assert.All(log, a => Assert.False(a.IsWrite));
    }

    [Fact]
    public void Rdy_DoesNotHaltAWriteCycle()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x8D, 0x00, 0x30);   // STA $3000
        cpu.State.A = 0x5A;

        cpu.Tick();   // opcode
        cpu.Tick();   // address low
        cpu.Tick();   // address high
        cpu.SetRdy(false);
        cpu.Tick();   // the write must complete despite RDY being low

        Assert.Equal(0x5A, ram[0x3000]);
    }

    [Fact]
    public void Rdy_DoesNotHaltAPushWriteCycle()
    {
        // ExecWrite is covered above; the push family (PHA/PHP/PushPch/PushPcl/PushPBrk/
        // PushPInt) reaches the bus through a different micro-op, MicroOp.Push. A future
        // write micro-op could be added to the RDY write table without exercising ExecWrite
        // at all, so this pins the push family separately.
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x48);   // PHA
        cpu.State.A = 0x5A;
        cpu.State.S = 0xFD;

        cpu.Tick();   // opcode
        cpu.Tick();   // ImpliedDummy
        cpu.SetRdy(false);
        cpu.Tick();   // the push must complete despite RDY being low

        Assert.Equal(0x5A, ram[0x01FD]);
    }

    [Fact]
    public void Rdy_CountsHaltedCyclesAgainstTheCycleCounter()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.Equal(2, cpu.Cycles);
    }

    [Fact]
    public void Ready_ReportsTheCurrentPinState()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        Assert.True(cpu.Ready);
        cpu.SetRdy(false);
        Assert.False(cpu.Ready);
    }

    [Fact]
    public void So_SetsTheOverflowFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.V = false;

        cpu.SetSo();

        Assert.True(cpu.State.V);
    }

    [Fact]
    public void So_DoesNotDisturbAnyOtherFlag()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);
        cpu.State.P = Flag.U | Flag.C | Flag.N;

        cpu.SetSo();

        Assert.Equal(Flag.U | Flag.C | Flag.N | Flag.V, cpu.State.P);
    }
}
