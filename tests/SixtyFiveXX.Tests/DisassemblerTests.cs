using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The disassembler, mode by mode. The 64tass round-trip in the conformance suite gates
/// byte-exactness across whole images; this gates the notation itself, which a round-trip
/// cannot see — an assembler that agreed with a wrong rendering would still round-trip.
/// </summary>
public class DisassemblerTests
{
    private static Instruction Decode<TVariant>(int address, params byte[] bytes)
        where TVariant : struct, ICpuVariant
    {
        var ram = new byte[0x10000];
        for (var i = 0; i < bytes.Length; i++) ram[(address + i) & 0xFFFF] = bytes[i];
        return Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), address);
    }

    private static Instruction Decode(params byte[] bytes) => Decode<Mos6502Variant>(0x1000, bytes);

    [Theory]
    // The plain modes, one representative opcode each.
    [InlineData(new byte[] { 0xEA }, "NOP", "", 1)]                       // implied
    [InlineData(new byte[] { 0x0A }, "ASL", "A", 1)]                      // accumulator
    [InlineData(new byte[] { 0xA9, 0x0F }, "LDA", "#$0F", 2)]             // immediate
    [InlineData(new byte[] { 0xA5, 0x12 }, "LDA", "$12", 2)]              // zero page
    [InlineData(new byte[] { 0xB5, 0x12 }, "LDA", "$12,X", 2)]            // zero page,X
    [InlineData(new byte[] { 0xB6, 0x12 }, "LDX", "$12,Y", 2)]            // zero page,Y
    [InlineData(new byte[] { 0xAD, 0x34, 0x12 }, "LDA", "$1234", 3)]      // absolute
    [InlineData(new byte[] { 0xBD, 0x34, 0x12 }, "LDA", "$1234,X", 3)]    // absolute,X
    [InlineData(new byte[] { 0xB9, 0x34, 0x12 }, "LDA", "$1234,Y", 3)]    // absolute,Y
    [InlineData(new byte[] { 0x6C, 0x34, 0x12 }, "JMP", "($1234)", 3)]    // indirect
    [InlineData(new byte[] { 0xA1, 0x12 }, "LDA", "($12,X)", 2)]          // indexed indirect
    [InlineData(new byte[] { 0xB1, 0x12 }, "LDA", "($12),Y", 2)]          // indirect indexed
    public void EachMode_RendersInTheUsualNotation(byte[] bytes, string mnemonic, string operand, int length)
    {
        var decoded = Decode(bytes);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal(operand, decoded.Operand);
        Assert.Equal(length, decoded.Length);
    }

    [Theory]
    // AddrMode.Stack is the one mode that fixes no length: pushes and pulls are one byte,
    // BRK is two, and the absolute JMP and JSR are three.
    [InlineData(new byte[] { 0x48 }, "PHA", "", 1)]
    [InlineData(new byte[] { 0x68 }, "PLA", "", 1)]
    [InlineData(new byte[] { 0x60 }, "RTS", "", 1)]
    [InlineData(new byte[] { 0x40 }, "RTI", "", 1)]
    [InlineData(new byte[] { 0x00, 0x12 }, "BRK", "#$12", 2)]
    [InlineData(new byte[] { 0x4C, 0x34, 0x12 }, "JMP", "$1234", 3)]
    [InlineData(new byte[] { 0x20, 0x34, 0x12 }, "JSR", "$1234", 3)]
    public void StackMode_TakesItsLengthFromTheOperation(byte[] bytes, string mnemonic, string operand, int length)
    {
        var decoded = Decode(bytes);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal(operand, decoded.Operand);
        Assert.Equal(length, decoded.Length);
    }

    /// <summary>
    /// BRK's second byte is fetched and discarded rather than executed, so a caller walking
    /// memory by <c>Length</c> has to step over it exactly as the processor does. Decoding
    /// it as one byte would put every subsequent instruction out of phase.
    /// </summary>
    [Fact]
    public void Brk_ConsumesItsSignatureByte()
    {
        Assert.Equal(2, Decode(0x00, 0x12).Length);
    }

    [Theory]
    // The displacement is measured from the byte after the branch, so $00 lands on the next
    // instruction, $7F is the furthest forward and $80 the furthest back.
    [InlineData(0x1000, 0x00, 0x1002)]
    [InlineData(0x1000, 0x7F, 0x1081)]
    [InlineData(0x1000, 0x80, 0x0F82)]
    [InlineData(0x1000, 0xFE, 0x1000)]   // branch to self
    [InlineData(0x1000, 0xFD, 0x0FFF)]
    public void Relative_ShowsTheTargetRatherThanTheDisplacement(int at, byte displacement, int target)
    {
        var decoded = Decode<Mos6502Variant>(at, 0xD0, displacement);   // BNE

        Assert.Equal("BNE", decoded.Mnemonic);
        Assert.Equal($"${target:X4}", decoded.Operand);
        Assert.Equal(2, decoded.Length);
    }

    [Theory]
    // A branch near the top of memory wraps, because the program counter does.
    [InlineData(0xFFF0, 0x7F, 0x0071)]
    [InlineData(0x0000, 0x80, 0xFF82)]
    public void Relative_WrapsAtTheEndsOfMemory(int at, byte displacement, int target)
    {
        Assert.Equal($"${target:X4}", Decode<Mos6502Variant>(at, 0xD0, displacement).Operand);
    }

    /// <summary>
    /// An opcode at $FFFF takes its operand from $0000 onwards, because that is where the
    /// processor fetches it from.
    /// </summary>
    [Fact]
    public void OperandFetches_WrapAtTheTopOfMemory()
    {
        var ram = new byte[0x10000];
        ram[0xFFFF] = 0xAD;             // LDA absolute
        ram[0x0000] = 0x34;
        ram[0x0001] = 0x12;

        var decoded = Disassembler.Decode<FlatBus, Mos6502Variant>(new FlatBus(ram), 0xFFFF);

        Assert.Equal("$1234", decoded.Operand);
    }

    [Fact]
    public void Jam_IsOneByteAndNamed()
    {
        var decoded = Decode(0x02);

        Assert.Equal("JAM", decoded.Mnemonic);
        Assert.Equal(1, decoded.Length);
    }

    /// <summary>
    /// The same byte is a different instruction on a different core, which is the whole
    /// reason the variant is a type parameter. $07 is the undocumented SLO on NMOS and
    /// Rockwell's RMB0 on the parts that have it.
    /// </summary>
    [Fact]
    public void TheSameByte_DecodesPerVariant()
    {
        Assert.Equal("SLO", Decode<Mos6502Variant>(0x1000, 0x07, 0x12).Mnemonic);
        Assert.Equal("SLO", Decode<Mos6510Variant>(0x1000, 0x07, 0x12).Mnemonic);
        Assert.Equal("RMB0", Decode<Rockwell65C02Variant>(0x1000, 0x07, 0x12).Mnemonic);
        Assert.Equal("RMB0", Decode<Wdc65C02Variant>(0x1000, 0x07, 0x12).Mnemonic);
    }

    /// <summary>
    /// BBR and BBS carry two operands and are the only mode that both names a zero-page
    /// address and branches. The displacement is measured from the end of three bytes.
    /// </summary>
    [Theory]
    [InlineData(0x0F, "BBR0")]
    [InlineData(0x8F, "BBS0")]
    [InlineData(0x7F, "BBR7")]
    [InlineData(0xFF, "BBS7")]
    public void ZeroPageRelative_NamesTheAddressThenTheTarget(byte opcode, string mnemonic)
    {
        var decoded = Decode<Rockwell65C02Variant>(0x1000, opcode, 0x12, 0x05);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal("$12,$1008", decoded.Operand);
        Assert.Equal(3, decoded.Length);
    }

    [Fact]
    public void ZeroPageRelative_BranchesBackwards()
    {
        Assert.Equal("$12,$0FFB", Decode<Rockwell65C02Variant>(0x1000, 0x0F, 0x12, 0xF8).Operand);
    }

    /// <summary>
    /// The 65C02's CMOS additions, including the two modes the NMOS parts do not have.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x80, 0x05 }, "BRA", "$1007", 2)]
    [InlineData(new byte[] { 0x64, 0x12 }, "STZ", "$12", 2)]
    [InlineData(new byte[] { 0xB2, 0x12 }, "LDA", "($12)", 2)]
    [InlineData(new byte[] { 0x7C, 0x34, 0x12 }, "JMP", "($1234,X)", 3)]
    [InlineData(new byte[] { 0x1C, 0x34, 0x12 }, "TRB", "$1234", 3)]
    public void CmosModes_Decode(byte[] bytes, string mnemonic, string operand, int length)
    {
        var decoded = Decode<Wdc65C02Variant>(0x1000, bytes);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal(operand, decoded.Operand);
        Assert.Equal(length, decoded.Length);
    }

    [Theory]
    [InlineData(0xCB, "WAI")]
    [InlineData(0xDB, "STP")]
    public void WaiAndStp_AreWdcOnly(byte opcode, string mnemonic)
    {
        Assert.Equal(mnemonic, Decode<Wdc65C02Variant>(0x1000, opcode).Mnemonic);
        Assert.NotEqual(mnemonic, Decode<Rockwell65C02Variant>(0x1000, opcode).Mnemonic);
    }

    /// <summary>
    /// Every opcode of every variant decodes to something with a length between one and
    /// three, and a linear walk of memory therefore always terminates. An unhandled mode
    /// throws rather than returning a zero length, which would loop forever.
    /// </summary>
    [Fact]
    public void EveryOpcode_DecodesWithAWorkableLength()
    {
        AssertAllDecode<Mos6502Variant>();
        AssertAllDecode<Mos6510Variant>();
        AssertAllDecode<Synertek65C02Variant>();
        AssertAllDecode<Rockwell65C02Variant>();
        AssertAllDecode<Wdc65C02Variant>();
    }

    private static void AssertAllDecode<TVariant>() where TVariant : struct, ICpuVariant
    {
        for (var opcode = 0; opcode < 256; opcode++)
        {
            var decoded = Decode<TVariant>(0x1000, (byte)opcode, 0x34, 0x12);

            Assert.InRange(decoded.Length, 1, 3);
            Assert.False(string.IsNullOrWhiteSpace(decoded.Mnemonic),
                $"${opcode:X2} decoded to an empty mnemonic on {typeof(TVariant).Name}.");
        }
    }

    /// <summary>
    /// Decoding touches the instruction's own bytes and nothing else. The class documents
    /// that decoding reads the bus, so a caller with side-effecting reads can map a safe
    /// window — but only if "reads the bus" means these bytes rather than two arbitrary
    /// ones past the end of a one-byte opcode. It is also three times the reads for anyone
    /// decoding every instruction they execute, which is what a trace does.
    /// </summary>
    [Theory]
    [InlineData(0xEA, 1)]                   // NOP, implied
    [InlineData(0x0A, 1)]                   // ASL A
    [InlineData(0x48, 1)]                   // PHA, via the stack mode
    [InlineData(0x60, 1)]                   // RTS
    [InlineData(0xA9, 2)]                   // LDA immediate
    [InlineData(0xA5, 2)]                   // LDA zero page
    [InlineData(0xD0, 2)]                   // BNE, relative
    [InlineData(0x00, 2)]                   // BRK and its discarded byte
    [InlineData(0xAD, 3)]                   // LDA absolute
    [InlineData(0x20, 3)]                   // JSR
    public void Decode_ReadsExactlyTheInstructionsOwnBytes(byte opcode, int expected)
    {
        var reads = new List<int>();
        var ram = new byte[0x10000];
        ram[0x1000] = opcode;

        Disassembler.Decode<RefBus, Mos6502Variant>(new RefBus(new LoggingBus(ram, reads)), 0x1000);

        Assert.Equal(expected, reads.Distinct().Count());
        Assert.All(reads, address => Assert.InRange(address, 0x1000, 0x1000 + expected - 1));
    }

    /// <summary>Records every address read, so a decode can be held to the bytes it needs.</summary>
    private sealed class LoggingBus(byte[] ram, List<int> reads) : IBus
    {
        public byte Read(int address)
        {
            reads.Add(address & 0xFFFF);
            return ram[address & 0xFFFF];
        }

        public void Write(int address, byte value) => ram[address & 0xFFFF] = value;
    }

    /// <summary>
    /// The README's example, compiled. Decoding straight from a running core's bus is the
    /// obvious way to reach for this, and <c>Cpu.Bus</c> hands back a <c>ref</c> that has to
    /// bind to the <c>in</c> parameter for that to work.
    /// </summary>
    [Fact]
    public void DecodesFromALiveCoresBus()
    {
        var ram = new byte[0x10000];
        ram[0xC000] = 0xBD; ram[0xC001] = 0x34; ram[0xC002] = 0x12;
        var cpu = new Cpu<FlatBus, Mos6502Variant>(new FlatBus(ram));

        var instruction = Disassembler.Decode<FlatBus, Mos6502Variant>(cpu.Bus, 0xC000);

        Assert.Equal("LDA $1234,X", instruction.ToString());
        Assert.Equal(3, instruction.Length);
    }

    [Fact]
    public void ToString_JoinsTheMnemonicAndOperand()
    {
        Assert.Equal("LDA $1234,X", Decode(0xBD, 0x34, 0x12).ToString());
        Assert.Equal("NOP", Decode(0xEA).ToString());
    }

    /// <summary>
    /// The claim this phase makes is that the disassembler and the engine cannot disagree,
    /// and length is where a disagreement would actually hurt: one wrong byte count puts
    /// every instruction after it out of phase. So execute each opcode and require the
    /// program counter to move by exactly what was decoded.
    /// </summary>
    /// <remarks>
    /// This is the gate for the opcodes the 64tass round-trip cannot reach. That gate has to
    /// exclude anything two opcodes render alike — the twelve NMOS JAMs, the undocumented
    /// NOPs, all thirty-two of Synertek's Rockwell slots — which is precisely the set whose
    /// length is least obvious. Here nothing is excluded for being ambiguous, because the
    /// processor is not confused by two opcodes sharing a name.
    /// </remarks>
    [Fact]
    public void DecodedLength_MatchesWhatTheProcessorConsumes()
    {
        AssertLengthsMatchExecution<Mos6502Variant>();
        AssertLengthsMatchExecution<Mos6510Variant>();
        AssertLengthsMatchExecution<Synertek65C02Variant>();
        AssertLengthsMatchExecution<Rockwell65C02Variant>();
        AssertLengthsMatchExecution<Wdc65C02Variant>();
    }

    /// <summary>
    /// Instructions that do not fall through to the next one, so the program counter after
    /// them says nothing about their length. Branches are absent deliberately: every operand
    /// byte in this test is <c>$00</c>, and a branch with a zero displacement lands on the
    /// instruction after itself whether it is taken or not.
    /// </summary>
    private static readonly HashSet<string> DoesNotFallThrough =
        ["JMP", "JSR", "RTS", "RTI", "BRK", "JAM", "WAI", "STP"];

    private static void AssertLengthsMatchExecution<TVariant>() where TVariant : struct, ICpuVariant
    {
        const ushort at = 0x0200;

        for (var opcode = 0; opcode < 256; opcode++)
        {
            var ram = new byte[0x10000];
            ram[at] = (byte)opcode;                 // operands stay $00, which keeps branches local

            var decoded = Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), at);
            if (DoesNotFallThrough.Contains(decoded.Mnemonic)) continue;

            var cpu = new Cpu<FlatBus, TVariant>(new FlatBus(ram));
            cpu.State.PC = at;
            cpu.State.S = 0xFD;
            cpu.State.P = Flag.U | Flag.I;
            cpu.Step();

            Assert.True(cpu.State.PC - at == decoded.Length,
                $"{typeof(TVariant).Name} ${opcode:X2} {decoded}: decoded as {decoded.Length} " +
                $"byte(s), but the processor advanced {cpu.State.PC - at}. A linear " +
                $"disassembly would lose sync here.");
        }
    }

    /// <summary>
    /// The 65816 fetches from PBR:PC across 16 MB. Decoding at $12C000 must read the opcode
    /// at $12C000, not at $00C000 — the mask Decode applied before this task.
    /// </summary>
    [Fact]
    public void W65C816_DecodesInTheBankItIsGiven()
    {
        var ram = new BankedBus();
        ram[0x12C000] = 0xEA;           // NOP in bank $12
        ram[0x00C000] = 0xA9;           // LDA # in bank 0, to prove the bank is not masked away

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0x12C000);

        Assert.Equal("NOP", decoded.Mnemonic);
    }

    /// <summary>
    /// An operand fetch wraps within the program bank, never into the next one: PC rolls
    /// $FFFF to $0000 without touching PBR (research document §2.2/§2.4).
    /// </summary>
    [Fact]
    public void W65C816_OperandFetchWrapsInsideTheBank()
    {
        var ram = new BankedBus();
        ram[0x12FFFF] = 0x4C;           // JMP abs, with its operand wrapping to $120000
        ram[0x120000] = 0x34;
        ram[0x120001] = 0x12;

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0x12FFFF);

        Assert.Equal("$1234", decoded.Operand);
    }

    /// <summary>
    /// The width-carrying overload exists and the old signature still compiles unchanged,
    /// defaulting to eight-bit widths. This is the compatibility promise the spec makes.
    /// </summary>
    /// <remarks>
    /// Two halves, because the 6502 alone cannot prove the claim. There both flags are false
    /// whatever the overload does — the part has no width flags — so equality proves only that
    /// one overload delegates to the other, not that the default <em>means</em> eight-bit. The
    /// 65816 half is where the default is a real choice: <c>LDA #</c> is two bytes at
    /// <c>m = 1</c> and three at <c>m = 0</c>, so a length of 2 through the width-less overload
    /// is the documented default observed rather than assumed. It passes trivially today —
    /// <see cref="AddrMode.Immediate"/> still decodes at a fixed two bytes — and stops being
    /// trivial the moment a later task makes that length depend on the flag, which is exactly
    /// when a silently flipped default would otherwise slip through.
    /// </remarks>
    [Fact]
    public void TheWidthlessOverload_StillCompilesAndMeansEightBit()
    {
        var ram = new byte[0x10000];
        ram[0x1000] = 0xA9;             // LDA #
        ram[0x1001] = 0x34;

        var implicitWidths = Disassembler.Decode<FlatBus, Mos6502Variant>(new FlatBus(ram), 0x1000);
        var explicitWidths = Disassembler.Decode<FlatBus, Mos6502Variant>(
            new FlatBus(ram), 0x1000, wideAccumulator: false, wideIndex: false);

        Assert.Equal(implicitWidths, explicitWidths);

        var narrow = Disassembler.Decode<FlatBus, W65C816Variant>(new FlatBus(ram), 0x1000);

        Assert.Equal(2, narrow.Length);
    }

    /// <summary>
    /// Lays bytes at <paramref name="address"/> in a full 24-bit space and decodes there.
    /// Operand bytes wrap within the program bank, the way the processor fetches them.
    /// </summary>
    private static Instruction Decode816(
        int address, bool wideAccumulator, bool wideIndex, params byte[] bytes)
    {
        var ram = new BankedBus();
        for (var i = 0; i < bytes.Length; i++)
        {
            ram[(address & 0xFF0000) | ((address + i) & 0xFFFF)] = bytes[i];
        }

        return Disassembler.Decode<RefBus, W65C816Variant>(
            new RefBus(ram), address, wideAccumulator, wideIndex);
    }

    /// <summary>
    /// Every 65816 addressing mode and the text it renders, at a fixed <c>$C000</c>. Expected
    /// values are research §15.1's notation table, which was measured by assembling each line
    /// with 64tass rather than recalled. Operand bytes are <c>$12</c>, <c>$34</c>, <c>$56</c>
    /// in stream order, so a renderer that transposed a little-endian pair would show it.
    /// </summary>
    /// <remarks>
    /// The <c>@w</c>/<c>@l</c> prefixes §15.1's table carries are deliberately absent: they are
    /// 64tass spellings (§15.5 gap 2) and live in <c>RoundTripTests.ForAssembler</c>, not in
    /// <see cref="Instruction.Operand"/>. <see cref="W65C816_RendersNoAssemblerSpecificText"/>
    /// holds that line.
    /// </remarks>
    [Theory]
    [InlineData(new byte[] { 0xEA }, "NOP", "", 1)]
    [InlineData(new byte[] { 0x0A }, "ASL", "A", 1)]

    // ImmediateByte — one byte whatever m and x say (datasheet Table 5-7 Note 1).
    [InlineData(new byte[] { 0xC2, 0x12 }, "REP", "#$12", 2)]
    [InlineData(new byte[] { 0xE2, 0x12 }, "SEP", "#$12", 2)]
    [InlineData(new byte[] { 0x42, 0x12 }, "WDM", "#$12", 2)]

    // The direct-page family. Same notation as the eight-bit cores' page zero.
    [InlineData(new byte[] { 0xA5, 0x12 }, "LDA", "$12", 2)]
    [InlineData(new byte[] { 0xB5, 0x12 }, "LDA", "$12,X", 2)]
    [InlineData(new byte[] { 0xB6, 0x12 }, "LDX", "$12,Y", 2)]
    [InlineData(new byte[] { 0xB2, 0x12 }, "LDA", "($12)", 2)]
    [InlineData(new byte[] { 0xB1, 0x12 }, "LDA", "($12),Y", 2)]
    [InlineData(new byte[] { 0xA1, 0x12 }, "LDA", "($12,X)", 2)]

    // The long indirects. Square brackets are how every 65816 assembler tells them from the
    // sixteen-bit indirects, and they are the only operands that use them.
    [InlineData(new byte[] { 0xA7, 0x12 }, "LDA", "[$12]", 2)]
    [InlineData(new byte[] { 0xB7, 0x12 }, "LDA", "[$12],Y", 2)]

    // Stack-relative.
    [InlineData(new byte[] { 0xA3, 0x12 }, "LDA", "$12,S", 2)]
    [InlineData(new byte[] { 0xB3, 0x12 }, "LDA", "($12,S),Y", 2)]

    // Absolute, and absolute long — three operand bytes, so six hex digits.
    [InlineData(new byte[] { 0xAD, 0x12, 0x34 }, "LDA", "$3412", 3)]
    [InlineData(new byte[] { 0xBD, 0x12, 0x34 }, "LDA", "$3412,X", 3)]
    [InlineData(new byte[] { 0xB9, 0x12, 0x34 }, "LDA", "$3412,Y", 3)]
    [InlineData(new byte[] { 0xAF, 0x12, 0x34, 0x56 }, "LDA", "$563412", 4)]
    [InlineData(new byte[] { 0xBF, 0x12, 0x34, 0x56 }, "LDA", "$563412,X", 4)]

    // The jumps and calls, including both opcodes this table spells JML.
    [InlineData(new byte[] { 0x6C, 0x12, 0x34 }, "JMP", "($3412)", 3)]
    [InlineData(new byte[] { 0x7C, 0x12, 0x34 }, "JMP", "($3412,X)", 3)]
    [InlineData(new byte[] { 0xFC, 0x12, 0x34 }, "JSR", "($3412,X)", 3)]
    [InlineData(new byte[] { 0xDC, 0x12, 0x34 }, "JML", "[$3412]", 3)]
    [InlineData(new byte[] { 0x5C, 0x12, 0x34, 0x56 }, "JML", "$563412", 4)]
    [InlineData(new byte[] { 0x22, 0x12, 0x34, 0x56 }, "JSL", "$563412", 4)]

    // Both branch widths, shown as the address landed on. $C000 + 2 + $12 and
    // $C000 + 3 + $3412 — the second is research §15.1's displacement rule run backwards.
    [InlineData(new byte[] { 0xF0, 0x12 }, "BEQ", "$C014", 2)]
    [InlineData(new byte[] { 0x82, 0x12, 0x34 }, "BRL", "$F415", 3)]

    // AddrMode.Stack, which fixes no length: one byte for the pushes, pulls and RTL, two for
    // BRK, COP and PEI, three for JMP, JSR, PEA and PER.
    [InlineData(new byte[] { 0x8B }, "PHB", "", 1)]
    [InlineData(new byte[] { 0x6B }, "RTL", "", 1)]
    [InlineData(new byte[] { 0x00, 0x12 }, "BRK", "#$12", 2)]
    [InlineData(new byte[] { 0x02, 0x12 }, "COP", "#$12", 2)]
    [InlineData(new byte[] { 0xD4, 0x12 }, "PEI", "($12)", 2)]
    [InlineData(new byte[] { 0x4C, 0x12, 0x34 }, "JMP", "$3412", 3)]
    [InlineData(new byte[] { 0x20, 0x12, 0x34 }, "JSR", "$3412", 3)]
    [InlineData(new byte[] { 0xF4, 0x12, 0x34 }, "PEA", "$3412", 3)]
    [InlineData(new byte[] { 0x62, 0x12, 0x34 }, "PER", "$F415", 3)]
    public void W65C816_EveryModeRendersInTheMeasuredNotation(
        byte[] bytes, string mnemonic, string operand, int length)
    {
        var decoded = Decode816(0xC000, wideAccumulator: false, wideIndex: false, bytes);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal(operand, decoded.Operand);
        Assert.Equal(length, decoded.Length);
    }

    /// <summary>
    /// MVN and MVP reverse their operands. The byte stream is opcode, destination bank,
    /// source bank (research document §14.3); assemblers write MVN source,destination.
    /// Rendering them in byte order produces text that reassembles into a different
    /// instruction — measured against 64tass: MVN $12,$34 assembles to 54 34 12.
    /// </summary>
    [Theory]
    [InlineData(0x54, "MVN")]
    [InlineData(0x44, "MVP")]
    public void BlockMove_RendersSourceThenDestination(byte opcode, string mnemonic)
    {
        var ram = new BankedBus();
        ram[0xC000] = opcode;
        ram[0xC001] = 0x34;             // destination bank, first in the stream
        ram[0xC002] = 0x12;             // source bank

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0xC000);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal("$12,$34", decoded.Operand);
        Assert.Equal(3, decoded.Length);
    }

    /// <summary>
    /// The immediate is the one mode whose length the byte stream does not encode, and the
    /// two width flags are independent: <c>m</c> sizes the accumulator operations and
    /// <c>x</c> the index ones.
    /// </summary>
    /// <remarks>
    /// Every case passes <strong>opposed</strong> flags, which is the whole point of the test.
    /// Setting both true would let an implementation that read <c>x</c> where it meant
    /// <c>m</c> pass unmoved; the same blind spot produced a real hole in phase 7c's test
    /// design. The membership under test is not a list written here — it is
    /// <c>OpcodeInfo.Width</c>, which the opcode table declares per opcode.
    /// </remarks>
    [Theory]
    // Width.M: widens on wideAccumulator, and is unmoved by wideIndex.
    [InlineData(0xA9, "LDA", true, false, "#$3412", 3)]
    [InlineData(0xA9, "LDA", false, true, "#$12", 2)]
    [InlineData(0x09, "ORA", true, false, "#$3412", 3)]
    [InlineData(0x09, "ORA", false, true, "#$12", 2)]
    [InlineData(0x89, "BIT", true, false, "#$3412", 3)]
    [InlineData(0x89, "BIT", false, true, "#$12", 2)]
    [InlineData(0x69, "ADC", true, false, "#$3412", 3)]
    [InlineData(0xE9, "SBC", false, true, "#$12", 2)]
    // Width.X: the mirror image.
    [InlineData(0xA2, "LDX", false, true, "#$3412", 3)]
    [InlineData(0xA2, "LDX", true, false, "#$12", 2)]
    [InlineData(0xA0, "LDY", false, true, "#$3412", 3)]
    [InlineData(0xA0, "LDY", true, false, "#$12", 2)]
    [InlineData(0xE0, "CPX", false, true, "#$3412", 3)]
    [InlineData(0xE0, "CPX", true, false, "#$12", 2)]
    [InlineData(0xC0, "CPY", false, true, "#$3412", 3)]
    [InlineData(0xC0, "CPY", true, false, "#$12", 2)]
    // ImmediateByte never widens, whichever flag is set.
    [InlineData(0xC2, "REP", true, false, "#$12", 2)]
    [InlineData(0xC2, "REP", false, true, "#$12", 2)]
    [InlineData(0xE2, "SEP", true, false, "#$12", 2)]
    [InlineData(0xE2, "SEP", false, true, "#$12", 2)]
    [InlineData(0x42, "WDM", true, false, "#$12", 2)]
    [InlineData(0x42, "WDM", false, true, "#$12", 2)]
    public void W65C816_ImmediateTakesItsWidthFromTheOperationsOwnFlag(
        byte opcode, string mnemonic, bool wideAccumulator, bool wideIndex,
        string operand, int length)
    {
        var decoded = Decode816(0xC000, wideAccumulator, wideIndex, opcode, 0x12, 0x34);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal(operand, decoded.Operand);
        Assert.Equal(length, decoded.Length);
    }

    /// <summary>
    /// A branch's displacement add wraps within the program bank and never carries into
    /// <c>PBR</c> (research document §14.5), so the target's bank is always the instruction's
    /// own. Rendered as the full address when that bank is non-zero: a sixteen-bit rendering
    /// would print <c>$C036</c> for a branch in bank <c>$12</c> and say nothing about which
    /// bank it lands in.
    /// </summary>
    /// <remarks>
    /// <strong>No round-trip can catch this.</strong> <c>RoundTripTests</c> lays every opcode
    /// out in bank 0, where the bank is zero and the rendering is four digits either way, so
    /// this is the only gate on the banked case.
    /// </remarks>
    [Theory]
    [InlineData(0x12C000, 0x34, "$12C036")]         // forwards, inside the bank
    [InlineData(0x12C000, 0x80, "$12BF82")]         // backwards, inside the bank
    [InlineData(0x12FFFF, 0x00, "$120001")]         // over the top of the bank, and no carry into PBR
    [InlineData(0x120001, 0xFC, "$12FFFF")]         // under the bottom of the bank, likewise
    [InlineData(0x00C000, 0x34, "$C036")]           // bank 0 keeps §15.1's four-digit notation
    public void W65C816_BranchTargetStaysInTheProgramBank(int at, byte displacement, string target)
    {
        var decoded = Decode816(at, wideAccumulator: false, wideIndex: false, 0x80, displacement);

        Assert.Equal("BRA", decoded.Mnemonic);
        Assert.Equal(target, decoded.Operand);
    }

    /// <summary>
    /// BRL and PER encode sixteen-bit displacements and wrap the same way, which is worth its
    /// own case because they are the only operands read as a signed <em>word</em>.
    /// </summary>
    [Theory]
    [InlineData(0x82, "BRL")]
    [InlineData(0x62, "PER")]
    public void W65C816_SixteenBitDisplacementsWrapInTheProgramBank(byte opcode, string mnemonic)
    {
        // $12FFFE + 3 + $0004 = $130005 unwrapped; the bank must not carry.
        var decoded = Decode816(0x12FFFE, false, false, opcode, 0x04, 0x00);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal("$120005", decoded.Operand);

        // And backwards, with the sign bit set: $C000 + 3 - 2 = $C001.
        Assert.Equal("$C001", Decode816(0xC000, false, false, opcode, 0xFE, 0xFF).Operand);
    }

    /// <summary>
    /// Every 65816 opcode decodes, at every width combination, with a length a linear walk can
    /// use. This is the gate on the phase's actual claim: no <see cref="NotSupportedException"/>
    /// is reachable for any defined opcode of this variant.
    /// </summary>
    [Fact]
    public void W65C816_EveryOpcodeDecodesAtEveryWidth()
    {
        foreach (var (wideAccumulator, wideIndex) in
                 new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            for (var opcode = 0; opcode < 256; opcode++)
            {
                var decoded = Decode816(
                    0xC000, wideAccumulator, wideIndex, (byte)opcode, 0x12, 0x34, 0x56);

                Assert.InRange(decoded.Length, 1, 4);
                Assert.False(string.IsNullOrWhiteSpace(decoded.Mnemonic),
                    $"${opcode:X2} decoded to an empty mnemonic on the 65816.");
            }
        }
    }

    /// <summary>
    /// The library renders dialect-neutral text. <c>@w</c> and <c>@l</c> are 64tass's spelling
    /// of "this width, not the shorter one that also fits" (research §15.5 gap 2); ca65 writes
    /// <c>a:</c>. They belong in <c>RoundTripTests.ForAssembler</c>, which is where this project
    /// already puts an assembler's spelling — the <c>RMB0</c>/<c>rmb 0,</c> case — and putting
    /// them here instead was tried in task 3 and reversed.
    /// </summary>
    [Fact]
    public void W65C816_RendersNoAssemblerSpecificText()
    {
        for (var opcode = 0; opcode < 256; opcode++)
        {
            var decoded = Decode816(0xC000, true, true, (byte)opcode, 0x12, 0x34, 0x56);

            Assert.DoesNotContain("@", decoded.Operand);
        }
    }
}
