using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs every SingleStepTests vector for all 256 6502 opcodes, checking the final
/// register file, the named RAM bytes, and the exact per-cycle bus activity.
/// </summary>
public class Harte6502Tests(ITestOutputHelper output)
{
    /// <summary>Every opcode. All 256 are implemented as of Phase 2a.</summary>
    public static TheoryData<byte> AllOpcodes
    {
        get
        {
            var data = new TheoryData<byte>();
            for (var opcode = 0; opcode < 256; opcode++) data.Add((byte)opcode);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllOpcodes))]
    public void Opcode_MatchesEveryVector(byte opcode)
    {
        var cases = HarteCache.Load("6502", opcode);
        Assert.NotEmpty(cases);

        // One 64 KB buffer and one log for the whole file. Allocating per vector would
        // mean 10,000 64 KB arrays per opcode, which dominates the suite's runtime.
        var ram = new byte[0x10000];
        var log = new List<Cycle>(16);
        Cpu<HarteBus, Mos6502Variant> cpu = new(new HarteBus(ram, log));

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

            // A JAM opcode never reaches an instruction boundary, so Step() cannot drive
            // it. Tick exactly as many cycles as the vector records instead.
            if (Opcodes6502.Table[opcode].Operation == Op.Jam)
            {
                for (var i = 0; i < test.Cycles.Length; i++) cpu.Tick();
            }
            else
            {
                cpu.Step();
            }

            AssertRegisters(test, cpu.State);
            AssertMemory(test, ram);
            AssertCycles(test, log);

            // A jammed core never executes again. Replace it so the next vector starts
            // from a working CPU. Only the twelve JAM opcodes ever take this path.
            if (cpu.IsJammed) cpu = new Cpu<HarteBus, Mos6502Variant>(new HarteBus(ram, log));
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
        var implemented = Opcodes6502.Table.Count(e => e.Operation != Op.Undefined);

        output.WriteLine($"Phase 2a runs all 256 opcodes ({256 * 10_000:N0} vectors).");
        output.WriteLine($"{implemented} of 256 opcodes are implemented.");

        Assert.Equal(256, implemented);
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
