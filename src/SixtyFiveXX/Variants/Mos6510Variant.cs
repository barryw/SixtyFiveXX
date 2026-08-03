namespace SixtyFiveXX.Variants;

/// <summary>
/// The MOS 6510: a 6502 with the on-chip <c>$00</c> data-direction and <c>$01</c> I/O port
/// registers.
/// </summary>
/// <remarks>
/// The instruction set is the 6502's, undocumented opcodes included — the two cores share
/// one opcode table and one set of micro-op sequences. The only difference is that two
/// addresses never reach the bus, because the CPU answers them itself.
/// </remarks>
public readonly struct Mos6510Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.Mos6510;
}
