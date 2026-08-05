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
    /// <para>
    /// Limitation: the list of deciding micro-ops below is hard-coded. If a later task adds
    /// another one, this test's coverage shrinks silently until the list is updated to match —
    /// nothing here detects that a new deciding micro-op exists. It did catch the addition
    /// itself, though: phase 7c′ task 2's <see cref="MicroOp.RmwRead816"/> was the fourth, and
    /// this test failed on <c>$06 ASL</c> — "declares a Width = True" with no deciding micro-op
    /// reached — the moment the read-modify-write opcodes landed, because the list did not yet
    /// name it.
    /// </para>
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
                // RmwRead816 is the read-modify-write tail's entry point and the only one of its
                // six slots that every execution reaches, so it is the RMW family's deciding
                // micro-op; the other five are downstream of the branch it takes on _wide.
                if (table.Ops[i] is MicroOp.ReadExec816 or MicroOp.ExecWrite816 or MicroOp.ImmExec816
                    or MicroOp.RmwRead816)
                    decides = true;
            }

            var declared = table.Info[opcode].Width != Width.None;

            Assert.True(decides == declared,
                $"${opcode:X2} {table.Info[opcode].Mnemonic}: reaches a width-deciding micro-op = " +
                $"{decides}, declares a Width = {declared}. These must agree.");
        }
    }

    /// <summary>
    /// The previous test only checks that an opcode declares <i>a</i> <see cref="Width"/>, not
    /// <i>which</i> one — so a single wrong <c>Set(...)</c> line inside a run of near-identical
    /// calls, e.g. an <c>LDX</c> form mis-declared <see cref="Width.M"/>, would pass it and be
    /// caught only by a 20,000-vector conformance file. Every opcode that shares an <see cref="Op"/>
    /// must declare the same <see cref="Width"/> as every other opcode for that operation — all
    /// fifteen <c>LDA</c> forms agree with each other, all five <c>LDX</c> forms agree with each
    /// other, and so on — so this closes the realistic version of that gap without merely
    /// restating the table.
    /// </summary>
    [Fact]
    public void OpcodesSharingAnOperationAgreeOnWidth()
    {
        var table = MicroOpTable.For<W65C816Variant>();

        var groups = Enumerable.Range(0, 256)
            .Select(opcode => (Opcode: opcode, Info: table.Info[opcode]))
            .Where(x => x.Info.Operation != Op.Undefined)
            .GroupBy(x => x.Info.Operation);

        foreach (var group in groups)
        {
            var widths = group.Select(x => x.Info.Width).Distinct().ToList();
            if (widths.Count <= 1) continue;

            var detail = string.Join(", ", group.Select(x => $"${x.Opcode:X2}={x.Info.Width}"));
            Assert.Fail($"{group.Key}: opcodes disagree on Width — {detail}");
        }
    }
}
