namespace SixtyFiveXX;

/// <summary>
/// One row of a variant's opcode table. This is the single source of truth from which
/// <c>MicroOpTable</c> derives cycle sequences and the disassembler derives text.
/// </summary>
/// <param name="Mnemonic">Three-letter assembler mnemonic, upper case.</param>
/// <param name="Mode">How the effective address is formed.</param>
/// <param name="Operation">What is done once the address is formed.</param>
/// <param name="Access">Whether the effective address is read, written, or both.</param>
/// <param name="Width">
/// Which flag decides the operand width on the 65816. Defaults to <see cref="SixtyFiveXX.Width.None"/>,
/// which is what every 8-bit core's table wants and why this is a defaulted parameter rather than a
/// required one — the five 8-bit opcode tables are left entirely untouched by its addition.
/// </param>
internal readonly record struct OpcodeInfo(
    string Mnemonic, AddrMode Mode, Op Operation, Access Access, Width Width = Width.None)
{
    /// <summary>An opcode this variant does not implement.</summary>
    public static readonly OpcodeInfo Undefined =
        new("???", AddrMode.Undefined, Op.Undefined, Access.None);
}
