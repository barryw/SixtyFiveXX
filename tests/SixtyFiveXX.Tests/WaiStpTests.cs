using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// WDC's <c>WAI</c> and <c>STP</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These tests are the entire gate for these two instructions.</strong> Klaus's
/// 65C02 program does not use them, and SingleStepTests ships <em>empty</em> vector files
/// for <c>$CB</c> and <c>$DB</c> — verified upstream, where both are zero bytes while a
/// neighbouring opcode is 3.3 MB. Neither instruction can be expressed as a
/// before-and-after vector for a single instruction, because both halt. Nothing else in
/// this project will catch a mistake here, so these are written as a specification rather
/// than as a spot check.
/// </para>
/// <para>
/// Where behaviour is not pinned by an external oracle it is stated deliberately, and the
/// reasoning is in the assertion. The wake condition for <c>WAI</c> is the interrupt
/// <em>signal</em>, not the interrupt being taken: it resumes with <c>I</c> set, and the
/// instruction after <c>WAI</c> then runs instead of a handler. That is the property that
/// makes <c>WAI</c> useful for cycle-exact synchronisation, and the one most likely to be
/// modelled wrongly.
/// </para>
/// </remarks>
public class WaiStpTests
{
    private static (Cpu<FlatBus, Wdc65C02Variant> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat<Wdc65C02Variant>(0x0200, program);
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;   // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;   // NMI     -> $8000
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x70;   // RESET   -> $7000
        return (cpu, ram);
    }

    // ---- WAI ----

    [Fact]
    public void Wai_HoldsTheProcessorUntilAnInterruptIsSignalled()
    {
        var (cpu, _) = Machine(0xCB, 0xEA);   // WAI; NOP
        cpu.State.P = Flag.U;                  // I clear

        for (var i = 0; i < 50; i++) cpu.Tick();

        Assert.True(cpu.IsWaiting);
        Assert.Equal(0x0201, cpu.State.PC);    // consumed WAI itself, then held
        Assert.False(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void Wai_ConsumesOneCyclePerTickWhileHolding()
    {
        // A held processor still runs the clock — it is stalled, not stopped, which is
        // what makes it usable to wait for a raster interrupt without losing cycle count.
        var (cpu, _) = Machine(0xCB);
        cpu.State.P = Flag.U;

        cpu.Tick();
        var atHold = cpu.Cycles;
        for (var i = 0; i < 20; i++) cpu.Tick();

        Assert.Equal(atHold + 20, cpu.Cycles);
    }

    [Fact]
    public void Wai_ReleasesOnIrqAndTakesTheInterruptWhenIIsClear()
    {
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U;                  // I clear, so the interrupt is taken

        for (var i = 0; i < 10; i++) cpu.Tick();
        Assert.True(cpu.IsWaiting);

        cpu.SetIrq(true);
        cpu.Tick();
        Assert.False(cpu.IsWaiting);

        // One Step dispatches: the wake cycle's poll already saw the interrupt, so the
        // next fetch takes it. A second Step would execute the handler's first byte, and
        // with RAM zeroed that is BRK — whose vector is also $9000, so the assertion would
        // pass for the wrong reason.
        cpu.Step();
        Assert.Equal(0x9000, cpu.State.PC);
    }

    [Fact]
    public void Wai_ReleasesOnIrqEvenWhenIIsSet_AndRunsTheNextInstruction()
    {
        // The defining property. I blocks the interrupt being *taken*, not the wake, so a
        // WAI with interrupts masked is a precise stall until the signal arrives. A model
        // that waited on the interrupt poll instead would hang here forever.
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U | Flag.I;

        for (var i = 0; i < 10; i++) cpu.Tick();
        Assert.True(cpu.IsWaiting);

        cpu.SetIrq(true);
        cpu.Tick();
        Assert.False(cpu.IsWaiting);

        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);    // the NOP after WAI, not the handler
    }

    [Fact]
    public void Wai_ReleasesOnNmi()
    {
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U | Flag.I;         // NMI is never blocked by I

        for (var i = 0; i < 10; i++) cpu.Tick();
        cpu.SetNmi(true);
        cpu.Tick();

        Assert.False(cpu.IsWaiting);

        cpu.Step();
        Assert.Equal(0x8000, cpu.State.PC);
    }

    [Fact]
    public void Wai_ReleasesOnAnIrqAssertedBeforeItEvenRuns()
    {
        // The line is level-sensitive, so an interrupt already asserted must not cause a
        // wait at all. Modelling the wake as an edge would hang here.
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U | Flag.I;
        cpu.SetIrq(true);

        cpu.Step();

        Assert.False(cpu.IsWaiting);
    }

    [Fact]
    public void Wai_StepReturnsRatherThanSpinningWhileHeld()
    {
        // Only the host can assert the interrupt that releases WAI, so Step must return
        // while held. Otherwise a single Step would never terminate.
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U;

        var cycles = cpu.Step();

        Assert.True(cpu.IsWaiting);
        Assert.True(cycles > 0);
    }

    [Fact]
    public void Wai_IsClearedByReset()
    {
        var (cpu, _) = Machine(0xCB);
        cpu.State.P = Flag.U;
        cpu.Step();
        Assert.True(cpu.IsWaiting);

        cpu.Reset();

        Assert.False(cpu.IsWaiting);
    }

    // ---- STP ----

    [Fact]
    public void Stp_HaltsTheProcessorIndefinitely()
    {
        var (cpu, _) = Machine(0xDB, 0xEA);

        for (var i = 0; i < 100; i++) cpu.Tick();

        Assert.True(cpu.IsStopped);
        Assert.Equal(0x0201, cpu.State.PC);
    }

    [Fact]
    public void Stp_IsNotReleasedByAnyInterrupt()
    {
        // The difference from WAI, and the whole point of the instruction.
        var (cpu, _) = Machine(0xDB, 0xEA);
        cpu.State.P = Flag.U;
        cpu.Step();
        Assert.True(cpu.IsStopped);

        cpu.SetIrq(true);
        cpu.SetNmi(true);
        for (var i = 0; i < 20; i++) cpu.Tick();

        Assert.True(cpu.IsStopped);
        Assert.Equal(0x0201, cpu.State.PC);
    }

    [Fact]
    public void Stp_IsClearedOnlyByReset()
    {
        var (cpu, _) = Machine(0xDB);
        cpu.Step();
        Assert.True(cpu.IsStopped);

        cpu.Reset();
        Assert.False(cpu.IsStopped);

        cpu.Step();                            // the reset sequence
        Assert.Equal(0x7000, cpu.State.PC);
    }

    [Fact]
    public void Stp_IsDistinctFromAJam()
    {
        // STP is a defined instruction, not a decode failure, so IsJammed must stay false —
        // a consumer distinguishing "the program stopped the CPU" from "the CPU fell over"
        // needs the two to be separate.
        var (cpu, _) = Machine(0xDB);

        cpu.Step();

        Assert.True(cpu.IsStopped);
        Assert.False(cpu.IsJammed);
    }

    [Fact]
    public void Stp_StepReturnsRatherThanSpinning()
    {
        var (cpu, _) = Machine(0xDB);

        var cycles = cpu.Step();

        Assert.True(cpu.IsStopped);
        Assert.True(cycles > 0);
    }

    // ---- RDY during a hold ----

    [Fact]
    public void Wai_HeldByRdy_DrivesPcRatherThanAStaleAddress()
    {
        // WAI exists to synchronise with hardware that also drives RDY, so the overlap is
        // the expected case, not a corner. A held cycle re-drives the bus without
        // advancing, and for an unbounded hold it must drive PC: the effective-address
        // register is stale here — WAI is an implied instruction and never sets it — so a
        // naive halt would drive the *previous* instruction's address for the entire wait,
        // on a bus that may have read side effects.
        var (cpu, ram, log) = TestMachine.Logged<Wdc65C02Variant>(0x0200, 0xAD, 0x00, 0x40, 0xCB);
        ram[0x4000] = 0x7E;                       // LDA $4000 leaves $4000 in the address latch
        cpu.State.P = Flag.U;

        cpu.Step();                                // LDA $4000
        Assert.Equal(0x7E, cpu.State.A);

        cpu.Step();                                // WAI, now holding
        Assert.True(cpu.IsWaiting);

        log.Clear();
        cpu.SetRdy(false);
        cpu.Tick();
        cpu.Tick();

        Assert.All(log, a => Assert.Equal(0x0204, a.Address));   // PC, not the stale $4000
        Assert.DoesNotContain(log, a => a.IsWrite);
    }

    [Fact]
    public void Stp_HeldByRdy_AlsoDrivesPc()
    {
        var (cpu, ram, log) = TestMachine.Logged<Wdc65C02Variant>(0x0200, 0xAD, 0x00, 0x40, 0xDB);
        ram[0x4000] = 0x7E;

        cpu.Step();
        cpu.Step();
        Assert.True(cpu.IsStopped);

        log.Clear();
        cpu.SetRdy(false);
        cpu.Tick();

        Assert.All(log, a => Assert.Equal(0x0204, a.Address));
    }

    [Fact]
    public void Wai_ReleasedWhileRdyIsLow_StaysHeldUntilRdyReturns()
    {
        // The two hold conditions are independent. An interrupt clears the wait, but a low
        // RDY still halts the processor, so nothing advances until both are satisfied.
        var (cpu, _) = Machine(0xCB, 0xEA);
        cpu.State.P = Flag.U;

        cpu.Step();
        Assert.True(cpu.IsWaiting);

        cpu.SetRdy(false);
        cpu.SetIrq(true);
        for (var i = 0; i < 10; i++) cpu.Tick();

        Assert.Equal(0x0201, cpu.State.PC);        // RDY still holds it at the WAI
        Assert.True(cpu.IsWaiting);                // the hold micro-op never ran to clear it

        cpu.SetRdy(true);
        cpu.Tick();

        Assert.False(cpu.IsWaiting);
    }

    // ---- The 65816 ----
    //
    // The same two instructions on the 65816, and the same rules — which is why they are in
    // this file rather than a new one. What differs is the coverage situation. SingleStepTests
    // DOES ship $CB and $DB for the 65816: 40,000 vectors, all four entries long, of which
    // research document §14.4 measured the fourth to be [null, null, "--------"] in every single
    // one. So the three EXECUTED cycles are arbitrated by Harte816Tests — but the vector set
    // models no hold, no wake, no interrupt and no reset anywhere in those 40,000 files.
    //
    // The four rules below are therefore in exactly the position the 65C02's are: nothing but
    // these tests will catch a mistake in them. They are (1) that the core holds at all rather
    // than running on like a three-cycle NOP, (2) that IRQB or NMIB wakes it, (3) that the wake
    // is the SIGNAL and not the interrupt poll, so I set does not prevent it, and (4) that STP
    // is escaped only by Reset.

    /// <summary>
    /// A 65816 with WAI or STP at $12C000, the native and emulation vectors all distinct, and
    /// <c>m</c> and <c>x</c> deliberately opposed — neither instruction reads a
    /// width-dependent register, and a sequence that consulted one anyway would show it here.
    /// </summary>
    private static (Cpu<RefBus, W65C816Variant> Cpu, BankedBus Ram) Machine816(bool emulation, byte opcode)
    {
        var ram = new BankedBus();
        ram[0x12C000] = opcode;
        ram[0x12C001] = 0xEA;                                       // the NOP after it
        (ram[0xFFEA], ram[0xFFEB]) = (0x00, 0x33);                  // native NMI       -> $003300
        (ram[0xFFEE], ram[0xFFEF]) = (0x00, 0x44);                  // native IRQ       -> $004400
        (ram[0xFFFA], ram[0xFFFB]) = (0x00, 0x66);                  // emulation NMI    -> $006600
        (ram[0xFFFC], ram[0xFFFD]) = (0x00, 0x70);                  // RESET            -> $007000
        (ram[0xFFFE], ram[0xFFFF]) = (0x00, 0x77);                  // emulation IRQ    -> $007700

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.PBR = 0x12;
        cpu.State.E = emulation;
        cpu.State.P = Flag.U;
        cpu.State.M = false;                                        // opposed on purpose
        cpu.State.XFlag = true;
        cpu.State.S = emulation ? (ushort)0x01FF : (ushort)0x1FFF;
        return (cpu, ram);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void W65C816Wai_HoldsTheProcessorUntilAnInterruptIsSignalled(bool emulation)
    {
        var (cpu, _) = Machine816(emulation, 0xCB);

        for (var i = 0; i < 50; i++) cpu.Tick();

        Assert.True(cpu.IsWaiting);
        Assert.Equal(0xC001, cpu.State.PC);          // consumed WAI itself, then held
        Assert.Equal(0x12, cpu.State.PBR);           // and never left the bank
        Assert.False(cpu.AtInstructionBoundary);     // held mid-sequence, not ended
    }

    [Theory]
    [InlineData(0xCB)]
    [InlineData(0xDB)]
    public void W65C816Halt_RunsThreeCycles_ThenHoldsWithoutTouchingTheBus(int opcode)
    {
        // Research document §14.4, measured: the datasheet's three cycles are an opcode fetch
        // and two internal cycles at PBR,PC+1, and the vectors' fourth entry is
        // [null, null, "--------"] — no address, no value, not even a read/write character. So
        // exactly one bus access happens (the fetch), and the hold adds none however long it
        // lasts. This is the property Harte816Tests' cycle-count assertion rests on.
        var (cpu, ram) = Machine816(emulation: false, (byte)opcode);

        var cycles = cpu.Step();

        Assert.Equal(4, cycles);                     // three executed, then the held one
        Assert.Single(ram.Log);                      // the opcode fetch, and nothing else
        Assert.Equal((0x12C000, (byte)opcode, false), ram.Log[0]);

        for (var i = 0; i < 100; i++) cpu.Tick();

        Assert.Single(ram.Log);                      // a hold drives no bus cycle at all
        Assert.Equal(cycles + 100, cpu.Cycles);      // but the clock still runs
    }

    [Theory]
    [InlineData(false, 0x004400)]
    [InlineData(true, 0x007700)]
    public void W65C816Wai_ReleasesOnIrqAndTakesTheInterrupt_WhenIIsClear(bool emulation, int handler)
    {
        var (cpu, _) = Machine816(emulation, 0xCB);

        cpu.Step();
        Assert.True(cpu.IsWaiting);

        cpu.SetIrq(true);
        cpu.Tick();
        Assert.False(cpu.IsWaiting);
        Assert.True(cpu.AtInstructionBoundary);      // the wake ENDS the instruction

        cpu.Step();                                  // the interrupt sequence
        Assert.Equal(handler & 0xFFFF, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);           // handlers run in bank 0
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void W65C816Wai_ReleasesOnIrqEvenWhenIIsSet_AndRunsTheNextInstruction(bool emulation)
    {
        // The i-flag special case, Clark §6.9 verbatim: "WAI when the i flag is 1 is a special
        // case; specifically, when an IRQ occurs (after the WAI instruction), the 65C816 will
        // continue with the next instruction rather than jumping to the interrupt vector." A
        // model that waited on the interrupt POLL instead of the signal would hang here forever.
        var (cpu, _) = Machine816(emulation, 0xCB);
        cpu.State.I = true;

        cpu.Step();
        Assert.True(cpu.IsWaiting);

        cpu.SetIrq(true);
        cpu.Tick();
        Assert.False(cpu.IsWaiting);

        cpu.Step();                                  // the NOP at $12C001, not a handler
        Assert.Equal(0xC002, cpu.State.PC);
        Assert.Equal(0x12, cpu.State.PBR);
    }

    [Theory]
    [InlineData(false, 0x003300)]
    [InlineData(true, 0x006600)]
    public void W65C816Wai_ReleasesOnNmi(bool emulation, int handler)
    {
        var (cpu, _) = Machine816(emulation, 0xCB);
        cpu.State.I = true;                          // NMI is never blocked by I

        cpu.Step();
        Assert.True(cpu.IsWaiting);

        cpu.SetNmi(true);
        cpu.Tick();
        Assert.False(cpu.IsWaiting);

        cpu.Step();
        Assert.Equal(handler & 0xFFFF, cpu.State.PC);
    }

    [Fact]
    public void W65C816Wai_ReleasesOnAnIrqAssertedBeforeItEvenRuns()
    {
        // Level-sensitive, as on the 65C02: an interrupt already asserted must not cause a wait.
        var (cpu, _) = Machine816(emulation: false, 0xCB);
        cpu.State.I = true;
        cpu.SetIrq(true);

        cpu.Step();

        Assert.False(cpu.IsWaiting);
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void W65C816Stp_IsNotReleasedByAnyInterrupt(bool emulation)
    {
        var (cpu, _) = Machine816(emulation, 0xDB);

        cpu.Step();
        Assert.True(cpu.IsStopped);

        cpu.SetIrq(true);
        cpu.SetNmi(true);
        for (var i = 0; i < 50; i++) cpu.Tick();

        Assert.True(cpu.IsStopped);
        Assert.Equal(0xC001, cpu.State.PC);
        Assert.False(cpu.IsJammed);                  // a defined instruction, not a decode failure
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void W65C816Stp_IsClearedOnlyByReset(bool emulation)
    {
        var (cpu, _) = Machine816(emulation, 0xDB);

        cpu.Step();
        Assert.True(cpu.IsStopped);

        cpu.Reset();
        Assert.False(cpu.IsStopped);

        cpu.Step();                                  // the reset sequence
        Assert.Equal(0x7000, cpu.State.PC);
        Assert.True(cpu.State.E);                    // and reset forces emulation mode
    }

    [Theory]
    [InlineData(0xCB)]
    [InlineData(0xDB)]
    public void W65C816Halt_HeldByRdy_DrivesPbrPcRatherThanAStaleAddress(int opcode)
    {
        // The 65816 counterpart of the two 65C02 RDY tests above, and the reason both halts are
        // on MicroOps.HoldsAtPc. The hold is unbounded, so the effective-address register — stale
        // here, since neither instruction ever sets it — would be driven for the entire wait. The
        // address must also carry the program bank: a bare sixteen-bit PC would read $00C001.
        var (cpu, _) = Machine816(emulation: false, (byte)opcode);

        cpu.Step();
        cpu.SetRdy(false);
        cpu.Tick();

        Assert.Equal(0x12C001, cpu.LastAddress);
        Assert.Equal(BusPins.None, cpu.LastPins);    // an internal cycle: the halt reads nothing
    }

    // ---- The other sub-variants do not have them ----

    [Fact]
    public void TheOtherSubVariantsTreatTheseOpcodesAsNops()
    {
        // $CB and $DB are NOPs on Synertek and Rockwell, with the shapes their vector sets
        // record. If this ever regressed, both of those cores would fail thousands of
        // vectors — but it is worth stating here, next to the WDC behaviour it contrasts.
        var (rockwell, _) = TestMachine.Flat<Rockwell65C02Variant>(0x0200, 0xDB, 0x40);

        rockwell.Step();

        Assert.False(rockwell.IsStopped);
        Assert.Equal(0x0202, rockwell.State.PC);
    }
}
