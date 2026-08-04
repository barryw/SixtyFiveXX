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
    /// Opcodes the vector set deliberately ships no cases for. Declared by the derived
    /// class so an absence is a stated fact rather than a silent gap.
    /// </summary>
    protected virtual IReadOnlySet<byte> OpcodesWithoutVectors => new HashSet<byte>();

    /// <summary>
    /// True when a vector cannot apply to this variant, because it assumes ordinary memory
    /// at an address the core answers itself.
    /// </summary>
    /// <remarks>
    /// Only the 6510 has any. Skipping is not a workaround: a vector that writes $01 and
    /// expects to read it back is describing a 6502, and the 6510 is <em>correct</em> to
    /// disagree. The count is reported per opcode so the coverage given up stays visible,
    /// and a floor below asserts most of each opcode's vectors still ran — a filter that
    /// silently widened would otherwise look like a green suite.
    /// </remarks>
    protected virtual bool VectorIsInapplicable(HarteCase test) => false;

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

        // Deliberate: SingleStepTests has no 6510 set anywhere, and the 6510's instruction
        // set IS the 6502's. Running the 6502 vectors against the 6510 core certifies the
        // inherited opcodes — but not every vector applies, because 12,658 of them use
        // $00/$01 as ordinary memory and on a 6510 the CPU answers those itself. See
        // VectorIsInapplicable.
        CpuVariant.Mos6510 => "6502",
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

        if (cases.Length == 0)
        {
            // Reported, never silent: an opcode with no vectors has no conformance
            // coverage at all, and that has to be visible in the run rather than inferred
            // from a green suite.
            Assert.True(OpcodesWithoutVectors.Contains(opcode),
                $"{Set} ${opcode:X2} has no vectors, and is not declared in " +
                $"{nameof(OpcodesWithoutVectors)}. Either upstream changed, or this opcode " +
                $"is silently uncovered.");

            output.WriteLine($"{Set} ${opcode:X2} {Table[opcode].Mnemonic}: NO VECTORS — not covered here.");
            return;
        }

        // One 64 KB buffer and one log for the whole file. Allocating per vector would
        // mean 10,000 64 KB arrays per opcode, which dominates the suite's runtime.
        var applicable = cases.Where(c => !VectorIsInapplicable(c)).ToArray();
        var skipped = cases.Length - applicable.Length;

        // A filter that ran away would leave nothing to test while still reporting green.
        Assert.True(applicable.Length >= cases.Length * 9 / 10,
            $"{Set} ${opcode:X2}: only {applicable.Length} of {cases.Length} vectors are " +
            $"applicable. That is far more than the ~0.5% expected; the filter is wrong.");

        var ram = new byte[0x10000];
        var log = new List<Cycle>(16);
        Cpu<HarteBus, TVariant> cpu = new(new HarteBus(ram, log));

        // A JAM opcode never reaches an instruction boundary, so Step() cannot drive it.
        // Read from the resolved table rather than assuming: the NMOS core has twelve, and
        // no 65C02 has any.
        var jams = Table[opcode].Operation == Op.Jam;

        foreach (var test in applicable)
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
            AssertCycles(test, log, UnmodellableDummyCycle(opcode, test));

            // A jammed core never executes again. Replace it so the next vector starts
            // from a working CPU. Only JAM opcodes ever take this path.
            if (cpu.IsJammed) cpu = new Cpu<HarteBus, TVariant>(new HarteBus(ram, log));
        }

        output.WriteLine($"{Set} ${opcode:X2} {Table[opcode].Mnemonic}: {applicable.Length} vectors passed" +
                         (skipped > 0 ? $", {skipped} skipped as inapplicable." : "."));
    }

    /// <summary>
    /// Records how much of the opcode space this variant covers, so a green suite is
    /// never mistaken for complete coverage.
    /// </summary>
    [Fact]
    public void Coverage_IsReportedHonestly()
    {
        var implemented = Table.Count(e => e.Operation != Op.Undefined);
        var uncovered = OpcodesWithoutVectors.Order().ToArray();

        output.WriteLine($"{Set}: {(256 - uncovered.Length) * 10_000:N0} vectors across " +
                         $"{256 - uncovered.Length} opcodes.");
        output.WriteLine($"{implemented} of 256 opcodes are implemented.");

        if (uncovered.Length > 0)
            output.WriteLine($"NOT COVERED by this suite: " +
                             $"{string.Join(", ", uncovered.Select(o => $"${o:X2} {Table[o].Mnemonic}"))}.");

        Assert.Equal(ExpectedImplementedOpcodes, implemented);
    }

    /// <summary>
    /// Fails if an opcode declared as having no vectors turns out to have some.
    /// </summary>
    /// <remarks>
    /// The declaration is a claim about upstream, and upstream can change. Without this,
    /// vectors added later would be skipped forever and the suite would stay green while
    /// quietly testing less than it could.
    /// </remarks>
    [Fact]
    public void DeclaredUncoveredOpcodes_ReallyHaveNoVectors()
    {
        foreach (var opcode in OpcodesWithoutVectors)
            Assert.True(HarteCache.Load(Set, opcode).Length == 0,
                $"{Set} ${opcode:X2} now has vectors upstream. Remove it from " +
                $"{nameof(OpcodesWithoutVectors)} so they are actually run.");
    }

    private static void AssertRegisters(HarteCase test, in CpuState actual)
    {
        var expected = test.Final;
        var same = actual.PC == expected.Pc && actual.S == expected.S && actual.A == expected.A &&
                   actual.X == expected.X && actual.Y == expected.Y && actual.P == expected.P;

        Assert.True(same,
            $"{test.Name}: registers diverged.\n" +
            $"  expected PC:{expected.Pc:X4} A:{expected.A:X4} X:{expected.X:X4} " +
            $"Y:{expected.Y:X4} S:{expected.S:X4} P:{expected.P:X2}\n" +
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

    /// <summary>
    /// The index of a cycle whose <em>address</em> the vectors record but no core can
    /// derive, or -1 when every cycle is checkable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CMOS <c>ADC #imm</c> and <c>SBC #imm</c> spend an extra cycle in decimal mode. For
    /// every memory addressing mode that cycle repeats the effective-address read, which is
    /// modellable and is asserted normally — <c>$6D</c>'s last two cycles share an address
    /// in 5048 of 5048 decimal vectors. Immediate mode has no effective address, and the
    /// vectors instead record a <strong>fixed per-opcode constant</strong>: <c>$0056</c> for
    /// every one of <c>$69</c>'s 4,991 decimal vectors and <c>$0000</c> for every one of
    /// <c>$E9</c>'s 4,988, regardless of PC, A, X, Y or S. It is seeded into each vector's
    /// initial RAM, so it is deliberate on the generator's part rather than incidental —
    /// but it reflects that generator's internal address latch, not anything the
    /// architecture defines.
    /// </para>
    /// <para>
    /// Reproducing it would mean hard-coding two magic addresses into the core, which is
    /// modelling the test generator rather than the CPU. So this one cycle's address and
    /// value are skipped, and everything else about these vectors is still asserted in
    /// full: registers, memory, the total cycle count, this cycle's read/write direction,
    /// and every other cycle exactly. No vector is excluded.
    /// </para>
    /// </remarks>
    private static int UnmodellableDummyCycle(byte opcode, HarteCase test)
    {
        if (opcode is not (0x69 or 0xE9)) return -1;
        if (TVariant.Variant == CpuVariant.Mos6502) return -1;

        const byte Decimal = 0x08;
        return (test.Initial.P & Decimal) != 0 ? test.Cycles.Length - 1 : -1;
    }

    private static void AssertCycles(HarteCase test, List<Cycle> actual, int addressExemptCycle)
    {
        Assert.True(test.Cycles.Length == actual.Count,
            $"{test.Name}: expected {test.Cycles.Length} cycles, got {actual.Count}.\n" +
            $"  expected {Describe(test)}\n  actual   {string.Join(" ", actual)}");

        for (var i = 0; i < test.Cycles.Length; i++)
        {
            var raw = test.Cycles[i];
            var expected = new Cycle(raw[0].GetInt32(), raw[1].GetByte(), raw[2].GetString() == "write");

            if (i == addressExemptCycle)
            {
                // Direction is still checked — only the address and the value read from it
                // are generator-specific. See UnmodellableDummyCycle.
                Assert.True(expected.IsWrite == actual[i].IsWrite,
                    $"{test.Name}: cycle {i} direction expected {expected}, got {actual[i]}.");
                continue;
            }

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

/// <summary>
/// The WDC 65C02 against the <c>wdc65c02</c> vector set.
/// </summary>
/// <remarks>
/// Upstream ships empty files for <c>$CB</c> and <c>$DB</c>: <c>WAI</c> and <c>STP</c> halt,
/// so they cannot be expressed as a before-and-after vector for a single instruction. They
/// are declared uncovered here and carried entirely by unit tests.
/// </remarks>
public class HarteWdc65C02Tests(ITestOutputHelper output) : HarteTests<Wdc65C02Variant>(output)
{
    /// <inheritdoc />
    protected override int ExpectedImplementedOpcodes => 256;

    /// <inheritdoc />
    protected override IReadOnlySet<byte> OpcodesWithoutVectors => new HashSet<byte> { 0xCB, 0xDB };
}

/// <summary>
/// The MOS 6510 against the <c>6502</c> vector set.
/// </summary>
/// <remarks>
/// There is no <c>6510</c> vector set to run. This certifies that the 6510 inherits the
/// 6502's instruction set exactly — 2,560,000 vectors through a different core — and
/// nothing about the on-chip port, which has its own gate.
/// </remarks>
public class Harte6510Tests(ITestOutputHelper output) : HarteTests<Mos6510Variant>(output)
{
    /// <inheritdoc />
    protected override int ExpectedImplementedOpcodes => 256;

    /// <summary>
    /// Skips the vectors that use <c>$00</c> or <c>$01</c> as memory. On a 6510 the CPU
    /// answers both itself, so such a vector describes a 6502 and the 6510 is right to
    /// disagree — 12,658 of 2,560,000, or 0.49%, spread across 185 opcodes.
    /// </summary>
    protected override bool VectorIsInapplicable(HarteCase test) =>
        test.Cycles.Any(c => c[0].GetInt32() <= 1) ||
        test.Initial.Ram.Any(entry => entry[0] <= 1) ||
        test.Final.Ram.Any(entry => entry[0] <= 1);
}

/// <summary>The Rockwell 65C02 against the <c>rockwell65c02</c> vector set.</summary>
public class HarteRockwell65C02Tests(ITestOutputHelper output) : HarteTests<Rockwell65C02Variant>(output)
{
    /// <inheritdoc />
    protected override int ExpectedImplementedOpcodes => 256;
}

/// <summary>The Synertek 65C02 against the <c>synertek65c02</c> vector set.</summary>
public class HarteSynertek65C02Tests(ITestOutputHelper output) : HarteTests<Synertek65C02Variant>(output)
{
    /// <inheritdoc />
    protected override int ExpectedImplementedOpcodes => 256;
}
