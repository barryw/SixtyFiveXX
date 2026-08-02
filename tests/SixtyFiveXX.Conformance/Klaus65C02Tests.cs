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
/// A gate for Rockwell and WDC, which both have the bit operations the program uses. It is
/// deliberately not run for Synertek, where those opcodes are NOPs: the program would run
/// and fail rather than fail to load, and Synertek is carried by its own vector set — which
/// is the whole reason the three sub-variants have separate ones.
/// </para>
/// <para>
/// The program does not use <c>WAI</c> or <c>STP</c>, so it adds nothing to WDC's coverage
/// of those two. They have no vectors either; <c>WaiStpTests</c> is their entire gate.
/// </para>
/// </remarks>
public class Klaus65C02Tests(ITestOutputHelper output)
{
    /// <summary>Entry point of the test program within the 64 KB image.</summary>
    private const ushort StartAddress = 0x0400;

    /// <summary>Generous ceiling; a passing run completes well inside this.</summary>
    private const long CycleCeiling = 500_000_000;

    [Fact]
    public void ExtendedOpcodeTest_RunsToTheSuccessTrap_OnRockwell() =>
        RunToSuccessTrap<Rockwell65C02Variant>();

    [Fact]
    public void ExtendedOpcodeTest_RunsToTheSuccessTrap_OnWdc() =>
        RunToSuccessTrap<Wdc65C02Variant>();

    private void RunToSuccessTrap<TVariant>() where TVariant : struct, ICpuVariant
    {
        var ram = KlausCache.Load("65C02_extended_opcodes_test.bin");
        var cpu = new Cpu<FlatBus, TVariant>(new FlatBus(ram));
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

        output.WriteLine($"{TVariant.Variant}: trapped at ${cpu.State.PC:X4} after {cpu.Cycles:N0} cycles.");

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
