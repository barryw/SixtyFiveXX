using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs Klaus Dormann's 65C02 extended-opcode test to completion.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="KlausFunctionalTests"/> for the CMOS parts: where the
/// SingleStepTests vectors check each opcode in isolation, this exercises the new
/// instructions against each other across a real program.
/// </para>
/// <para>
/// It is a gate for Rockwell only among the sub-variants implemented so far. The program
/// uses <c>RMB</c>/<c>SMB</c>/<c>BBR</c>/<c>BBS</c>, which Synertek does not have — there
/// they are NOPs, so the program would run and fail rather than fail to assemble. Synertek
/// is carried by its Harte set alone, which is the whole reason the three sub-variants have
/// separate vector sets.
/// </para>
/// </remarks>
public class Klaus65C02Tests(ITestOutputHelper output)
{
    /// <summary>Entry point of the test program within the 64 KB image.</summary>
    private const ushort StartAddress = 0x0400;

    /// <summary>Generous ceiling; a passing run completes well inside this.</summary>
    private const long CycleCeiling = 500_000_000;

    [Fact]
    public void ExtendedOpcodeTest_RunsToTheSuccessTrap()
    {
        var ram = KlausCache.Load("65C02_extended_opcodes_test.bin");
        var cpu = new Cpu<FlatBus, Rockwell65C02Variant>(new FlatBus(ram));
        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        // Both success and failure are signalled by a branch to self, so the test is over
        // the moment an instruction leaves PC where it started.
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
            $"65C02_extended_opcodes_test.lst.");
    }

    /// <summary>
    /// The address of the success trap, confirmed by running the program rather than taken
    /// on trust: a wrong constant here would turn any early failure trap into a pass.
    /// </summary>
    private const ushort SuccessAddress = 0x24F1;
}
