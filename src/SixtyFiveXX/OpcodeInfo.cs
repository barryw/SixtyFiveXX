namespace SixtyFiveXX;

/// <summary>
/// One row of a variant's opcode table. This is the single source of truth from which
/// <c>MicroOpTable</c> derives cycle sequences and the disassembler derives text.
/// </summary>
/// <param name="Mnemonic">Three-letter assembler mnemonic, upper case.</param>
/// <param name="Mode">How the effective address is formed.</param>
/// <param name="Operation">What is done once the address is formed.</param>
/// <param name="Access">Whether the effective address is read, written, or both.</param>
internal readonly record struct OpcodeInfo(string Mnemonic, AddrMode Mode, Op Operation, Access Access)
{
    /// <summary>An opcode this variant does not implement.</summary>
    public static readonly OpcodeInfo Undefined =
        new("???", AddrMode.Undefined, Op.Undefined, Access.None);
}
