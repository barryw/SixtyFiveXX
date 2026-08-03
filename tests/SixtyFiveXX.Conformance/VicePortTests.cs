using SixtyFiveXX.Variants;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs VICE's <c>testprogs/CPU/cpuport/test1</c>, the only independent oracle that exists
/// for the 6510's on-chip port. There is no SingleStepTests vector set for the 6510, so the
/// inherited instruction set is covered by the 6502's suites and the port delta by these
/// 136 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The program needs no C64. It writes <c>$D020</c>, <c>$0400</c> and <c>$D7FF</c> as
/// ordinary memory; no VIC-II, CIA or KERNAL is consulted. Four of its six assertions test
/// the floating-pin charge model rather than the plain direction/port semantics: that a bit
/// switched from output to input keeps reading what it was last driven to, and that writing
/// the port while a bit is an input does not change what that bit reads.
/// </para>
/// <para>
/// It is deliberately small. It checks bit 7 only, says nothing about bits 0-5, nothing
/// about <c>$00</c>'s own readback, and nothing about the port's interaction with the bus.
/// The unit suite is not a supplement to this gate; for most of the port it is the gate.
/// </para>
/// </remarks>
public class VicePortTests(ITestOutputHelper output)
{
    private const string Program = "CPU/cpuport/test1.prg";

    /// <summary>
    /// Entry point. The <c>.prg</c> loads at <c>$0801</c> behind a BASIC stub reading
    /// <c>SYS 2061</c>, and 2061 is <c>$080D</c>.
    /// </summary>
    private const ushort StartAddress = 0x080D;

    /// <summary>Where the program reports its verdict: <c>$00</c> passed, <c>$FF</c> failed.</summary>
    private const ushort ResultAddress = 0xD7FF;

    /// <summary>
    /// Where a failing run leaves an ASCII digit. The program stores <c>X</c>, which counts
    /// the assertions that <em>passed</em>, so a digit of <c>n</c> means assertion
    /// <c>n + 1</c> is the one that failed.
    /// </summary>
    private const ushort StepAddress = 0x0400;

    /// <summary>
    /// Generous ceiling. The program is fifty-odd instructions; the budget exists because a
    /// failing run ends in a two-instruction loop rather than a branch to self, so it is the
    /// only thing that stops it.
    /// </summary>
    private const long CycleCeiling = 100_000;

    [Fact]
    public void CpuPortTest1_Passes()
    {
        var (result, step, pc, cycles) = Run<Mos6510Variant>();
        output.WriteLine($"Result ${result:X2} at ${pc:X4} after {cycles:N0} cycles.");

        Assert.True(result == 0x00,
            $"cpuport/test1 reported ${result:X2} at ${ResultAddress:X4}; expected $00. " +
            $"The digit at ${StepAddress:X4} is '{(char)step}', so assertion {step - '0' + 1} failed. " +
            $"In order they are: 1 output readback, 2 the charge is retained after switching to " +
            $"input, 3 a write while input does not change the read, then 4-6 the same three " +
            $"with the opposite bit value.");
    }

    /// <summary>
    /// The same program on a 6502, where <c>$00</c> and <c>$01</c> are ordinary RAM, must
    /// <em>fail</em> — and fail on its third assertion, the first that ordinary RAM cannot
    /// satisfy: writing <c>$00</c> to <c>$01</c> while the bit is an input changes what RAM
    /// reads back but must not change what the port reads back. Two assertions pass first,
    /// so the digit is <c>'2'</c>. This is what makes the test above a gate rather than a
    /// formality: it proves the program discriminates the port from memory, so a passing
    /// 6510 has been measured against something it could have failed.
    /// </summary>
    [Fact]
    public void CpuPortTest1_FailsOnACoreWithoutThePort()
    {
        var (result, step, pc, cycles) = Run<Mos6502Variant>();
        output.WriteLine($"Result ${result:X2}, step '{(char)step}' at ${pc:X4} after {cycles:N0} cycles.");

        Assert.Equal(0xFF, result);
        Assert.Equal('2', (char)step);
    }

    private static (byte Result, byte Step, ushort Pc, long Cycles) Run<TVariant>()
        where TVariant : struct, ICpuVariant
    {
        var (ram, _) = ViceCache.LoadProgram(Program);
        var cpu = new Cpu<FlatBus, TVariant>(new FlatBus(ram));
        cpu.State.PC = StartAddress;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;

        while (cpu.Cycles < CycleCeiling)
        {
            var previous = cpu.State.PC;
            cpu.Step();

            // Success ends in a branch to self. Failure ends in an INC/JMP pair, which only
            // the cycle ceiling stops.
            if (cpu.State.PC == previous || cpu.IsJammed) break;
        }

        Assert.False(cpu.IsJammed, $"The processor jammed at ${cpu.State.PC:X4}.");

        return (ram[ResultAddress], ram[StepAddress], cpu.State.PC, cpu.Cycles);
    }
}
