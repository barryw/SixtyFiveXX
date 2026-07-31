using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class Opcodes6502Tests
{
    [Fact]
    public void Table_HasTwoHundredFiftySixEntries()
    {
        Assert.Equal(256, Opcodes6502.Table.Length);
    }

    [Fact]
    public void Table_DefinesTheExpectedNumberOfOpcodes()
    {
        var legal = Opcodes6502.Table.Count(e => e.Operation != Op.Undefined);
        Assert.Equal(178, legal);
    }

    [Theory]
    [InlineData(0xA9, "LDA", AddrMode.Immediate, Op.Lda, Access.Read)]
    [InlineData(0xAD, "LDA", AddrMode.Absolute, Op.Lda, Access.Read)]
    [InlineData(0xBD, "LDA", AddrMode.AbsoluteX, Op.Lda, Access.Read)]
    [InlineData(0x8D, "STA", AddrMode.Absolute, Op.Sta, Access.Write)]
    [InlineData(0x9D, "STA", AddrMode.AbsoluteX, Op.Sta, Access.Write)]
    [InlineData(0xEE, "INC", AddrMode.Absolute, Op.Inc, Access.ReadModifyWrite)]
    [InlineData(0x0A, "ASL", AddrMode.Accumulator, Op.AslA, Access.None)]
    [InlineData(0x20, "JSR", AddrMode.Stack, Op.Jsr, Access.None)]
    [InlineData(0x6C, "JMP", AddrMode.Indirect, Op.Jmp, Access.None)]
    [InlineData(0xD0, "BNE", AddrMode.Relative, Op.Bne, Access.None)]
    [InlineData(0xEA, "NOP", AddrMode.Implied, Op.Nop, Access.None)]
    internal void Table_DescribesKnownOpcodes(int opcode, string mnemonic, AddrMode mode, Op op, Access access)
    {
        var entry = Opcodes6502.Table[opcode];

        Assert.Equal(mnemonic, entry.Mnemonic);
        Assert.Equal(mode, entry.Mode);
        Assert.Equal(op, entry.Operation);
        Assert.Equal(access, entry.Access);
    }

    [Fact]
    public void Table_MarksUndocumentedOpcodesAsUndefined()
    {
        // $02 is one of the NMOS JAM opcodes; Phase 1 does not implement it.
        Assert.Equal(Op.Undefined, Opcodes6502.Table[0x02].Operation);
        Assert.Equal(AddrMode.Undefined, Opcodes6502.Table[0x02].Mode);
    }
}
