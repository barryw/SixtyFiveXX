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
    private const long CycleCeiling = 100_000_000;

    /// <summary>
    /// The binary is built on demand by <c>klaus/build.sh</c> rather than committed. If it
    /// is missing the test fails with instructions rather than silently passing.
    /// </summary>
    private static string BinaryPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "klaus", "6502_interrupt_test.bin"));

    [Fact]
    public void InterruptTest_RunsToTheSuccessTrap()
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
            $"The processor jammed at ${cpu.State.PC:X4}.");

        Assert.False(feedback.DiagnosticStop,
            $"The test raised its diagnostic-stop bit; trapped at ${cpu.State.PC:X4}.");

        Assert.True(cpu.Cycles < CycleCeiling,
            $"Did not terminate within {CycleCeiling:N0} cycles; last PC ${cpu.State.PC:X4}.");

        Assert.Equal(SuccessAddress, cpu.State.PC);
    }

    /// <summary>
    /// Address of the success trap, read from the 64tass listing that <c>klaus/build.sh</c>
    /// generates. Task 5's README records where it came from.
    /// </summary>
    private const ushort SuccessAddress = 0x06F5;
}
