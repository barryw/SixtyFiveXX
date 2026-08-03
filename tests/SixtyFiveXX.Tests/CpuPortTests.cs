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
    public void InputBits_DoNotReadTheOutputLatch()
    {
        // With every bit an input, the latch is invisible: bits 0-5 have no pin driving
        // them here, so they read 0 rather than leaking what was written.
        var (cpu, _, _) = Machine(
            0xA9, 0xFF, 0x85, 0x00,     // all output
            0xA9, 0x3F, 0x85, 0x01,     // drive $3F
            0xA9, 0x00, 0x85, 0x00,     // all input
            0xA5, 0x01);                // LDA $01

        for (var i = 0; i < 7; i++) cpu.Step();

        Assert.Equal(0x00, cpu.State.A & 0x3F);
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
    public void ZeroPageAddressesAboveTheePortAreUnaffected()
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
}
