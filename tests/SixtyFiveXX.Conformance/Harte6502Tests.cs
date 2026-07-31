using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs every SingleStepTests vector for the documented 6502 opcodes, checking the
/// final register file, the named RAM bytes, and the exact per-cycle bus activity.
/// </summary>
public class Harte6502Tests(ITestOutputHelper output)
{
    /// <summary>The 151 documented opcodes. Undocumented opcodes arrive in Phase 2.</summary>
    public static TheoryData<byte> LegalOpcodes
    {
        get
        {
            var data = new TheoryData<byte>();
            for (var opcode = 0; opcode < 256; opcode++)
            {
                if (Opcodes6502.Table[opcode].Operation != Op.Undefined) data.Add((byte)opcode);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LegalOpcodes))]
    public void Opcode_MatchesEveryVector(byte opcode)
    {
        var cases = HarteCache.Load("6502", opcode);
        Assert.NotEmpty(cases);

        // One 64 KB buffer and one log for the whole file. Allocating per vector would
        // mean 10,000 64 KB arrays per opcode, which dominates the suite's runtime.
        var ram = new byte[0x10000];
        var log = new List<Cycle>(16);
        var cpu = new Cpu<HarteBus>(new HarteBus(ram, log));

        foreach (var test in cases)
        {
            Array.Clear(ram);
            log.Clear();

            foreach (var entry in test.Initial.Ram) ram[entry[0]] = (byte)entry[1];

            cpu.State = new CpuState
            {
                PC = test.Initial.Pc,
                S = test.Initial.S,
                A = test.Initial.A,
                X = test.Initial.X,
                Y = test.Initial.Y,
                P = test.Initial.P,
            };

            cpu.Step();

            AssertRegisters(test, cpu.State);
            AssertMemory(test, ram);
            AssertCycles(test, log);
        }

        output.WriteLine($"${opcode:X2} {Opcodes6502.Table[opcode].Mnemonic}: {cases.Length} vectors passed.");
    }

    /// <summary>
    /// Records how much of the opcode space this phase covers, so a green suite is
    /// never mistaken for complete coverage.
    /// </summary>
    [Fact]
    public void Coverage_IsReportedHonestly()
    {
        var legal = LegalOpcodes.Count;
        var undefined = 256 - legal;

        output.WriteLine($"Phase 1 runs {legal} of 256 opcodes ({legal * 10_000:N0} vectors).");
        output.WriteLine($"{undefined} undocumented opcodes are NOT covered and land in Phase 2.");

        Assert.Equal(151, legal);
    }

    private static void AssertRegisters(HarteCase test, in CpuState actual)
    {
        var expected = test.Final;
        var same = actual.PC == expected.Pc && actual.S == expected.S && actual.A == expected.A &&
                   actual.X == expected.X && actual.Y == expected.Y && actual.P == expected.P;

        Assert.True(same,
            $"{test.Name}: registers diverged.\n" +
            $"  expected PC:{expected.Pc:X4} A:{expected.A:X2} X:{expected.X:X2} " +
            $"Y:{expected.Y:X2} S:{expected.S:X2} P:{expected.P:X2}\n" +
            $"  actual   {actual}");
    }

    private static void AssertMemory(HarteCase test, byte[] ram)
    {
        foreach (var entry in test.Final.Ram)
        {
            int address = entry[0], expected = entry[1];
            Assert.True(ram[address] == expected,
                $"{test.Name}: ${address:X4} expected ${expected:X2}, got ${ram[address]:X2}.");
        }
    }

    private static void AssertCycles(HarteCase test, List<Cycle> actual)
    {
        Assert.True(test.Cycles.Length == actual.Count,
            $"{test.Name}: expected {test.Cycles.Length} cycles, got {actual.Count}.\n" +
            $"  expected {Describe(test)}\n  actual   {string.Join(" ", actual)}");

        for (var i = 0; i < test.Cycles.Length; i++)
        {
            var raw = test.Cycles[i];
            var expected = new Cycle(raw[0].GetInt32(), raw[1].GetByte(), raw[2].GetString() == "write");

            Assert.True(expected == actual[i],
                $"{test.Name}: cycle {i} expected {expected}, got {actual[i]}.\n" +
                $"  expected {Describe(test)}\n  actual   {string.Join(" ", actual)}");
        }
    }

    private static string Describe(HarteCase test) =>
        string.Join(" ", test.Cycles.Select(c =>
            $"[${c[0].GetInt32():X4}, ${c[1].GetByte():X2}, {c[2].GetString()}]"));
}
