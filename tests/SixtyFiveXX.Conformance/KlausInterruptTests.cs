using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs Klaus Dormann's interrupt test. It is the only independent validation of 6502
/// interrupt behaviour available: the SingleStepTests vectors carry no interrupt lines,
/// so nothing else in this project's suites exercises IRQ or NMI against an external
/// oracle.
/// </summary>
public class KlausInterruptTests(ITestOutputHelper output)
{
    private const ushort StartAddress = 0x0400;

    /// <summary>
    /// Generous headroom over the actual trap at <see cref="NmosTrapCycles"/> (2,721
    /// cycles) so a runaway core is caught in a moment rather than after the ~96-million-
    /// cycle ceiling the sibling functional test needs.
    /// </summary>
    private const long CycleCeiling = 100_000;

    /// <summary>
    /// The binary is built on demand by <c>klaus/build.sh</c> rather than committed. If it
    /// is missing the test fails with instructions rather than silently passing.
    /// </summary>
    private static string BinaryPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "klaus", "6502_interrupt_test.bin"));

    [Fact]
    public void InterruptTest_RunsToTheNmosHijackTrap()
    {
        Assert.True(File.Exists(BinaryPath),
            $"{BinaryPath} is missing. Build it with " +
            $"tests/SixtyFiveXX.Conformance/klaus/build.sh (requires 64tass).");

        var ram = File.ReadAllBytes(BinaryPath);
        Assert.Equal(0x10000, ram.Length);

        var feedback = new FeedbackBus(ram);
        var cpu = new Cpu<RefBus>(new RefBus(feedback));
        feedback.Attach(cpu);

        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        ushort previous = 0xFFFF;
        while (cpu.Cycles < CycleCeiling)
        {
            previous = cpu.State.PC;
            cpu.Step();

            if (cpu.State.PC == previous) break;   // a jmp * trap, success or failure
            if (cpu.IsJammed) break;
        }

        output.WriteLine($"Trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles.");

        Assert.False(cpu.IsJammed,
            $"The processor jammed at ${cpu.State.PC:X4} — an opcode was decoded wrongly.");

        Assert.False(feedback.DiagnosticStop,
            $"The test raised its diagnostic-stop bit; trapped at ${cpu.State.PC:X4}.");

        Assert.True(cpu.Cycles < CycleCeiling,
            $"Did not terminate within {CycleCeiling:N0} cycles; last PC ${cpu.State.PC:X4}.");

        // A faithful NMOS core cannot reach the nominal success trap at $06F5 here. The
        // final sub-test's #I_set macro asserts IRQ and NMI on the write cycle of a
        // STA $BFFC that is immediately followed by BRK. The interrupt poll for that
        // STA-to-BRK boundary samples the pin state one cycle earlier — before the write
        // that asserts it — so BRK always starts normally, with the latch already set well
        // before its cycle 5. PushPBrk pushes the status byte with B set and, on that same
        // cycle, hijacks the vector to the NMI handler, leaving B set on the stack. That is
        // the documented NMOS BRK/NMI hijack (see
        // docs/superpowers/investigations/investigation-075c.md for the cycle-level trace
        // and the physically-reachable-timing sweep that rules out an off-by-one).
        // Klaus's own source calls it out on the trapping line itself
        // (asm 936-937: "unexpected B-flag! - this may fail on a real 6502 / due to a
        // hardware bug on concurrent BRK & NMI") and again eleven lines later, at the
        // very next check the same hijack event trips once this one is neutralised
        // (asm 830: "may fail due to a bug on a real NMOS 6502 - NMI could mask BRK"; see
        // docs/superpowers/investigations/experiment-past-075c.md). So this trap is not a gap in
        // coverage: it is the true edge of what a faithful NMOS core can satisfy, and it
        // covers 2,720 of the program's ~2,915 cycles — about 93%. Pinning both the
        // address and the exact cycle count turns that whole prefix into a regression
        // gate: any change to core interrupt or BRK timing ahead of this point moves one
        // of these two numbers.
        Assert.True(cpu.State.PC == NmosTrapAddress,
            $"Trapped at ${cpu.State.PC:X4}, expected the NMOS BRK/NMI hijack trap at " +
            $"${NmosTrapAddress:X4}, not the nominal success trap at ${SuccessAddress:X4} " +
            $"— see SuccessAddress's doc comment for why that one is unreachable here. The " +
            $"trap address identifies the failing sub-test — look it up in " +
            $"6502_interrupt_test.lst.");

        Assert.True(cpu.Cycles == NmosTrapCycles,
            $"Trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles, expected " +
            $"exactly {NmosTrapCycles:N0}. A mismatch means core interrupt or BRK timing " +
            $"changed somewhere in the prefix leading up to this trap.");
    }

    /// <summary>
    /// Address of the nominal success trap, read from the 64tass listing that
    /// <c>klaus/build.sh</c> generates: <c>jmp *</c> reached only when every sub-test
    /// passes. Documented for context only — unreachable on a faithful NMOS core; see
    /// <see cref="NmosTrapAddress"/>.
    /// </summary>
    private const ushort SuccessAddress = 0x06F5;

    /// <summary>
    /// Address of the trap a faithful NMOS core actually reaches: the concurrent
    /// BRK/NMI B-flag check in Klaus's "overlapping NMI, IRQ &amp; BRK" sub-test
    /// (asm 933-937), which Klaus's own comment documents as expected to fail on real
    /// NMOS hardware.
    /// </summary>
    private const ushort NmosTrapAddress = 0x075C;

    /// <summary>
    /// Cycle count at which <see cref="NmosTrapAddress"/> is reached. The run is fully
    /// deterministic, so pinning this exactly turns the entire preceding prefix into an
    /// exact regression gate.
    /// </summary>
    private const long NmosTrapCycles = 2721;
}
