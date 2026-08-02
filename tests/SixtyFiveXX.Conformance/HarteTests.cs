using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs every SingleStepTests vector for all 256 opcodes of one variant, checking the final
/// register file, the named RAM bytes, and the exact per-cycle bus activity.
/// </summary>
/// <typeparam name="TVariant">The core under test. Also selects which vector set to run.</typeparam>
/// <remarks>
/// Generic so each variant is a gate of its own rather than a copy of this harness. A
/// derived class supplies nothing but the variant and its expected coverage — the vector
/// set name and the opcode table both follow from <typeparamref name="TVariant"/>, so a
/// core cannot be certified against the wrong set by a copy-paste slip.
/// </remarks>
public abstract class HarteTests<TVariant>(ITestOutputHelper output)
    where TVariant : struct, ICpuVariant
{
    /// <summary>Every opcode. All 256 are tested; a variant leaving some undefined still has vectors for them.</summary>
    public static TheoryData<byte> AllOpcodes
    {
        get
        {
            var data = new TheoryData<byte>();
            for (var opcode = 0; opcode < 256; opcode++) data.Add((byte)opcode);
            return data;
        }
    }

    /// <summary>
    /// How many of the 256 opcodes this variant is expected to implement. Stated by the
    /// derived class rather than measured, so a table that silently loses entries fails
    /// instead of quietly reporting a smaller number.
    /// </summary>
    protected abstract int ExpectedImplementedOpcodes { get; }

    /// <summary>
    /// The opcode descriptors the core actually resolved, rather than a table named
    /// directly. Asserting against the resolved table means a variant wired to the wrong
    /// table cannot pass by having the test look at the right one.
    /// </summary>
    private static OpcodeInfo[] Table => MicroOpTable.For<TVariant>().Info;

    /// <summary>
    /// The SingleStepTests directory for this variant. Derived from the variant so it
    /// cannot drift from the core being run.
    /// </summary>
    private static string Set => TVariant.Variant switch
    {
        CpuVariant.Mos6502 => "6502",
        CpuVariant.Wdc65C02 => "wdc65c02",
        CpuVariant.Rockwell65C02 => "rockwell65c02",
        CpuVariant.Synertek65C02 => "synertek65c02",
        var v => throw new NotSupportedException($"No SingleStepTests set for {v}."),
    };

    [Theory]
    [MemberData(nameof(AllOpcodes))]
    public void Opcode_MatchesEveryVector(byte opcode)
    {
        var cases = HarteCache.Load(Set, opcode);
        Assert.NotEmpty(cases);

        // One 64 KB buffer and one log for the whole file. Allocating per vector would
        // mean 10,000 64 KB arrays per opcode, which dominates the suite's runtime.
        var ram = new byte[0x10000];
        var log = new List<Cycle>(16);
        Cpu<HarteBus, TVariant> cpu = new(new HarteBus(ram, log));

        // A JAM opcode never reaches an instruction boundary, so Step() cannot drive it.
        // Read from the resolved table rather than assuming: the NMOS core has twelve, and
        // no 65C02 has any.
        var jams = Table[opcode].Operation == Op.Jam;

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

            if (jams)
            {
                // Tick exactly as many cycles as the vector records instead.
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
            // from a working CPU. Only JAM opcodes ever take this path.
            if (cpu.IsJammed) cpu = new Cpu<HarteBus, TVariant>(new HarteBus(ram, log));
        }

        output.WriteLine($"{Set} ${opcode:X2} {Table[opcode].Mnemonic}: {cases.Length} vectors passed.");
    }

    /// <summary>
    /// Records how much of the opcode space this variant covers, so a green suite is
    /// never mistaken for complete coverage.
    /// </summary>
    [Fact]
    public void Coverage_IsReportedHonestly()
    {
        var implemented = Table.Count(e => e.Operation != Op.Undefined);

        output.WriteLine($"{Set}: {256 * 10_000:N0} vectors across 256 opcodes.");
        output.WriteLine($"{implemented} of 256 opcodes are implemented.");

        Assert.Equal(ExpectedImplementedOpcodes, implemented);
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

/// <summary>The NMOS 6502 against the <c>6502</c> vector set. Certified since phase 2a.</summary>
public class Harte6502Tests(ITestOutputHelper output) : HarteTests<Mos6502Variant>(output)
{
    /// <inheritdoc />
    protected override int ExpectedImplementedOpcodes => 256;
}
