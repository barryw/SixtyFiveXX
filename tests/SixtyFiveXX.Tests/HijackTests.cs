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

        // The late edge of the window. Silicon commits the vector at T5 phase 1 — the edge
        // that begins cycle 5, the P push, because cycle 5 forms the vector-low address that
        // appears on the pins in cycle 6. Latching here, after four ticks, is the last edge
        // that still redirects the vector; NmiArrivingAfterThePushOfPDoesNotHijack pins the
        // other side. Narrowing the window by one tick breaks this test.
        cpu.Tick();   // 1: opcode fetch
        cpu.Tick();   // 2: BrkPad
        cpu.Tick();   // 3: push PCH
        cpu.Tick();   // 4: push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // 5: push P — the vector is committed here
        cpu.Tick();   // 6: vector low
        cpu.Tick();   // 7: vector high

        Assert.Equal(0x8000, cpu.State.PC);          // NMI's vector, not BRK's
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);  // but BRK's pushed B is still set
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void NmiArrivingAfterThePushOfPDoesNotHijack()
    {
        var (cpu, ram) = Machine(0x00);   // BRK

        // The early edge of the window: one tick later than Nmi_HijacksABrkInProgress. The
        // P push has already run, so the vector-low address is formed and the ~VEC chain
        // holds NMI recognition off until the handler's first fetch. Widening the window by
        // one tick — deciding at the vector read instead of the P push — breaks this test.
        for (var i = 0; i < 5; i++) cpu.Tick();   // through the P push
        cpu.SetNmi(true);
        cpu.Tick();   // 6: vector low
        cpu.Tick();   // 7: vector high

        Assert.Equal(0x9000, cpu.State.PC);          // BRK's own vector stood
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);  // and its pushed B is untouched
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void ABlockedNmiStillRunsOneHandlerInstructionBeforeFiring()
    {
        var (cpu, ram) = Machine(0x00);   // BRK
        ram[0x9000] = 0xEA;               // the BRK handler's first instruction

        for (var i = 0; i < 5; i++) cpu.Tick();   // through the P push
        cpu.SetNmi(true);
        cpu.Tick(); cpu.Tick();
        Assert.Equal(0x9000, cpu.State.PC);

        // The NMI is not lost, only blocked. Hardware keeps node 1368 grounded from T5
        // phase 2 through T0 phase 1, so the sequence's own final cycle cannot recognise
        // it; the earliest recognition is T1 phase 1 of the handler's first instruction.
        // That instruction therefore always runs.
        cpu.Step();
        Assert.Equal(0x9001, cpu.State.PC);

        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);   // only now the NMI
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
    public void NmiArrivingAfterVectorLowStillDoesNotHijack()
    {
        var (cpu, _) = Machine(0x00, 0xEA);

        // One tick further out than the actual cutoff: NmiArrivingAfterThePushOfPDoesNotHijack
        // already pins the discriminating edge (the P push, cycle 5). This just confirms the
        // far side stays put once VectorLo has run too.
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
        // second edge after that cycle has already committed this sequence's vector.
        cpu.Tick();   // NOP opcode fetch
        cpu.Tick();   // NOP ImpliedExec; poll sees the pending NMI
        cpu.Tick();   // interrupt dispatch's dummy PC read; enters IrqEntry, vector = NmiVector
        cpu.Tick();   // IntDummy
        cpu.Tick();   // PushPch
        cpu.Tick();   // PushPcl
        cpu.Tick();   // PushPInt — this sequence's vector commit; it must not hijack itself
        cpu.SetNmi(false);
        cpu.SetNmi(true);   // a fresh edge, latched after this NMI's own vector was committed

        cpu.Step();   // let the sequence finish

        Assert.Equal(0x8000, cpu.State.PC);   // the first NMI dispatched normally
        Assert.True(cpu.AtInstructionBoundary);

        // The re-latched edge must survive to fire again — but not immediately. An interrupt
        // sequence does not poll on its own final cycle, so exactly one handler instruction
        // runs first. Here that is the NOP at $0201, and only the Step() after it dispatches.
        cpu.State.PC = 0x0201;

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);   // the handler instruction, not a second dispatch

        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);   // second NMI dispatch
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
        // to $FFFA. Reset_ClearsAPendingNmi covers the latch's own fate.
        Assert.Equal(0x7000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Reset_ClearsAPendingNmi()
    {
        var (cpu, ram) = Machine(0xEA);
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x70;   // RESET -> $7000
        ram[0x7000] = 0xEA; ram[0x7001] = 0xEA;   // two instructions at the reset vector
        cpu.SetNmi(true);                          // latch an NMI before the reset

        cpu.Reset();
        cpu.Step();                                // the seven-cycle reset sequence
        Assert.Equal(0x7000, cpu.State.PC);

        // A reset runs a BRK on this die, and that BRK clears NMI stage 1 (~NMIG) at T0
        // phase 1 unconditionally, so the pre-reset latch is gone. Two instructions must
        // therefore run untouched: one would prove nothing, because the sequence's own
        // final-cycle poll blackout defers even a surviving latch by exactly one.
        cpu.Step();
        Assert.Equal(0x7001, cpu.State.PC);

        cpu.Step();
        Assert.Equal(0x7002, cpu.State.PC);
    }

    [Fact]
    public void Reset_LeavesTheNmiLineLevelAlone()
    {
        var (cpu, ram) = Machine(0xEA);
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x70;
        ram[0x7000] = 0xEA; ram[0x7001] = 0xEA; ram[0x7002] = 0xEA;
        cpu.SetNmi(true);

        cpu.Reset();

        // Reset clears the pending latch, never the pin level: the edge detector compares
        // against this, so clearing it would re-arm on a pin that never moved.
        Assert.True(cpu.NmiAsserted);

        cpu.Step();                    // reset sequence -> $7000
        cpu.SetNmi(true);              // the host re-reports the same, still-asserted level
        cpu.Step(); cpu.Step();
        Assert.Equal(0x7002, cpu.State.PC);   // no phantom edge, so no dispatch

        cpu.SetNmi(false); cpu.SetNmi(true);  // a genuine new edge
        cpu.Step();                            // the NOP at $7002; its poll latches
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);
    }

    [Fact]
    public void Nmi_HijacksAGenuineIrqInProgress()
    {
        var (cpu, ram) = Machine(0xEA);   // NOP; IRQ fires at the next boundary
        cpu.SetIrq(true);

        // Drive a genuine hardware IRQ dispatch (PushPInt, not BRK's PushPBrk) up to its
        // PushPcl, then latch NMI before its P push — cycle 5 of the sequence, the last
        // edge that still redirects the vector. Same window as BRK; same silicon sequence.
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
