namespace SixtyFiveXX.Variants;

/// <summary>
/// The NMOS MOS 6502: the 151 documented opcodes plus the 105 undocumented NMOS opcodes
/// modelled internally by this package. Name this type as <c>TVariant</c> to build a
/// <see cref="Cpu{TBus, TVariant}"/> that models the plain NMOS 6502.
/// </summary>
public readonly struct Mos6502Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.Mos6502;
}
