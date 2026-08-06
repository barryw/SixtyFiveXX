using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

public class MicroOpTableTests
{
    private static readonly MicroOpTable Table = MicroOpTable.For<Mos6502Variant>();

    // Sequence length excludes the opcode-fetch cycle, so the instruction's minimum
    // cycle count is SequenceLength + 1.
    [Theory]
    [InlineData(0xEA, 1)]  // NOP implied            = 2 cycles
    [InlineData(0xA9, 1)]  // LDA #imm               = 2
    [InlineData(0xA5, 2)]  // LDA zp                 = 3
    [InlineData(0xB5, 3)]  // LDA zp,X               = 4
    [InlineData(0xAD, 3)]  // LDA abs                = 4
    [InlineData(0xBD, 4)]  // LDA abs,X              = 4 or 5
    [InlineData(0x9D, 4)]  // STA abs,X              = 5 always
    [InlineData(0xA1, 5)]  // LDA (zp,X)             = 6
    [InlineData(0xB1, 5)]  // LDA (zp),Y             = 5 or 6
    [InlineData(0x91, 5)]  // STA (zp),Y             = 6 always
    [InlineData(0x16, 5)]  // ASL zp,X               = 6
    [InlineData(0x81, 5)]  // STA (zp,X)             = 6
    [InlineData(0x85, 2)]  // STA zp                 = 3 cycles
    [InlineData(0x8D, 3)]  // STA abs                = 4
    [InlineData(0x95, 3)]  // STA zp,X               = 4
    [InlineData(0x96, 3)]  // STX zp,Y               = 4
    [InlineData(0x99, 4)]  // STA abs,Y              = 5 always
    [InlineData(0xB6, 3)]  // LDX zp,Y               = 4
    [InlineData(0xB9, 4)]  // LDA abs,Y              = 4 or 5
    [InlineData(0xE6, 4)]  // INC zp                 = 5
    [InlineData(0xEE, 5)]  // INC abs                = 6
    [InlineData(0xFE, 6)]  // INC abs,X              = 7 always
    [InlineData(0x4C, 2)]  // JMP abs                = 3
    [InlineData(0x6C, 4)]  // JMP (ind)              = 5
    [InlineData(0x20, 5)]  // JSR                    = 6
    [InlineData(0x60, 5)]  // RTS                    = 6
    [InlineData(0x40, 5)]  // RTI                    = 6
    [InlineData(0x00, 6)]  // BRK                    = 7
    [InlineData(0x48, 2)]  // PHA                    = 3
    [InlineData(0x68, 3)]  // PLA                    = 4
    [InlineData(0xD0, 3)]  // BNE                    = 2, 3 or 4
    public void SequenceLength_MatchesTheDatasheet(int opcode, int expected)
    {
        Assert.Equal(expected, Table.SequenceLength(opcode));
    }

    [Fact]
    public void EverySequence_IsTerminatedByEnd()
    {
        for (var opcode = 0; opcode < 256; opcode++)
        {
            if (Table.Info[opcode].Operation == Op.Undefined) continue;
            var start = Table.Entry[opcode];
            var i = start;
            while (Table.Ops[i] != MicroOp.End) i++;
            Assert.True(i > start, $"Opcode ${opcode:X2} has an empty sequence.");
        }
    }

    [Fact]
    public void LdaAbsolute_ExpandsToTheExpectedMicroOps()
    {
        var start = Table.Entry[0xAD];

        Assert.Equal(MicroOp.FetchAddrLo, Table.Ops[start]);
        Assert.Equal(MicroOp.FetchAddrHi, Table.Ops[start + 1]);
        Assert.Equal(MicroOp.ReadExec,    Table.Ops[start + 2]);
        Assert.Equal(MicroOp.End,         Table.Ops[start + 3]);
    }

    [Fact]
    public void IrqSequence_IsSixMicroOpsLong()
    {
        var i = Table.IrqEntry;
        var count = 0;
        while (Table.Ops[i + count] != MicroOp.End) count++;
        Assert.Equal(6, count);   // + the boundary cycle = 7
    }

    /// <summary>
    /// Every variant's table defines all 256 opcodes. Replaces the probe test that fetched the
    /// first undefined byte and asserted it threw — a test with no probe left once the 65816
    /// reached 256 of 256, and a weaker one in any case: this fails for a hole in ANY table,
    /// not only the one variant that happened to have one.
    /// </summary>
    [Fact]
    public void EveryVariantDefinesAll256Opcodes()
    {
        AssertTableIsFull(MicroOpTable.For<Mos6502Variant>(), "6502");
        AssertTableIsFull(MicroOpTable.For<Mos6510Variant>(), "6510");
        AssertTableIsFull(MicroOpTable.For<Synertek65C02Variant>(), "Synertek 65C02");
        AssertTableIsFull(MicroOpTable.For<Rockwell65C02Variant>(), "Rockwell 65C02");
        AssertTableIsFull(MicroOpTable.For<Wdc65C02Variant>(), "WDC 65C02");
        AssertTableIsFull(MicroOpTable.For<W65C816Variant>(), "65816");

        static void AssertTableIsFull(MicroOpTable table, string name)
        {
            for (var opcode = 0; opcode < 256; opcode++)
            {
                Assert.True(table.Info[opcode].Operation != Op.Undefined,
                    $"{name}: ${opcode:X2} is undefined.");
            }
        }
    }
}
