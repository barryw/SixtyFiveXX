namespace SixtyFiveXX.Variants;

/// <summary>
/// The NMOS MOS 6502: the 151 documented opcodes plus the 105 undocumented NMOS opcodes
/// modelled in <see cref="Opcodes6502"/>.
/// </summary>
internal readonly struct Mos6502Variant : ICpuVariant
{
    /// <inheritdoc />
    public static OpcodeInfo[] OpcodeTable => Opcodes6502.Table;
}
