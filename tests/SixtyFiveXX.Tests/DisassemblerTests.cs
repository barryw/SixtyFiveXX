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
}
