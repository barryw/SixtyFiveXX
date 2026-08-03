using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 6510's on-chip <c>$00</c> and <c>$01</c> registers.
/// </summary>
/// <remarks>
/// The only external oracle for any of this is VICE's <c>cpuport/test1</c>, which is 136
/// bytes and checks bit 7 alone. For bits 0-5, for the direction register's own readback,
/// and for the interaction with the bus, these tests are the entire gate — the same
/// position <c>WAI</c> and <c>STP</c> were in.
/// </remarks>
public class CpuPortTests
{
    private static (Cpu<RefBus, Mos6510Variant> Cpu, byte[] Ram, List<BusAccess> Log) Machine(
        params byte[] program) => TestMachine.Logged<Mos6510Variant>(0x0200, program);

    [Fact]
    public void PortWrites_NeverReachTheBus()
    {
        // The registers intercept, they do not shadow: the underlying RAM must be
        // untouched, and no bus cycle may carry the address at all. A model that wrote
        // through to RAM and read back from it would pass a value-only test.
        var (cpu, ram, log) = Machine(0xA9, 0xFF, 0x85, 0x00, 0xA9, 0x2A, 0x85, 0x01);

        cpu.Step();   // LDA #$FF
        cpu.Step();   // STA $00
        cpu.Step();   // LDA #$2A
        cpu.Step();   // STA $01

        Assert.Equal(0x00, ram[0x0000]);
        Assert.Equal(0x00, ram[0x0001]);
        Assert.DoesNotContain(log, a => a.IsWrite && a.Address <= 1);
    }

    [Fact]
    public void PortReads_NeverReachTheBus()
    {
        var (cpu, ram, log) = Machine(0xA5, 0x01);
        ram[0x0001] = 0x5A;   // whatever is in RAM must be invisible

        cpu.Step();

        Assert.NotEqual(0x5A, cpu.State.A);
        Assert.DoesNotContain(log, a => !a.IsWrite && a.Address == 1);
    }

    [Fact]
    public void DirectionRegister_ReadsBackWhatWasWritten()
    {
        var (cpu, _, _) = Machine(0xA9, 0x2F, 0x85, 0x00, 0xA5, 0x00);

        cpu.Step();
        cpu.Step();
        cpu.Step();

        Assert.Equal(0x2F, cpu.State.A);
    }

    [Fact]
    public void OutputBits_ReadBackWhatWasWritten()
    {
        // DDR $FF makes every bit an output, so the port reads back its own latch.
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,     // LDA #$FF; STA $00
            0xA9, 0x3C, 0x85, 0x01,     // LDA #$3C; STA $01
            0xA5, 0x01);                // LDA $01

        for (var i = 0; i < 5; i++) cpu.Step();

        Assert.Equal(0x3C, cpu.State.A);
    }

    [Fact]
    public void InputBits_ReadTheChargeLeftByTheLastDrive()
    {
        // Not the output latch — the pin. They coincide here, but the next test separates
        // them, and that separation is the whole point of the floating-bit model.
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,     // all output
            0xA9, 0x3F, 0x85, 0x01,     // drive $3F
            0xA9, 0x00, 0x85, 0x00,     // all input
            0xA5, 0x01);                // LDA $01

        for (var i = 0; i < 7; i++) cpu.Step();

        Assert.Equal(0x3F, cpu.State.A);
    }

    [Fact]
    public void AWriteWhileABitIsAnInput_DoesNotChangeWhatItReads()
    {
        // The property that separates the pin from the latch. A naive model that read the
        // latch back would return $00 here; hardware returns the charge, still $FF.
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,     // all output
            0xA9, 0xFF, 0x85, 0x01,     // drive $FF - charges every pin
            0xA9, 0x00, 0x85, 0x00,     // all input
            0xA9, 0x00, 0x85, 0x01,     // write $00 while floating
            0xA5, 0x01);                // LDA $01

        for (var i = 0; i < 9; i++) cpu.Step();

        Assert.Equal(0xFF, cpu.State.A);
    }

    [Fact]
    public void SettingABitToOutputCharges_WhicheverOrderTheRegistersAreWritten()
    {
        // Writing the port before the direction must charge the pin just as the other
        // order does: a bit switched to output starts driving whatever the latch holds.
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x01,     // latch $FF while everything is still an input
            0xA9, 0xFF, 0x85, 0x00,     // now make it all output - this is what charges
            0xA9, 0x00, 0x85, 0x00,     // back to input
            0xA5, 0x01);

        for (var i = 0; i < 7; i++) cpu.Step();

        Assert.Equal(0xFF, cpu.State.A);
    }

    /// <summary>
    /// VICE's <c>cpuport/test1</c>, step for step, on bit 7.
    /// </summary>
    /// <remarks>
    /// Mirrors the fetched <c>test1.s</c> exactly so a failure here points at the same step
    /// number the VICE program reports at <c>$0400</c>. Four of the six steps test the
    /// charge rather than the plain register semantics, which is why the design spec
    /// calling the floating behaviour out of scope was wrong: without it, this fails.
    /// </remarks>
    [Fact]
    public void VicePortTest1_StepForStep()
    {
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00, 0x85, 0x01,   // output, write 1
            0xA5, 0x01,                            // step 1: must read 1
            0xA9, 0x00, 0x85, 0x00,                // set to input
            0xA5, 0x01,                            // step 2: must still read 1
            0xA9, 0x00, 0x85, 0x01,                // write 0 while input
            0xA5, 0x01,                            // step 3: must still read 1
            0xA9, 0xFF, 0x85, 0x00, 0xA9, 0x00, 0x85, 0x01,   // output, write 0
            0xA5, 0x01,                            // step 4: must read 0
            0xA9, 0x00, 0x85, 0x00,                // set to input
            0xA5, 0x01,                            // step 5: must still read 0
            0xA9, 0xFF, 0x85, 0x01,                // write 1 while input
            0xA5, 0x01);                           // step 6: must still read 0

        void RunTo(int instructions) { for (var i = 0; i < instructions; i++) cpu.Step(); }

        RunTo(4); Assert.Equal(0x80, cpu.State.A & 0x80);   // 1
        RunTo(3); Assert.Equal(0x80, cpu.State.A & 0x80);   // 2
        RunTo(3); Assert.Equal(0x80, cpu.State.A & 0x80);   // 3
        RunTo(5); Assert.Equal(0x00, cpu.State.A & 0x80);   // 4
        RunTo(3); Assert.Equal(0x00, cpu.State.A & 0x80);   // 5
        RunTo(3); Assert.Equal(0x00, cpu.State.A & 0x80);   // 6
    }

    [Fact]
    public void DirectionRegister_SelectsPerBit()
    {
        // A mixed direction is the realistic case: the low nibble drives, the high nibble
        // floats, and only the driven bits read back.
        var (cpu, _, _) = Machine(
            0xA9, 0x0F, 0x85, 0x00,     // DDR = $0F
            0xA9, 0xFF, 0x85, 0x01,     // write $FF
            0xA5, 0x01);

        for (var i = 0; i < 5; i++) cpu.Step();

        Assert.Equal(0x0F, cpu.State.A & 0x0F);
    }

    [Fact]
    public void ThePortIsNotPresentOnThe6502()
    {
        // The same program on a 6502 must reach RAM, not a register. This is what the
        // JIT-folded variant test buys, and a regression that made the port unconditional
        // would break every other core's zero-page behaviour.
        var (cpu, ram, _) = TestMachine.Logged<Mos6502Variant>(0x0200, 0xA9, 0x2A, 0x85, 0x01, 0xA5, 0x01);

        cpu.Step();
        cpu.Step();
        Assert.Equal(0x2A, ram[0x0001]);

        cpu.Step();
        Assert.Equal(0x2A, cpu.State.A);
    }

    [Fact]
    public void ZeroPageAddressesAboveThePortAreUnaffected()
    {
        // Only $00 and $01 are intercepted. $02 is ordinary memory on every variant, and
        // an off-by-one in the address test would take it too.
        var (cpu, ram, _) = Machine(0xA9, 0x77, 0x85, 0x02, 0xA5, 0x02);

        cpu.Step();
        cpu.Step();
        Assert.Equal(0x77, ram[0x0002]);

        cpu.Step();
        Assert.Equal(0x77, cpu.State.A);
    }

    [Fact]
    public void TheStackAndVectorsAreUnaffected()
    {
        // The interception is by address, and the core reads the stack, vectors and PC
        // through the same path. A too-wide test would corrupt an interrupt.
        var (cpu, ram, _) = Machine(0x00);          // BRK
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;
        cpu.State.S = 0xFD;

        cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(0xFA, cpu.State.S);
        Assert.Equal(0x02, ram[0x01FD]);            // PCH reached the stack in RAM
    }

    [Fact]
    public void Reset_ClearsBothRegistersButNotTheCharge()
    {
        // VICE's cpuport/readme.txt: "DDR and DATA are both initialized to 0 on
        // powerup/reset". The charge is a capacitor and RES does not discharge it, so the
        // pins simply stop being driven and read back what they last held.
        var (cpu, ram, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,     // DDR  = $FF, every bit an output
            0xA9, 0x5A, 0x85, 0x01);    // DATA = $5A, charging every pin
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x90;

        for (var i = 0; i < 4; i++) cpu.Step();

        cpu.Reset();
        cpu.Step();                     // the reset sequence itself
        Assert.Equal(0x9000, cpu.State.PC);

        // LDA $00 / LDA $01 at the reset vector.
        ram[0x9000] = 0xA5; ram[0x9001] = 0x00;
        ram[0x9002] = 0xA5; ram[0x9003] = 0x01;

        cpu.Step();
        Assert.Equal(0x00, cpu.State.A);

        cpu.Step();
        Assert.Equal(0x5A, cpu.State.A);
    }

    [Fact]
    public void Reset_StopsOutputBitsFromDrivingTheirLatch()
    {
        // The half a cleared direction register alone would not catch: if RES cleared the
        // direction but left the output latch, a later DDR = $FF would drive the stale
        // value rather than the $00 a reset core holds.
        var (cpu, ram, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,
            0xA9, 0x5A, 0x85, 0x01);
        ram[0xFFFC] = 0x00; ram[0xFFFD] = 0x90;

        for (var i = 0; i < 4; i++) cpu.Step();

        cpu.Reset();
        cpu.Step();

        ram[0x9000] = 0xA9; ram[0x9001] = 0xFF;     // LDA #$FF
        ram[0x9002] = 0x85; ram[0x9003] = 0x00;     // STA $00 - back to all outputs
        ram[0x9004] = 0xA5; ram[0x9005] = 0x01;     // LDA $01

        for (var i = 0; i < 3; i++) cpu.Step();

        Assert.Equal(0x00, cpu.State.A);
    }
}
