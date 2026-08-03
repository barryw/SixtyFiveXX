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

    private void RoundTrip<TVariant>(string cpu, int expectedCovered) where TVariant : struct, ICpuVariant
    {
        var (source, expected) = Build<TVariant>(expectedCovered);
        var actual = Assemble(source, cpu);

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
    private (string Source, byte[] Expected) Build<TVariant>(int expectedCovered)
        where TVariant : struct, ICpuVariant
    {
        var ambiguous = AmbiguousOpcodes<TVariant>();

        var ram = new byte[0x10000];
        var cursor = Origin;
        for (var opcode = 0; opcode < 256; opcode++)
        {
            if (ambiguous.Contains(opcode)) continue;

            ram[cursor] = (byte)opcode;
            ram[cursor + 1] = OperandLo;
            ram[cursor + 2] = OperandHi;
            cursor += Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), cursor).Length;
        }

        var source = new StringBuilder()
            .AppendLine($"\t.cpu \"{CpuDirectivePlaceholder}\"")
            .AppendLine($"\t*=${Origin:X4}");

        // Disassemble what was just laid out, rather than reusing the loop above: the text
        // under test has to come from a linear walk, which is how a caller would use it.
        for (var address = Origin; address < cursor;)
        {
            var decoded = Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), address);
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
    private static HashSet<int> AmbiguousOpcodes<TVariant>() where TVariant : struct, ICpuVariant
    {
        var ram = new byte[0x10000];
        var byText = new Dictionary<string, List<int>>();

        for (var opcode = 0; opcode < 256; opcode++)
        {
            ram[Origin] = (byte)opcode;
            ram[Origin + 1] = OperandLo;
            ram[Origin + 2] = OperandHi;

            var text = Disassembler.Decode<FlatBus, TVariant>(new FlatBus(ram), Origin).ToString();
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
    /// rather than in the library.
    /// </summary>
    private static string ForAssembler(Instruction instruction)
    {
        var mnemonic = instruction.Mnemonic;

        if (mnemonic.Length == 4 && char.IsAsciiDigit(mnemonic[3]) &&
            mnemonic[..3] is "RMB" or "SMB" or "BBR" or "BBS")
        {
            return $"{mnemonic[..3].ToLowerInvariant()} {mnemonic[3]},{instruction.Operand}";
        }

        return instruction.ToString();
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

    /// <summary>
    /// Stands in for the <c>.cpu</c> name until <see cref="Assemble"/> substitutes it, so
    /// that the corruption test can reassemble the same source under a different dialect.
    /// </summary>
    private const string CpuDirectivePlaceholder = "%CPU%";

    private static string ReplaceFirst(string text, string find, string replacement)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"The disassembly does not contain '{find}' to corrupt.");
        return text[..at] + replacement + text[(at + find.Length)..];
    }
}
