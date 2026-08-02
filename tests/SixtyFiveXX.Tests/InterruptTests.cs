using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

public class InterruptTests
{
    /// <summary>A CPU with an IRQ handler vector at $9000 and NOPs to execute.</summary>
    private static (Cpu<FlatBus, Mos6502Variant> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, program);
        ram[0xFFFE] = 0x00;
        ram[0xFFFF] = 0x90;   // IRQ/BRK vector -> $9000
        ram[0xFFFA] = 0x00;
        ram[0xFFFB] = 0x80;   // NMI vector -> $8000
        cpu.State.P = Flag.U; // I clear, interrupts enabled
        return (cpu, ram);
    }

    [Fact]
    public void Irq_IsNotTakenWhileTheInterruptDisableFlagIsSet()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA);
        cpu.State.I = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x0202, cpu.State.PC);   // both NOPs ran, no vector taken
    }

    [Fact]
    public void Irq_IsTakenAtTheNextInstructionBoundary()
    {
        var (cpu, ram) = Machine(0xEA, 0xEA);
        cpu.State.S = 0xFD;
        cpu.SetIrq(true);

        cpu.Step();                            // the NOP completes first
        Assert.Equal(0x0201, cpu.State.PC);

        var cycles = cpu.Step();               // then the interrupt sequence

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
        Assert.True(cpu.State.I);              // I is set on entry
        Assert.Equal(0xFA, cpu.State.S);       // three pushes
        Assert.Equal(0x02, ram[0x01FD]);       // PCH of the return address
        Assert.Equal(0x01, ram[0x01FC]);       // PCL
    }

    [Fact]
    public void Irq_PushesStatusWithTheBreakFlagClear()
    {
        var (cpu, ram) = Machine(0xEA);
        cpu.State.S = 0xFD;
        cpu.State.C = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        var pushed = ram[0x01FB];
        Assert.Equal(0, pushed & Flag.B);      // B clear distinguishes IRQ from BRK
        Assert.Equal(Flag.U, pushed & Flag.U); // U always set
        Assert.Equal(Flag.C, pushed & Flag.C);
    }

    [Fact]
    public void Irq_DoesNotClearDecimalModeOnNmos()
    {
        var (cpu, _) = Machine(0xEA);
        cpu.State.D = true;
        cpu.SetIrq(true);

        cpu.Step();
        cpu.Step();

        Assert.True(cpu.State.D);              // NMOS leaves D alone; the 65C02 clears it
    }

    [Fact]
    public void Irq_IsLevelSensitiveAndStopsFiringWhenTheLineIsReleased()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA);
        cpu.SetIrq(true);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);

        // Clear I with the line still asserted. A level-sensitive interrupt is polled
        // fresh every cycle and fires again even though nothing re-asserted the line;
        // a latching SetIrq would not, since the earlier SetIrq(true) call is all that
        // set it. The poll model costs two Step() calls to observe: the first runs the
        // NOP (its fetch cycle still reads the stale poll left by the entry sequence),
        // and the poll taken during that NOP's own cycle is what the second Step() acts on.
        cpu.State.I = false;
        cpu.State.PC = 0x0201;

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);    // the NOP ran; one-instruction delay

        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);    // re-fired: the line, not a latch, drove this
        Assert.True(cpu.State.I);

        // Now release the line and repeat the same two-Step probe. This time the poll
        // taken during the NOP's cycle sees the line low, so the following fetch runs
        // the next instruction instead of dispatching again.
        cpu.SetIrq(false);
        cpu.State.I = false;
        cpu.State.PC = 0x0201;

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);

        cpu.Step();
        Assert.Equal(0x0203, cpu.State.PC);    // no second dispatch: the line is truly gone
    }

    [Fact]
    public void Cli_DelaysAPendingIrqByOneInstruction()
    {
        // CLI polls before its own effect, so the IRQ is taken after the NEXT instruction.
        var (cpu, _) = Machine(0x58, 0xEA, 0xEA);   // CLI, NOP, NOP
        cpu.State.I = true;
        cpu.SetIrq(true);

        cpu.Step();                                  // CLI
        Assert.Equal(0x0201, cpu.State.PC);
        Assert.False(cpu.State.I);

        cpu.Step();                                  // the NOP runs, not the handler
        Assert.Equal(0x0202, cpu.State.PC);

        cpu.Step();                                  // now the handler
        Assert.Equal(0x9000, cpu.State.PC);
    }

    [Fact]
    public void Sei_DoesNotPreventAnAlreadyPendingIrq()
    {
        // SEI polls before its own effect, so an IRQ asserted beforehand still lands.
        var (cpu, _) = Machine(0x78, 0xEA);          // SEI, NOP
        cpu.SetIrq(true);

        cpu.Step();                                  // SEI
        Assert.True(cpu.State.I);

        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);          // taken anyway
    }

    [Fact]
    public void IrqAsserted_ReportsTheCurrentPinState()
    {
        var (cpu, _) = Machine(0xEA);

        Assert.False(cpu.IrqAsserted);
        cpu.SetIrq(true);
        Assert.True(cpu.IrqAsserted);
        cpu.SetIrq(false);
        Assert.False(cpu.IrqAsserted);
    }

    [Fact]
    public void Nmi_IsTakenEvenWithInterruptsDisabled()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.State.I = true;
        cpu.SetNmi(true);

        cpu.Step();
        var cycles = cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_IsEdgeTriggeredAndFiresOnlyOncePerTransition()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA, 0xEA);
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);

        // The line is still high, but there has been no new edge.
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);   // no second dispatch
    }

    [Fact]
    public void Nmi_LatchesEvenIfTheLineIsReleasedBeforeTheBoundary()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.SetNmi(true);
        cpu.SetNmi(false);                     // pulse: high then low

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);    // the latch survived the release
    }

    [Fact]
    public void Nmi_FiresAgainAfterANewEdge()
    {
        var (cpu, _) = Machine(0xEA, 0xEA, 0xEA, 0xEA);
        cpu.SetNmi(true);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);

        cpu.SetNmi(false);
        cpu.SetNmi(true);                      // a fresh edge
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);    // intermediate: the NOP ran, not a dispatch
        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);    // re-fired: exactly one dispatch, from the edge

        // Holding the line high and re-asserting it while it is already high must not
        // latch again — the poll model's edge check is `asserted && !_nmiLine`. A latching
        // SetNmi (dropping that check) would re-arm on every one of these calls, and the
        // driver pattern a host actually uses is exactly this: calling SetNmi(cia.Active)
        // once per tick while the line is held. Assert exactly one dispatch happened above
        // and none happen below, or an NMI storm would pass this suite silently.
        cpu.SetNmi(true);
        cpu.SetNmi(true);
        cpu.SetNmi(true);
        cpu.State.PC = 0x0201;

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);    // one instruction ran...
        cpu.Step();
        Assert.Equal(0x0203, cpu.State.PC);    // ...and the next — no second dispatch
    }

    [Fact]
    public void Nmi_TakesPriorityOverASimultaneousIrq()
    {
        var (cpu, _) = Machine(0xEA, 0xEA);
        cpu.SetIrq(true);
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x8000, cpu.State.PC);    // the NMI vector, not $9000
    }

    [Fact]
    public void Nmi_PushesStatusWithTheBreakFlagClear()
    {
        var (cpu, ram) = Machine(0xEA);
        cpu.State.S = 0xFD;
        cpu.SetNmi(true);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0, ram[0x01FB] & Flag.B);
    }

    [Fact]
    public void NmiAsserted_ReportsTheCurrentPinState()
    {
        var (cpu, _) = Machine(0xEA);

        Assert.False(cpu.NmiAsserted);
        cpu.SetNmi(true);
        Assert.True(cpu.NmiAsserted);
        cpu.SetNmi(false);
        Assert.False(cpu.NmiAsserted);
    }
}
