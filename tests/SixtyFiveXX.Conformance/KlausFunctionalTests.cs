using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs Klaus Dormann's 6502 functional test to completion. Where the SingleStepTests
/// vectors check each instruction in isolation, this exercises interactions across a
/// real program of tens of millions of cycles.
/// </summary>
public class KlausFunctionalTests(ITestOutputHelper output)
{
    /// <summary>Entry point of the test program within the 64 KB image.</summary>
    private const ushort StartAddress = 0x0400;

    /// <summary>
    /// The address of the success trap. Verified from the published listing:
    /// <c>3469 : 4c6934  jmp *  ;test passed, no errors</c>.
    /// </summary>
    private const ushort SuccessAddress = 0x3469;

    /// <summary>Generous ceiling; a passing run completes in roughly 96 million cycles.</summary>
    private const long CycleCeiling = 500_000_000;

    [Fact]
    public void FunctionalTest_RunsToTheSuccessTrap()
    {
        var ram = KlausCache.Load("6502_functional_test.bin");
        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));
        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        // Both success and failure are signalled by a branch to self, so the test is
        // over the moment an instruction leaves PC where it started.
        ushort previous = 0xFFFF;
        while (cpu.Cycles < CycleCeiling)
        {
            previous = cpu.State.PC;
            cpu.Step();

            if (cpu.State.PC == previous) break;
            if (cpu.IsJammed) break;
        }

        output.WriteLine($"Trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles.");

        Assert.False(cpu.IsJammed,
            $"The processor jammed at ${cpu.State.PC:X4} — an opcode was decoded wrongly.");

        Assert.True(cpu.Cycles < CycleCeiling,
            $"Test did not terminate within {CycleCeiling:N0} cycles; last PC ${cpu.State.PC:X4}.");

        Assert.True(cpu.State.PC == SuccessAddress,
            $"Trapped at ${cpu.State.PC:X4}, expected the success trap at ${SuccessAddress:X4}. " +
            $"The trap address identifies the failing sub-test — look it up in " +
            $"6502_functional_test.lst.");
    }
}
