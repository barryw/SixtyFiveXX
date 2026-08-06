using System.Diagnostics;
using System.Text;
using SixtyFiveXX.Variants;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Disassembles an image containing every opcode, reassembles the text with 64tass, and
/// requires the bytes back. An external assembler is the only oracle that can say the
/// notation is right rather than merely self-consistent.
/// </summary>
/// <remarks>
/// <para>
/// 64tass is already a prerequisite for the Klaus interrupt test, so this costs no new
/// tooling. It is not a total gate: where several opcodes render as the same text, an
/// assembler has to pick one encoding, and the others cannot come back as themselves. The
/// 65xx families are full of those — six three-byte NOPs on the 65C02, twelve JAMs on the
/// NMOS parts. Those opcodes are excluded <em>and named in the output</em>, because a gate
/// that quietly drops coverage reads as certification of what it never ran.
/// </para>
/// <para>
/// The CMOS variants are assembled as <c>w65c02</c> rather than their own dialects: plain
/// <c>65c02</c> has no multi-byte <c>NOP</c> at all in 64tass, and rejects the text
/// outright. Where that dialect disagrees with the variant's real table — <c>$CB</c> is
/// <c>WAI</c> to 64tass and a one-cycle <c>NOP</c> to Synertek — the opcode collides with
/// the ordinary <c>NOP</c> and the ambiguity filter removes it without being told to.
/// </para>
/// </remarks>
public class RoundTripTests(ITestOutputHelper output)
{
    /// <summary>Where the synthesized program is assembled. Arbitrary, but must be fixed.</summary>
    private const int Origin = 0x1000;

    /// <summary>Operand bytes given to every opcode. Values chosen to be visible in a listing.</summary>
    private const byte OperandLo = 0x34;
    private const byte OperandHi = 0x12;

    /// <summary>
    /// The third operand byte, reached only by the 65816's four-byte long modes. Without it,
    /// <c>ram[cursor + 3]</c> is written by nothing at all: a four-byte instruction ends at
    /// <c>cursor + 3</c> and the next opcode starts at <c>cursor + 4</c>, so the byte would stay
    /// $00 from the zeroed array rather than take on any neighboring opcode. The first long form
    /// would then render <c>ORA @l $001234</c> — the bank silently collapses to $00, and every
    /// long form starts taking the <c>@l</c> forcing arm instead of the unforced path. $56 is
    /// the value research §15.3 laid its four listings out with, and being non-zero it is the
    /// one that proves a bank is carried rather than defaulted.
    /// </summary>
    private const byte OperandBank = 0x56;

    /// <summary>
    /// Stands in for the <c>.cpu</c> name until <see cref="Assemble"/> substitutes it, so
    /// one disassembly can be assembled under whichever dialect the caller names.
    /// </summary>
    private const string CpuDirectivePlaceholder = "%CPU%";

    // The covered count is pinned per variant rather than floored, because how many opcodes
    // are uniquely encodable is a property of the instruction set and not a tuning knob. A
    // renderer change that quietly collapsed two opcodes into the same text would otherwise
    // pass with less coverage than the run before it.
    //
    // The differences are all explicable. The NMOS parts lose the twelve JAMs to one another
    // and the undocumented NOPs to their documented namesakes. Synertek loses far more
    // because its thirty-two Rockwell slots are NOP shapes too. WDC covers two more than
    // Rockwell for exactly one reason: $CB and $DB are WAI and STP there, and NOP shapes —
    // hence ambiguous — on every other 65C02.
    //
    // The 65816 is the one core that loses nothing. WDC assigned all 256 opcodes and this
    // repository's table defines all 256, so there are no JAM or undefined shapes to collide
    // and no undocumented NOP aliases; its count is 256 at every width combination.

    [Fact]
    public void Mos6502_RoundTripsThroughTheAssembler() => RoundTrip<Mos6502Variant>("6502i", 213);

    /// <summary>The 6510 shares the 6502's table, so it must produce identical text.</summary>
    [Fact]
    public void Mos6510_RoundTripsThroughTheAssembler() => RoundTrip<Mos6510Variant>("6502i", 213);

    [Fact]
    public void Synertek65C02_RoundTripsThroughTheAssembler() =>
        RoundTrip<Synertek65C02Variant>("w65c02", 177);

    [Fact]
    public void Rockwell65C02_RoundTripsThroughTheAssembler() =>
        RoundTrip<Rockwell65C02Variant>("w65c02", 210);

    [Fact]
    public void Wdc65C02_RoundTripsThroughTheAssembler() => RoundTrip<Wdc65C02Variant>("w65c02", 212);

    /// <summary>
    /// The 65816 round-trips under every combination of the two width flags. Four runs rather
    /// than one because m and x size different opcodes, and a single setting would leave one
    /// flag's effect unexercised — the length of LDA # is the whole substance of the m/x
    /// problem, and it is invisible unless both settings are assembled.
    /// </summary>
    /// <remarks>
    /// The third argument is the listing's length, which research §15.3 measured at all four
    /// combinations. It is pinned rather than merely reported because it is the one number
    /// that says the four runs differ: the steps are the eight <c>Width.M</c> immediates and
    /// the four <c>Width.X</c> ones gaining a byte each, so +8 for <c>.al</c>, +4 for
    /// <c>.xl</c>, +12 for both.
    /// </remarks>
    [Theory]
    [InlineData(false, false, 559)]
    [InlineData(true, false, 567)]
    [InlineData(false, true, 563)]
    [InlineData(true, true, 571)]
    public void W65C816_RoundTripsThroughTheAssembler(
        bool wideAccumulator, bool wideIndex, int expectedBytes) =>
        RoundTrip816(wideAccumulator, wideIndex, expectedBytes);

    /// <summary>
    /// The width plumbing has to be load-bearing. Assembling a disassembly under the opposite
    /// width directive must change the bytes; if it does not, the round-trip is passing
    /// without the widths mattering and would pass with them ignored entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 8-bit listing is widened rather than the 16-bit one narrowed, which is the
    /// direction the brief proposed. Narrowing does not produce different bytes — it produces
    /// <em>no</em> bytes: <c>LDA #$1234</c> under <c>.as</c> is
    /// <c>too large for a 8 bit unsigned integer</c> and 64tass rejects the file, so
    /// <see cref="Assemble"/> fails on the exit code before there is anything to compare.
    /// Widening is the same claim in the direction that assembles: <c>LDA #$34</c> is
    /// <c>a9 34</c> under <c>.as</c> and <c>a9 34 00</c> under <c>.al</c>, so the twelve
    /// width-flagged immediates each gain a byte and the image grows from 559 to 571.
    /// </para>
    /// <para>
    /// This is one half of the proof and the pinned lengths on
    /// <see cref="W65C816_RoundTripsThroughTheAssembler"/> are the other. This says the
    /// directives reach 64tass and change what it emits; those say the disassembler's own
    /// flags reach the decode, because a renderer that ignored them would build all four
    /// listings at 559 bytes and miss three of the four pins.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGateDiscriminates_TheWidthDirectivesChangeTheBytes()
    {
        var (source, expected) = Build816(wideAccumulator: false, wideIndex: false);

        // Environment.NewLine because that is what StringBuilder.AppendLine wrote. Matching a
        // bare \n would silently replace nothing on Windows and leave both assertions below
        // comparing a listing with itself.
        var widened = source
            .Replace("\t.as" + Environment.NewLine, "\t.al" + Environment.NewLine)
            .Replace("\t.xs" + Environment.NewLine, "\t.xl" + Environment.NewLine);

        Assert.NotEqual(source, widened);

        var widenedBytes = Assemble(widened, "65816");
        Assert.NotEqual(expected, widenedBytes);
        Assert.Equal(571, widenedBytes.Length);
    }

    /// <summary>
    /// The gate has to be able to fail. Corrupting one operand — an <c>,X</c> that becomes
    /// an <c>,Y</c> — must change the bytes 64tass produces. Without this the round-trip
    /// could be passing because nothing it does is load-bearing.
    /// </summary>
    [Fact]
    public void TheGateDiscriminates_AnAlteredOperandChangesTheBytes()
    {
        var (source, expected) = Build<Mos6502Variant>(213);
        var corrupted = ReplaceFirst(source, "LDA $1234,X", "LDA $1234,Y");

        Assert.NotEqual(source, corrupted);
        Assert.NotEqual(expected, Assemble(corrupted, "6502i"));
    }

    /// <summary>
    /// The one case the five round-trips above <em>structurally cannot reach</em>: an absolute
    /// operand under <c>$0100</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OperandLo"/> and <see cref="OperandHi"/> put every absolute operand in that
    /// listing at <c>$1234</c>, where the shortest encoding is the one the disassembler meant,
    /// so those five gates pass whether the width is forced or not. Below <c>$0100</c> it is
    /// not: 64tass assembles <c>LDA $0012</c> as the two-byte direct-page <c>a5 12</c>, and
    /// research §15.4 counted 53 opcodes on each NMOS part and 42 on each 65C02 that render
    /// text reassembling to a different instruction. The defect sat there from phase 6a to
    /// phase 7e because nothing looked below the boundary.
    /// </para>
    /// <para>
    /// <strong>So this is not redundant with the five above and must not be deleted as such.</strong>
    /// The constants stay where they are — the pinned covered counts are a property of the
    /// current layout — and this test reaches the gap instead.
    /// </para>
    /// </remarks>
    [Fact]
    public void AbsoluteOperandsBelowTheCollapseBoundary_RoundTrip()
    {
        // The last opcode of each list is that dialect's three-byte NOP shape. On the CMOS
        // parts $DC is one of the three NopAbsoluteExtra shapes, and that trio alone is what
        // closes the real mode-collapse defect. On the NMOS parts $1C is plain NOP
        // AddrMode.AbsoluteX (Opcodes6502.cs:237) — NMOS has no NopAbsoluteExtra mode at all —
        // so it pins the NOP mnemonic through a path $BD already covers rather than closing any
        // mode-collapse debt of its own. Each is still the encoding 64tass picks for its
        // dialect's text, which is why the shape can be pinned by one opcode and not by the
        // others sharing its rendering.
        BelowTheBoundary<Mos6502Variant>("6502i", [0xAD, 0xBD, 0xB9, 0xBE, 0x8D, 0xEE, 0x1C]);
        BelowTheBoundary<Mos6510Variant>("6502i", [0xAD, 0xBD, 0xB9, 0xBE, 0x8D, 0xEE, 0x1C]);
        BelowTheBoundary<Synertek65C02Variant>("w65c02", [0xAD, 0xBD, 0xB9, 0xBE, 0x8D, 0xEE, 0xDC]);
        BelowTheBoundary<Rockwell65C02Variant>("w65c02", [0xAD, 0xBD, 0xB9, 0xBE, 0x8D, 0xEE, 0xDC]);
        BelowTheBoundary<Wdc65C02Variant>("w65c02", [0xAD, 0xBD, 0xB9, 0xBE, 0x8D, 0xEE, 0xDC]);

        // The 65816's long modes, and the only place @l is reached: $6F ADC long and $7F
        // ADC long,X render $000012, which 64tass unforced assembles as the two-byte
        // direct-page 65 12 and 75 12. $DC JML [abs] is the bracketed indirect — measured
        // not to collapse either way, so it pins that over-forcing through a bracket costs
        // nothing, which is the half of §15.2's finding TopByteIsZero's skip relies on.
        BelowTheBoundary<W65C816Variant>("65816", [0xAD, 0x6F, 0x7F, 0xDC]);
    }

    /// <summary>
    /// $AD LDA abs, $BD LDA abs,X, $BE LDX abs,Y, $8D STA abs and $EE INC abs at the operand
    /// $0012 — five three-byte opcodes every one of the five 8-bit cores decodes alike, and
    /// all five collapse without the prefix. $B9 LDA abs,Y is here for the opposite reason:
    /// there is no <c>LDA dp,Y</c>, so it never collapses and the prefix is redundant on it.
    /// Redundant still has to assemble, which is the half of §15.2's finding that says
    /// over-forcing costs nothing.
    /// </summary>
    private static void BelowTheBoundary<TVariant>(string cpu, byte[] opcodes)
        where TVariant : struct, ICpuVariant
    {
        var ram = new byte[0x10000];
        var source = new StringBuilder()
            .AppendLine($"\t.cpu \"{CpuDirectivePlaceholder}\"")
            .AppendLine($"\t*=${Origin:X4}");

        var cursor = Origin;
        foreach (var opcode in opcodes)
        {
            // $12 then zeros, so an absolute operand is $0012 and a long one $000012 — both
            // under their width's boundary. Written before the decode because an operand byte
            // cannot change a length, and any byte past this instruction is either overwritten
            // by the next opcode or left outside the slice compared at the end.
            ram[cursor] = opcode;
            ram[cursor + 1] = 0x12;
            ram[cursor + 2] = 0x00;
            ram[cursor + 3] = 0x00;

            var decoded = Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), cursor);
            Assert.True(decoded.Length is 3 or 4,
                $"${opcode:X2} is {decoded.Length} bytes on {typeof(TVariant).Name}; this test " +
                $"is about operands that can sit below a width boundary, and a shorter " +
                $"instruction has none.");
            source.AppendLine('\t' + ForAssembler(decoded));
            cursor += decoded.Length;
        }

        Assert.Equal(ram[Origin..cursor], Assemble(source.ToString(), cpu));
    }

    /// <summary>The 65816 arm of <see cref="RoundTrip{TVariant}"/>, pinning §15.3's listing length.</summary>
    private void RoundTrip816(bool wideAccumulator, bool wideIndex, int expectedBytes)
    {
        var (source, expected) = Build816(wideAccumulator, wideIndex);

        Assert.True(expected.Length == expectedBytes,
            $"The 65816 listing is {expected.Length} bytes at m={(wideAccumulator ? 16 : 8)}, " +
            $"x={(wideIndex ? 16 : 8)}; research §15.3 measured {expectedBytes}. A length that " +
            $"moved means an opcode changed size, not that the notation changed.");

        // Pins OperandBank: nothing writes ram[cursor + 3] for a four-byte instruction (the
        // next opcode starts at cursor + 4), so if that write were removed the bank would
        // silently revert to $00 and this text would read $001234 instead.
        Assert.Contains("$561234", source);

        Compare(expected, Assemble(source, "65816"));
    }

    /// <summary>
    /// The 65816's whole table at one width combination. Every opcode is covered — the part
    /// assigns all 256 and none of them render alike, so unlike the five 8-bit cores there is
    /// nothing to exclude (research §15.3).
    /// </summary>
    private (string Source, byte[] Expected) Build816(bool wideAccumulator, bool wideIndex) =>
        Build<W65C816Variant>(256, wideAccumulator, wideIndex);

    private void RoundTrip<TVariant>(string cpu, int expectedCovered)
        where TVariant : struct, ICpuVariant
    {
        var (source, expected) = Build<TVariant>(expectedCovered);
        Compare(expected, Assemble(source, cpu));
    }

    private static void Compare(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i] == actual[i],
                $"Byte {i} of the reassembled image is ${actual[i]:X2}, expected ${expected[i]:X2}. " +
                $"The disassembler rendered something 64tass reads as a different instruction.");
        }
    }

    /// <summary>
    /// Lays out every unambiguously-encodable opcode, disassembles the result, and returns
    /// the source text alongside the bytes it has to reproduce.
    /// </summary>
    /// <remarks>
    /// The two width flags are the 65816's <c>m</c> and <c>x</c>, and they are used three
    /// times over: to lay the image out, to decode it, and to declare it to the assembler.
    /// All three have to agree — a layout written at one width and read at another silently
    /// decodes something the bytes do not say. The five 8-bit cores take the defaults, have
    /// no width flags to declare, and emit no directives.
    /// </remarks>
    private (string Source, byte[] Expected) Build<TVariant>(
        int expectedCovered, bool wideAccumulator = false, bool wideIndex = false)
        where TVariant : struct, ICpuVariant
    {
        var ambiguous = AmbiguousOpcodes<TVariant>(wideAccumulator, wideIndex);

        var ram = new byte[0x10000];
        var cursor = Origin;
        for (var opcode = 0; opcode < 256; opcode++)
        {
            if (ambiguous.Contains(opcode)) continue;

            // Write only as many operand bytes as this opcode actually has. Filling all
            // three unconditionally would let the next opcode overwrite them, so each
            // instruction's operand would be whatever happened to follow it — decodable,
            // but not what the layout says, and it would make anything written against
            // these operands drift the moment the table gains an opcode. The length does
            // not depend on the operand bytes, so decoding before writing them is safe.
            ram[cursor] = (byte)opcode;
            var length = Disassembler
                .Decode<FlatBus, TVariant>(new FlatBus(ram), cursor, wideAccumulator, wideIndex)
                .Length;
            if (length > 1) ram[cursor + 1] = OperandLo;
            if (length > 2) ram[cursor + 2] = OperandHi;
            if (length > 3) ram[cursor + 3] = OperandBank;
            cursor += length;
        }

        var source = new StringBuilder().AppendLine($"\t.cpu \"{CpuDirectivePlaceholder}\"");

        if (TVariant.Variant == CpuVariant.W65C816)
        {
            // .mansiz is 64tass's default, and is stated rather than assumed because the
            // alternative corrupts this gate in silence: under .autsiz the assembler tracks
            // the $C2 and $E2 bytes this image contains — REP and SEP — and resizes every
            // immediate after them, so the listing would assemble at a width the disassembly
            // never claimed (research §15.1).
            source.AppendLine("\t.mansiz")
                  .AppendLine(wideAccumulator ? "\t.al" : "\t.as")
                  .AppendLine(wideIndex ? "\t.xl" : "\t.xs");
        }

        source.AppendLine($"\t*=${Origin:X4}");

        // Disassemble what was just laid out, rather than reusing the loop above: the text
        // under test has to come from a linear walk, which is how a caller would use it.
        for (var address = Origin; address < cursor;)
        {
            var decoded = Disassembler
                .Decode<FlatBus, TVariant>(new FlatBus(ram), address, wideAccumulator, wideIndex);
            source.AppendLine('\t' + ForAssembler(decoded));
            address += decoded.Length;
        }

        var covered = 256 - ambiguous.Count;
        output.WriteLine($"{typeof(TVariant).Name}: {covered} of 256 opcodes round-tripped, " +
                         $"{ambiguous.Count} excluded as ambiguous: " +
                         string.Join(", ", ambiguous.Order().Select(o => $"${o:X2}")));

        Assert.True(covered == expectedCovered,
            $"{covered} of 256 opcodes are uniquely encodable on {typeof(TVariant).Name}; " +
            $"this gate ran {expectedCovered} the last time it was reviewed. If the table " +
            $"gained or lost an opcode that is expected — update the number. If nothing " +
            $"changed in the table, the renderer has started collapsing opcodes onto each " +
            $"other and the coverage above is what was silently given up.");

        return (source.ToString(), ram[Origin..cursor]);
    }

    /// <summary>
    /// Opcodes that cannot come back as themselves, because at least one other opcode of
    /// this variant renders as exactly the same text and an assembler must choose between
    /// them. Decided by rendering, not by addressing mode: the 65C02's two three-byte
    /// <c>NOP</c> shapes are different modes that read identically.
    /// </summary>
    /// <remarks>
    /// The widths are the ones the listing will be built at, not defaults: an immediate's text
    /// is <c>#$34</c> at eight bits and <c>#$1234</c> at sixteen, so a set computed at one
    /// width and used at another would be answering a different question. §15.3 measured that
    /// the 65816's set is empty at all four regardless, which this passes the flags to keep
    /// true rather than to assume.
    /// </remarks>
    private static HashSet<int> AmbiguousOpcodes<TVariant>(bool wideAccumulator, bool wideIndex)
        where TVariant : struct, ICpuVariant
    {
        var ram = new byte[0x10000];
        var byText = new Dictionary<string, List<int>>();

        for (var opcode = 0; opcode < 256; opcode++)
        {
            ram[Origin] = (byte)opcode;
            ram[Origin + 1] = OperandLo;
            ram[Origin + 2] = OperandHi;
            ram[Origin + 3] = OperandBank;

            var text = Disassembler
                .Decode<FlatBus, TVariant>(new FlatBus(ram), Origin, wideAccumulator, wideIndex)
                .ToString();
            if (!byText.TryGetValue(text, out var sharing)) byText[text] = sharing = [];
            sharing.Add(opcode);
        }

        return [.. byText.Values.Where(s => s.Count > 1).SelectMany(s => s)];
    }

    /// <summary>
    /// Rewrites what a reader wants into what 64tass accepts. The bit number is part of the
    /// mnemonic in this project's tables and in every 65xx datasheet — <c>RMB0</c>,
    /// <c>BBS7</c> — but 64tass takes it as a leading operand and rejects the fused form
    /// outright. The encodings are identical, so this is a spelling, and it belongs here
    /// rather than in the library. <see cref="WidthPrefix"/> is here for the same reason.
    /// </summary>
    private static string ForAssembler(Instruction instruction)
    {
        var mnemonic = instruction.Mnemonic;
        var operand = WidthPrefix(instruction) + instruction.Operand;

        if (mnemonic.Length == 4 && char.IsAsciiDigit(mnemonic[3]) &&
            mnemonic[..3] is "RMB" or "SMB" or "BBR" or "BBS")
        {
            return $"{mnemonic[..3].ToLowerInvariant()} {mnemonic[3]},{operand}";
        }

        return operand.Length == 0 ? mnemonic : $"{mnemonic} {operand}";
    }

    /// <summary>
    /// The width 64tass has to be told, or the empty string when the shortest encoding it
    /// would pick is already the one that was decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 64tass emits the shortest encoding that fits the value, so a three-byte
    /// <c>LDA $0012</c> comes back as the two-byte direct-page <c>a5 12</c> — a different
    /// instruction, and on a linear listing every byte after it shifts. Research §15.4
    /// counted 53 such opcodes on each NMOS part and 42 on each 65C02. <c>@w</c> and
    /// <c>@l</c> say "this width, not the shorter one that also fits". They are 64tass's
    /// spelling of it — ca65 writes <c>a:</c> — which is exactly why they live in this
    /// harness and not in <see cref="Instruction.Operand"/>.
    /// </para>
    /// <para>
    /// Decided from the rendered text rather than from an addressing mode, which is what
    /// makes it cheap and what makes it total: an absolute operand always renders as exactly
    /// four hex digits and a long one as six, so a leading <c>$00</c> is precisely the case
    /// that collapses. Every instruction the disassembler emits passes through here,
    /// including the 65C02's three-byte <c>NOP</c> shapes, which a mode-aware rule inside
    /// the library had missed. Direct-page and immediate operands are two digits and
    /// branches are two bytes, so neither can match; <c>BBR0 $00,$1234</c> cannot either,
    /// because the four characters after its <c>$</c> are not all hex.
    /// </para>
    /// <para>
    /// §15.2 measured that 64tass also decides by mnemonic — <c>LDX $0012,Y</c> collapses
    /// where <c>LDA $0012,Y</c> does not, and the jumps never collapse — so this over-forces
    /// on a few opcodes. That was measured to be a no-op: a redundant prefix assembles to
    /// the same bytes, and the alternative is a second opcode-shaped table to keep in step.
    /// </para>
    /// </remarks>
    private static string WidthPrefix(Instruction instruction) => instruction.Length switch
    {
        3 when TopByteIsZero(instruction.Operand, 4) => "@w ",

        // The 65816's four-byte long modes are the only callers, and only below $010000:
        // AbsoluteOperandsBelowTheCollapseBoundary_RoundTrip's $6F and $7F reach it, where
        // 64tass collapses an unforced ADC $000012 to the two-byte direct-page 65 12.
        4 when TopByteIsZero(instruction.Operand, 6) => "@l ",
        _ => "",
    };

    /// <summary>
    /// True when the operand's address is <c>$</c> followed by exactly <paramref name="digits"/>
    /// hex digits of which the top two are zero. Skips one opening parenthesis or bracket so
    /// the indirect forms — <c>($0012)</c>, <c>($0012,X)</c>, and the 65816's long
    /// <c>[$0012]</c> — read the same as the plain ones; the <c>,X</c> and <c>,Y</c> suffixes
    /// fall out of the exact digit count.
    /// </summary>
    private static bool TopByteIsZero(string operand, int digits)
    {
        var text = operand.AsSpan(operand.StartsWith('(') || operand.StartsWith('[') ? 1 : 0);

        // `$` then exactly this many hex digits. One more would be a longer operand entirely,
        // and one fewer a shorter one — either way not the width this prefix is about.
        if (text.Length < digits + 1 || text[0] != '$') return false;
        if (text.Length > digits + 1 && char.IsAsciiHexDigit(text[digits + 1])) return false;

        for (var i = 1; i <= digits; i++)
        {
            if (!char.IsAsciiHexDigit(text[i])) return false;
        }

        return text[1] == '0' && text[2] == '0';
    }

    /// <summary>Assembles source text and returns the raw bytes, with no load address.</summary>
    private static byte[] Assemble(string source, string cpu)
    {
        var directory = Directory.CreateTempSubdirectory("sixtyfivexx-roundtrip");
        try
        {
            var asm = Path.Combine(directory.FullName, "roundtrip.asm");
            var bin = Path.Combine(directory.FullName, "roundtrip.bin");
            File.WriteAllText(asm, source.Replace(CpuDirectivePlaceholder, cpu));

            var start = new ProcessStartInfo("64tass", ["--nostart", "-o", bin, asm])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start 64tass.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0 && File.Exists(bin),
                $"64tass rejected the disassembly.\n{stdout}\n{stderr}");

            return File.ReadAllBytes(bin);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "64tass is not on PATH. Install it with `brew install 64tass` or " +
                "`apt-get install 64tass`; it is already required to build the Klaus " +
                "interrupt test.", ex);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string ReplaceFirst(string text, string find, string replacement)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"The disassembly does not contain '{find}' to corrupt.");
        return text[..at] + replacement + text[(at + find.Length)..];
    }
}
