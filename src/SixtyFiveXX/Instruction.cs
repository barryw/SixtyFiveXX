namespace SixtyFiveXX;

/// <summary>
/// One decoded instruction: what it is called, what follows the mnemonic, and how many
/// bytes it occupies.
/// </summary>
/// <remarks>
/// Deliberately strings and an integer rather than anything from the opcode table. The
/// descriptors stay internal so a variant can reshape them without it being public API,
/// and a caller that wanted to switch on an addressing mode would be building a second
/// source of truth — which is the thing this whole design exists to prevent.
/// </remarks>
/// <param name="Mnemonic">
/// Upper case, and exact for the variant that decoded it: <c>LDA</c>, but also
/// <c>RMB0</c>, <c>BBS3</c>, <c>WAI</c>, <c>JAM</c>, and <c>???</c> for an opcode the
/// variant does not implement.
/// </param>
/// <param name="Operand">
/// Everything after the mnemonic, in the usual 6502 notation — <c>#$0F</c>, <c>$1234,X</c>,
/// <c>($12),Y</c>, <c>A</c> — and empty for an implied instruction. Branches are shown as
/// the address they land on rather than the displacement they encode.
/// </param>
/// <param name="Length">
/// Bytes consumed, 1 to 4. This is what the <em>processor</em> consumes, which is why
/// <c>BRK</c> is 2: the byte after the opcode is fetched and discarded, and a caller walking
/// memory by <see cref="Length"/> has to skip it the same way. Four occurs only on the
/// 65816 — <c>LDA $123456,X</c> and <c>JSL</c> — and on that part the length of an immediate
/// instruction also depends on the width flags passed to
/// <see cref="Disassembler.Decode{TBus, TVariant}(in TBus, int, bool, bool)"/>.
/// </param>
public readonly record struct Instruction(string Mnemonic, string Operand, int Length)
{
    /// <summary>
    /// The mnemonic and operand joined by a space, or just the mnemonic when there is no
    /// operand. The address and any register decoration are the caller's business.
    /// </summary>
    public override string ToString() =>
        Operand.Length == 0 ? Mnemonic : $"{Mnemonic} {Operand}";
}
