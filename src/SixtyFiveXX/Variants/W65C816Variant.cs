namespace SixtyFiveXX.Variants;

/// <summary>
/// The WDC 65C816: 16-bit registers, 24-bit addressing, and the emulation/native mode
/// switch. Name this type as <c>TVariant</c> to build a <see cref="Cpu{TBus, TVariant}"/>
/// that models it.
/// </summary>
/// <remarks>
/// Built up across phase 7b. This task lands the variant itself, an opcode table with only
/// its first 32 opcodes defined, and reset — every other opcode throws
/// <see cref="UndefinedOpcodeException"/> until a later task in the phase fills it in.
/// </remarks>
public readonly struct W65C816Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.W65C816;
}
