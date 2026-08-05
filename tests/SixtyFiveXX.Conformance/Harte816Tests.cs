using System.Text.Json;
using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Runs the SingleStepTests <c>65816</c> vectors for the opcodes this phase has wired a real
/// micro-op sequence for.
/// </summary>
/// <remarks>
/// A sibling to <see cref="HarteTests{TVariant}"/>, not an extension of it: the 65816 vector
/// set has a different JSON shape (research document §2.3), a 24-bit address space, and an
/// eight-character per-cycle pin string the 8-bit sets have nothing like — see
/// <see cref="Harte816Case"/> and <see cref="Harte816Bus"/>.
/// <para>
/// Unlike <see cref="HarteTests{TVariant}"/>, this does not loop over all 256 opcodes. Only 128
/// of the 65816's opcodes are defined at all yet (<c>Opcodes65C816.Table</c>) — every one of
/// them has a real sequence: <c>XCE</c>, <c>REP</c>, <c>SEP</c>, all fifteen addressing forms
/// of <c>LDA</c>, <c>ORA</c>, <c>AND</c>, <c>EOR</c>, <c>CMP</c>, <c>ADC</c> and <c>SBC</c> plus
/// <c>STA</c>'s fourteen, and three each of <c>CPX</c> and <c>CPY</c>. Looping over the full
/// opcode space the way the 8-bit harness does would still require declaring 128 "not yet
/// covered" opcodes as a matter of routine, which is what the 8-bit harness's
/// <c>OpcodesWithoutVectors</c> mechanism exists to flag as an exception, not a norm.
/// </para>
/// </remarks>
public class Harte816Tests(ITestOutputHelper output)
{
    /// <summary>
    /// How many opcodes <c>MicroOpTable.Emit816</c> has a real sequence for. Declared rather
    /// than measured here, so a sequence that regresses to empty — deleted or forgotten in
    /// tasks 5-6 — fails <see cref="ImplementedOpcodes_MatchesDeclaredCount"/> instead of just
    /// quietly running a smaller, still-green suite. The same protection
    /// <c>HarteTests{TVariant}.ExpectedImplementedOpcodes</c> gives the 8-bit cores. XCE was
    /// the only one Task 3 landed (research document §9, row 19a); Task 4 added REP and SEP
    /// (§9's "Immediate, and REP/SEP"); Task 5 added LDA and STA's seven direct-page forms each
    /// (research document §9's "Direct", "Direct,X", "(Direct,X)", "(Direct)", "(Direct),Y",
    /// "[Direct]" and "[Direct],Y" blocks) — 3 + 14 = 17. Task 6 adds LDA and STA's remaining
    /// seven forms each — absolute, absolute,X, absolute,Y, long, long,X, stack,S,
    /// (stack,S),Y (§9's "Absolute", "Absolute,X — row 6a, and Absolute,Y — row 7", "Absolute
    /// Long — row 4a, and Absolute Long,X — row 5", "Stack Relative — row 23" and "(Stack
    /// Relative),Y — row 24" blocks) — plus LDA's immediate form (§9's "Immediate, and
    /// REP/SEP"; STA has none) — 17 + 14 + 1 = 32, all 32 opcodes phase 7b is gated on. Phase 7c
    /// task 3 adds <c>ORA</c>, <c>AND</c> and <c>EOR</c> in all fifteen addressing forms each,
    /// every one of them reusing an addressing sequence phase 7b already certified — 32 + 45 = 77.
    /// Task 4 adds <c>CMP</c> in all fifteen, and <c>CPX</c> and <c>CPY</c> in the three forms
    /// each the 65816 gives them — immediate, direct page and absolute — 77 + 21 = 98. Task 5
    /// adds <c>ADC</c> and <c>SBC</c> in all fifteen forms each, the first 65816 opcodes with a
    /// decimal mode — and no extra cycle for it, so they reuse the same read tails again
    /// (research document §12.5) — 98 + 30 = 128. <c>$EB</c> is not among them: on this part it
    /// is <c>XBA</c>, not the NMOS 6502's undocumented <c>SBC</c> alias.
    /// </summary>
    private static readonly int ExpectedImplementedOpcodes = 128;

    /// <summary>
    /// Opcode bytes this phase has emitted a real <c>MicroOpTable.Emit816</c> sequence for —
    /// measured from the resolved table rather than a hand-maintained literal, so an opcode
    /// whose sequence is forgotten or deleted cannot silently vanish from the theory data the
    /// way a literal list would let it. An opcode counts as implemented when
    /// <c>MicroOpTable.SequenceLength</c> is nonzero: every 65816 opcode without a real
    /// sequence yet emits nothing and ends after its fetch cycle, per <c>Emit816</c>'s own
    /// remarks, so <c>SequenceLength</c> is 0 for it.
    /// </summary>
    public static TheoryData<byte> ImplementedOpcodes
    {
        get
        {
            var data = new TheoryData<byte>();
            foreach (var opcode in ImplementedOpcodeBytes()) data.Add(opcode);
            return data;
        }
    }

    private static byte[] ImplementedOpcodeBytes()
    {
        var table = MicroOpTable.For<W65C816Variant>();
        var opcodes = new List<byte>();
        for (var opcode = 0; opcode < 256; opcode++)
            if (table.SequenceLength(opcode) > 0) opcodes.Add((byte)opcode);
        return opcodes.ToArray();
    }

    /// <summary>
    /// Guards the exact failure this measured derivation exists to prevent: if a sequence is
    /// forgotten or deleted in tasks 4-6, the measured count drops below the declared one and
    /// this fails loudly, rather than <see cref="ImplementedOpcodes"/> just running — and
    /// reporting green over — fewer opcodes.
    /// </summary>
    [Fact]
    public void ImplementedOpcodes_MatchesDeclaredCount() =>
        Assert.Equal(ExpectedImplementedOpcodes, ImplementedOpcodeBytes().Length);

    [Theory]
    [MemberData(nameof(ImplementedOpcodes))]
    public void Opcode_MatchesEveryVector_EmulationMode(byte opcode) => RunMode(opcode, 'e');

    [Theory]
    [MemberData(nameof(ImplementedOpcodes))]
    public void Opcode_MatchesEveryVector_NativeMode(byte opcode) => RunMode(opcode, 'n');

    private void RunMode(byte opcode, char mode)
    {
        var cases = Harte816Cache.Load(opcode, mode);
        Assert.True(cases.Length > 0, $"65816 ${opcode:X2}.{mode}: no vectors.");

        var mnemonic = Opcodes65C816.Table[opcode].Mnemonic;

        // One RAM dictionary and one log for the whole file, cleared per vector — the same
        // shape HarteTests uses its 64 KB array and log for, so 10,000 vectors do not mean
        // 10,000 allocations.
        var ram = new Dictionary<int, byte>(64);
        var log = new List<Cycle816>(16);
        var cpu = new Cpu<Harte816Bus, W65C816Variant>(new Harte816Bus(ram, log));

        foreach (var test in cases)
        {
            ram.Clear();
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
                DBR = test.Initial.Dbr,
                PBR = test.Initial.Pbr,
                DP = test.Initial.D,
                E = test.Initial.E != 0,
            };

            // Per-cycle E/M/X and pins are sampled here, rather than read back once after
            // Step() the way HarteTests does for the 8-bit cores: the eight-character pin
            // string needs the flags as they stood BEFORE each cycle's own Execute() ran —
            // see BuildPinString — and Cpu.LastPins is overwritten by the next Tick(), so
            // both have to be captured cycle by cycle rather than reconstructed afterwards.
            var preFlags = new (bool E, bool M, bool X)[test.Cycles.Length];
            var pins = new BusPins[test.Cycles.Length];

            for (var i = 0; i < test.Cycles.Length; i++)
            {
                preFlags[i] = (cpu.State.E, cpu.State.M, cpu.State.XFlag);
                cpu.Tick();
                pins[i] = cpu.LastPins;
            }

            Assert.True(cpu.AtInstructionBoundary,
                $"{test.Name}: instruction did not finish within the vector's " +
                $"{test.Cycles.Length} cycles.");

            AssertRegisters(test, cpu.State);
            AssertMemory(test, ram);
            AssertCycles(test, log, pins, preFlags);
        }

        output.WriteLine($"65816 ${opcode:X2}.{mode} {mnemonic}: {cases.Length} vectors passed.");
    }

    private static void AssertRegisters(Harte816Case test, in CpuState actual)
    {
        var expected = test.Final;
        var same = actual.PC == expected.Pc && actual.S == expected.S && actual.A == expected.A &&
                   actual.X == expected.X && actual.Y == expected.Y && actual.P == expected.P &&
                   actual.DBR == expected.Dbr && actual.PBR == expected.Pbr && actual.DP == expected.D &&
                   actual.E == (expected.E != 0);

        Assert.True(same,
            $"{test.Name}: registers diverged.\n" +
            $"  expected PC:{expected.Pc:X4} A:{expected.A:X4} X:{expected.X:X4} Y:{expected.Y:X4} " +
            $"S:{expected.S:X4} P:{expected.P:X2} DBR:{expected.Dbr:X2} PBR:{expected.Pbr:X2} " +
            $"D:{expected.D:X4} E:{expected.E}\n" +
            $"  actual   {actual}");
    }

    private static void AssertMemory(Harte816Case test, Dictionary<int, byte> ram)
    {
        foreach (var entry in test.Final.Ram)
        {
            int address = entry[0], expected = entry[1];
            ram.TryGetValue(address, out var actual);
            Assert.True(actual == expected,
                $"{test.Name}: ${address:X6} expected ${expected:X2}, got ${actual:X2}.");
        }
    }

    private static void AssertCycles(
        Harte816Case test, List<Cycle816> actual, BusPins[] pins, (bool E, bool M, bool X)[] preFlags)
    {
        Assert.True(test.Cycles.Length == actual.Count,
            $"{test.Name}: expected {test.Cycles.Length} cycles, got {actual.Count}.");

        for (var i = 0; i < test.Cycles.Length; i++)
        {
            var raw = test.Cycles[i];
            var expectedAddress = raw[0].GetInt32();
            byte? expectedValue = raw[1].ValueKind == JsonValueKind.Null ? null : raw[1].GetByte();
            var expectedPinString = raw[2].GetString()!;

            var cycle = actual[i];
            var actualPinString = BuildPinString(pins[i], cycle.Kind, preFlags[i]);

            var matches = cycle.Address == expectedAddress && cycle.Value == expectedValue &&
                          actualPinString == expectedPinString;

            Assert.True(matches,
                $"{test.Name}: cycle {i} expected " +
                $"[${expectedAddress:X6}, {(expectedValue is { } v ? $"${v:X2}" : "null")}, " +
                $"\"{expectedPinString}\"], got [${cycle.Address:X6}, " +
                $"{(cycle.Value is { } av ? $"${av:X2}" : "null")}, \"{actualPinString}\"].");
        }
    }

    /// <summary>
    /// Assembles the eight-character pin string research document §2.3 defines — <c>d p v r e
    /// m x l</c> — from <see cref="Cpu{TBus,TVariant}.LastPins"/> (VDA/VPA/VPB/MLB), the
    /// cycle's own read/write direction (RWB, never absent, even on an internal cycle), and
    /// E/M/X sampled from <see cref="CpuState"/> BEFORE the cycle ran.
    /// </summary>
    /// <remarks>
    /// The "before" part is load-bearing, not incidental. <c>XCE</c>'s own internal cycle is
    /// the one that flips <c>E</c> and, when the result is emulation mode, forces <c>M</c> and
    /// <c>X</c> — and the vectors show that cycle's pin string carrying the OLD values, not the
    /// ones the operation just produced: confirmed against SingleStepTests/65816's
    /// <c>fb.e.json</c> vector 1, whose cycle 2 pin string still shows <c>e</c> set even though
    /// the vector's final state has <c>E</c> clear, and <c>fb.n.json</c> vector 4, whose cycle
    /// 2 pin string still shows <c>m</c>/<c>x</c> clear even though the final state forces both
    /// to 1. This matches how <c>Cpu.Tick</c> itself samples <c>LastPins</c> — at the top of
    /// the cycle, before <c>Execute</c> runs — so the harness reads the flags the same way.
    /// </remarks>
    private static string BuildPinString(BusPins pins, Cycle816Kind kind, (bool E, bool M, bool X) flags)
    {
        Span<char> s = stackalloc char[8];
        s[0] = (pins & BusPins.Vda) != 0 ? 'd' : '-';
        s[1] = (pins & BusPins.Vpa) != 0 ? 'p' : '-';
        s[2] = (pins & BusPins.Vpb) != 0 ? 'v' : '-';
        s[3] = kind == Cycle816Kind.Write ? 'w' : 'r';
        s[4] = flags.E ? 'e' : '-';
        s[5] = flags.M ? 'm' : '-';
        s[6] = flags.X ? 'x' : '-';
        s[7] = (pins & BusPins.Mlb) != 0 ? 'l' : '-';
        return new string(s);
    }
}
