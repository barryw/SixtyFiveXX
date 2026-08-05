using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The operand-width mechanism: which flag an opcode's width comes from, and the table
/// integrity that keeps the two in step as opcodes are added.
/// </summary>
public class W65C816WidthTests
{
    /// <summary>
    /// The tripwire for every remaining task in this phase. An opcode whose sequence contains
    /// one of the three width-deciding micro-ops MUST declare a <c>Width</c>, or it silently
    /// takes the 8-bit path for every operand regardless of <c>m</c> and <c>x</c>; an opcode
    /// that declares one but never reaches a deciding micro-op is dead data that will mislead
    /// the next reader. Asserted as set equality rather than one direction, so both mistakes
    /// fail here — in a sub-second unit run — instead of inside a 20,000-vector file.
    /// </summary>
    [Fact]
    public void WidthIsDeclaredExactlyForOpcodesThatDecideAnOperandWidth()
    {
        var table = MicroOpTable.For<W65C816Variant>();

        for (var opcode = 0; opcode < 256; opcode++)
        {
            var decides = false;
            for (var i = table.Entry[opcode]; table.Ops[i] != MicroOp.End; i++)
            {
                if (table.Ops[i] is MicroOp.ReadExec816 or MicroOp.ExecWrite816 or MicroOp.ImmExec816)
                    decides = true;
            }

            var declared = table.Info[opcode].Width != Width.None;

            Assert.True(decides == declared,
                $"${opcode:X2} {table.Info[opcode].Mnemonic}: reaches a width-deciding micro-op = " +
                $"{decides}, declares a Width = {declared}. These must agree.");
        }
    }
}
