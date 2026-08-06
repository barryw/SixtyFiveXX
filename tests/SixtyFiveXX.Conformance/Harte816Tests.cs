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
/// Unlike <see cref="HarteTests{TVariant}"/>, this does not loop over all 256 opcodes. Only 242
/// of the 65816's opcodes are defined at all yet (<c>Opcodes65C816.Table</c>) — every one of
/// them has a real sequence: <c>XCE</c>, <c>REP</c>, <c>SEP</c>, all fifteen addressing forms
/// of <c>LDA</c>, <c>ORA</c>, <c>AND</c>, <c>EOR</c>, <c>CMP</c>, <c>ADC</c> and <c>SBC</c> plus
/// <c>STA</c>'s fourteen, three each of <c>CPX</c> and <c>CPY</c>, <c>BIT</c>'s five, five each
/// of <c>LDX</c> and <c>LDY</c>, three each of <c>STX</c> and <c>STY</c>, <c>STZ</c>'s four,
/// four each of <c>ASL</c>, <c>LSR</c>, <c>ROL</c>, <c>ROR</c>, <c>INC</c> and <c>DEC</c> on
/// memory, two each of <c>TSB</c> and <c>TRB</c>, one each of <c>ASL</c>, <c>LSR</c>,
/// <c>ROL</c>, <c>ROR</c>, <c>INC</c> and <c>DEC</c> on the accumulator, the twelve transfers,
/// <c>XBA</c>, the seven flag instructions, <c>INX</c>/<c>INY</c>/<c>DEX</c>/<c>DEY</c>,
/// <c>NOP</c>, the seven pushes and six pulls, <c>BRK</c>/<c>COP</c>/<c>WDM</c>, the two block
/// moves <c>MVN</c>/<c>MVP</c>, the two halts <c>WAI</c>/<c>STP</c>, and the ten branches
/// <c>BPL</c>/<c>BMI</c>/<c>BVC</c>/<c>BVS</c>/<c>BCC</c>/<c>BCS</c>/<c>BNE</c>/<c>BEQ</c>,
/// <c>BRA</c> and <c>BRL</c>. Looping over
/// the full opcode space the way the 8-bit harness does would still require declaring the
/// remaining "not yet covered" opcodes as a matter of routine, which is what the 8-bit harness's
/// <c>OpcodesWithoutVectors</c> mechanism exists to flag as an exception, not a norm.
/// </para>
/// </remarks>
public class Harte816Tests(ITestOutputHelper output)
{
    /// <summary>
    /// How many opcodes <c>MicroOpTable.Emit816</c> has a real sequence for. Declared rather
    /// than measured here, so a sequence that regresses to empty — deleted or forgotten
    /// somewhere along the way — fails <see cref="ImplementedOpcodes_MatchesDeclaredCount"/>
    /// instead of just quietly running a smaller, still-green suite. The same protection
    /// <c>HarteTests{TVariant}.ExpectedImplementedOpcodes</c> gives the 8-bit cores. XCE was
    /// the only one phase 7b task 3 landed (research document §9, row 19a); phase 7b task 4
    /// added REP and SEP (§9's "Immediate, and REP/SEP"); phase 7b task 5 added LDA and STA's
    /// seven direct-page forms each (research document §9's "Direct", "Direct,X",
    /// "(Direct,X)", "(Direct)", "(Direct),Y", "[Direct]" and "[Direct],Y" blocks) — 3 + 14 =
    /// 17. Phase 7b task 6 adds LDA and STA's remaining seven forms each — absolute,
    /// absolute,X, absolute,Y, long, long,X, stack,S, (stack,S),Y (§9's "Absolute",
    /// "Absolute,X — row 6a, and Absolute,Y — row 7", "Absolute Long — row 4a, and Absolute
    /// Long,X — row 5", "Stack Relative — row 23" and "(Stack Relative),Y — row 24" blocks) —
    /// plus LDA's immediate form (§9's "Immediate, and REP/SEP"; STA has none) — 17 + 14 + 1 =
    /// 32, all 32 opcodes phase 7b is gated on. Phase 7c task 3 adds <c>ORA</c>, <c>AND</c> and
    /// <c>EOR</c> in all fifteen addressing forms each, every one of them reusing an addressing
    /// sequence phase 7b already certified — 32 + 45 = 77. Phase 7c task 4 adds <c>CMP</c> in
    /// all fifteen, and <c>CPX</c> and <c>CPY</c> in the three forms each the 65816 gives them
    /// — immediate, direct page and absolute — 77 + 21 = 98. Phase 7c task 5 adds <c>ADC</c>
    /// and <c>SBC</c> in all fifteen forms each, the first 65816 opcodes with a decimal mode —
    /// and no extra cycle for it, so they reuse the same read tails again (research document
    /// §12.5) — 98 + 30 = 128. <c>$EB</c> is not among them: on this part it is <c>XBA</c>, not
    /// the NMOS 6502's undocumented <c>SBC</c> alias. Phase 7c task 6 adds <c>BIT</c>'s five
    /// opcodes — immediate plus its four other addressing forms — 128 + 5 = 133. <c>$89</c>
    /// takes <c>Op.BitImm</c>, which sets Z alone; the other four take <c>Op.Bit</c>, which also
    /// takes N and V from the operand's top two bits. Phase 7c task 7 adds <c>LDX</c> and
    /// <c>LDY</c> in five forms each, <c>STX</c> and <c>STY</c> in three each, and <c>STZ</c> in
    /// four — 133 + 20 = 153 — along with the one addressing mode this phase adds, <c>dp,Y</c>,
    /// which <c>LDX</c> and <c>STX</c> use and no other instruction on the part does. Phase 7c′
    /// task 2 adds <c>ASL</c>, <c>LSR</c>, <c>ROL</c> and <c>ROR</c> in <c>dp</c>, <c>dp,X</c>,
    /// <c>abs</c> and <c>abs,X</c> each — 153 + 16 = 169 — the first read-modify-writes this
    /// emitter has produced, and with them the run-time <c>E</c> branch of datasheet Note 17
    /// (research document §13.1). Phase 7c′ task 3 adds <c>INC</c> and <c>DEC</c> on memory in
    /// <c>dp</c>, <c>dp,X</c>, <c>abs</c> and <c>abs,X</c> each, plus <c>TSB</c> and <c>TRB</c>
    /// in <c>dp</c> and <c>abs</c> each — 169 + 12 = 181. Phase 7c′ task 4 adds <c>ASL</c>,
    /// <c>LSR</c>, <c>ROL</c>, <c>ROR</c>, <c>INC</c> and <c>DEC</c> on the accumulator, the
    /// first opcodes here with no operand at all — 181 + 6 = 187. Phase 7c′ task 5 adds the
    /// twelve transfers and <c>XBA</c> — 187 + 13 = 200 — sized by the destination register
    /// rather than the source (research document §13.4). Phase 7c′ task 6, the last opcode task
    /// of the phase, adds the seven flag instructions (<c>CLC</c>, <c>SEC</c>, <c>CLI</c>,
    /// <c>SEI</c>, <c>CLV</c>, <c>CLD</c>, <c>SED</c>), <c>INX</c>/<c>INY</c>/<c>DEX</c>/<c>DEY</c>
    /// and <c>NOP</c> — 200 + 12 = 212. The flag instructions and <c>NOP</c> touch no
    /// width-dependent register; the index increments are sized by <c>x</c>, the same
    /// implied-mode shape task 5's accumulator forms use. Phase 7d task 2 adds the seven pushes
    /// (<c>PHA</c>, <c>PHP</c>, <c>PHX</c>, <c>PHY</c>, <c>PHB</c>, <c>PHD</c>, <c>PHK</c>) and
    /// the six pulls (<c>PLA</c>, <c>PLP</c>, <c>PLX</c>, <c>PLY</c>, <c>PLB</c>, <c>PLD</c>) —
    /// 212 + 13 = 225 — the first opcodes here routed through
    /// <c>MicroOpTable.EmitControlFlow816</c> rather than <c>EmitAddressed816</c>, and the first
    /// to drive the 65816's own stack address (research document §14.1) instead of the eight-bit
    /// cores' <c>$0100 + SL</c>. Phase 7d task 3 adds <c>BRK</c> (<c>$00</c>), <c>COP</c>
    /// (<c>$02</c>) and <c>WDM</c> (<c>$42</c>) — 225 + 3 = 228 — the first opcodes here to
    /// assert <c>VPB</c>, to push the program bank, and to choose between two vector sets
    /// (research document §14.2). <c>WDM</c> is none of those: it is a reserved two-byte,
    /// two-cycle no-operation whose second byte is never read, and it is in this task only
    /// because <c>$42</c> sits between the two interrupts in every table that lists them.
    /// The task's other two sequences — <c>IRQ</c> and <c>NMI</c> — have <b>no vectors at
    /// all</b> and are certified by <c>W65C816InterruptTests</c> instead; §14.2's gap 1
    /// records why. Phase 7d task 4 adds the two block moves, <c>MVN</c> (<c>$54</c>) and
    /// <c>MVP</c> (<c>$44</c>) — 228 + 2 = 230 — the only instructions in this engine that move
    /// <c>PC</c> backwards, and the only ones whose vectors are truncated mid-instruction; see
    /// <see cref="VectorsTruncatedMidInstruction"/>. Phase 7d task 5 adds the two halts,
    /// <c>WAI</c> (<c>$CB</c>) and <c>STP</c> (<c>$DB</c>) — 230 + 2 = 232 — whose vectors cover
    /// their three executed cycles and nothing else: research document §14.4 measured all 40,000
    /// ending in a <c>[null, null, "--------"]</c> sentinel, with the hold, the wake on
    /// <c>IRQB</c>/<c>NMIB</c>, <c>WAI</c>'s <c>i</c>-flag special case and <c>STP</c>'s
    /// reset-only exit absent from the vector set entirely. <c>WaiStpTests</c> is the only
    /// certification those four rules get. Phase 7d task 6 adds the ten branches — the eight
    /// conditional ones, <c>BRA</c> (<c>$80</c>) and <c>BRL</c> (<c>$82</c>) — 232 + 10 = 242.
    /// Research document §14.5, Table 5-7 rows 20 and 21. Their 200,000 vectors settle the
    /// question the sequence hangs on: <c>10.n</c> contains only two- and three-cycle vectors and
    /// <c>80.n</c> only three-cycle ones, while <c>10.e</c> and <c>80.e</c> both contain
    /// four-cycle ones — the taken-branch page-cross cycle is emulation-mode-only, exactly as
    /// datasheet Note 6 says. <c>82</c> is a flat four cycles in both modes. They also cover the
    /// bank boundary, which was expected to be a gap and measured not to be: 5,078 of the 200,000
    /// — 101 <c>rel8</c> and 4,977 <c>rel16</c> — have a destination outside
    /// <c>$0000</c>-<c>$FFFF</c> before the wrap, and every one records the wrapped <c>PC</c>
    /// with <c>PBR</c> unchanged. <c>W65C816ControlFlowTests</c> pins the same rule at the
    /// specific addresses Clark §4 works through. Phase 7d task 7, the last structural task of
    /// the phase, adds the five jumps (<c>$4C</c>, <c>$6C</c>, <c>$7C</c>, <c>$5C</c>,
    /// <c>$DC</c>), the three calls (<c>$20</c>, <c>$FC</c>, <c>$22</c>) and the three returns
    /// (<c>$40</c>, <c>$60</c>, <c>$6B</c>) — 242 + 11 = 253. Research document §14.6, Table 5-7
    /// rows 1b, 3b, 2a, 4b, 3a, 1c, 2b, 4c, 22g, 22h and 22i. Their 220,000 vectors arbitrate
    /// three shapes no cycle count would have caught: <c>JSR (abs,X)</c> pushes at cycles 3 and
    /// 4, before it has finished fetching its operand; <c>JSL</c> pushes the <em>old</em> program
    /// bank at cycle 4, two cycles before it reads the new one; and <c>RTI</c> pulls <c>P</c>
    /// first and the program bank last, the latter in native mode only — the one opcode of the
    /// eleven whose cycle count depends on <c>e</c>.
    /// </summary>
    private static readonly int ExpectedImplementedOpcodes = 253;

    /// <summary>
    /// Opcodes whose vectors' final entry is not an instruction boundary, for either of the two
    /// reasons this core has one.
    /// <para>
    /// <b>The block moves (<c>$54</c>, <c>$44</c>)</b> are truncated at 100 cycles with a final
    /// state part-way through the move — research document §14.3, measured from the files rather
    /// than inferred. A block move runs seven cycles per byte and moves up to 65,536 bytes, so no
    /// fixed-length vector could contain a whole one. <c>$54</c>'s cycle arrays are 9,999 of
    /// exactly 100 entries and one of 98; <c>$44</c>'s are 9,997 of 100 plus one each of 63, 28
    /// and 14. <c>54 n 1</c> begins with <c>A = $EF9B</c> — 61,340 bytes to move — and its
    /// recorded final state is fourteen bytes in.
    /// </para>
    /// <para>
    /// <b>The halts (<c>$CB</c>, <c>$DB</c>)</b> are not truncated: they are complete, and the
    /// instruction simply never ends. Research document §14.4 measured all 40,000 as four
    /// entries whose fourth is <c>[null, null, "--------"]</c> — the sentinel for "and then the
    /// processor stopped". A core that is correctly holding is by definition not at an
    /// instruction boundary, so the assertion is as inapplicable here as it is to a half-finished
    /// block move.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ONLY the instruction-boundary assertion is skipped. Every cycle's address, value and
    /// eight-character pin string is still compared, and so are the final registers and memory —
    /// against the mid-instruction state the vector actually records. For $54 that is a hundred
    /// arbitrated cycles of a real block move per vector, rewind included, across 10,000 vectors:
    /// stronger coverage than most opcodes in this core get, not weaker. For $CB and $DB it is
    /// the three cycles the datasheet prints, plus the assertion in <see cref="AssertCycles"/>
    /// that the core performed no fourth access. No vector file is excluded and no vector is
    /// skipped.
    /// </remarks>
    private static readonly HashSet<int> VectorsTruncatedMidInstruction = [0x54, 0x44, 0xCB, 0xDB];

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
    /// forgotten or deleted anywhere along the way, the measured count drops below the declared
    /// one and this fails loudly, rather than <see cref="ImplementedOpcodes"/> just running —
    /// and reporting green over — fewer opcodes.
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

            // A vector every other opcode runs leaves the core at an instruction boundary, so
            // the same core serves the whole file. The two block moves and the two halts do not:
            // their cycle arrays stop part-way through a sequence — truncated for $54/$44,
            // held forever for $CB/$DB (see VectorsTruncatedMidInstruction) — and
            // `cpu.State` assigns the architectural registers but NOT the micro-op position — so
            // without this the next vector would resume the previous vector's half-finished
            // block move instead of fetching its own opcode, and diverge in the fifth cycle.
            // Measured, not theorised: it is what $54.n vector 2 did. A fresh core is the only
            // way back to a boundary that neither adds public API nor spends cycles on the bus,
            // and it costs an allocation on 40,000 of the suite's 4.6 million vectors.
            //
            // The condition is AtInstructionBoundary — that is, `_mpc >= 0` — so it catches a
            // core left mid-sequence and nothing else. WAI and STP hold by decrementing _mpc,
            // which keeps them mid-sequence and so inside this guard.
            //
            // This comment used to go on to say that a halt implemented instead as "end the
            // instruction but leave _waiting or _stopped set" would slip past the guard and stop
            // the NEXT vector fetching at all, the same vector-2 shape as the block moves'.
            // Phase 7d task 5 PROBED that, by building exactly that halt and running the suite:
            // it is not what happens, and $CB and $DB pass all 40,000 vectors with it. Cpu.Tick
            // has no _waiting or _stopped test anywhere — the hold is expressed by _mpc and
            // nothing else, and those two fields are readback plus Step()'s loop condition. A
            // leaked flag therefore stops nothing here, and this guard is not what protects the
            // halts.
            //
            // What does is WaiStpTests: 14 of its 17 65816 cases fail against that halt, because
            // a core that ends the instruction goes on to execute the NEXT one instead of
            // holding. The constraint on how a halt may be modelled is real; the mechanism that
            // enforces it is the unit tests, not the harness.
            if (!cpu.AtInstructionBoundary)
                cpu = new Cpu<Harte816Bus, W65C816Variant>(new Harte816Bus(ram, log));

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

            if (!VectorsTruncatedMidInstruction.Contains(opcode))
            {
                Assert.True(cpu.AtInstructionBoundary,
                    $"{test.Name}: instruction did not finish within the vector's " +
                    $"{test.Cycles.Length} cycles.");
            }

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

    /// <summary>
    /// Compares the vector's cycles against the ones the core actually drove.
    /// </summary>
    /// <remarks>
    /// A trailing <c>[null, null, "--------"]</c> entry is not a bus cycle. Research document
    /// §14.4 measured every one of <c>$CB</c>'s and <c>$DB</c>'s 40,000 vectors ending in one —
    /// no address, no value, and not even a read/write character, with the <c>e</c>, <c>m</c> and
    /// <c>x</c> slots blank even in the emulation-mode files where <c>e</c> is 1. It marks "and
    /// then the processor halted", which the core expresses by performing no bus access on the
    /// held cycle (<c>MicroOp.WaiHold816</c>). So it has no counterpart in the log, and the
    /// expected access count is the entries that ARE accesses — which is itself the assertion:
    /// a hold that drove an address, or read the bus, would log a fourth access and fail here.
    /// A null anywhere other than at the end still fails, on the count or on the address.
    /// </remarks>
    private static void AssertCycles(
        Harte816Case test, List<Cycle816> actual, BusPins[] pins, (bool E, bool M, bool X)[] preFlags)
    {
        var accesses = test.Cycles.Length;
        while (accesses > 0 && test.Cycles[accesses - 1][0].ValueKind == JsonValueKind.Null) accesses--;

        Assert.True(accesses == actual.Count,
            $"{test.Name}: expected {accesses} bus cycles of the vector's " +
            $"{test.Cycles.Length}, got {actual.Count}.");

        for (var i = 0; i < accesses; i++)
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
