namespace SixtyFiveXX.Variants;

/// <summary>
/// The Rockwell 65C02: the CMOS baseline plus <c>RMB</c>, <c>SMB</c>, <c>BBR</c> and
/// <c>BBS</c>.
/// </summary>
/// <remarks>
/// Thirty-two opcodes that are NOPs on <see cref="Synertek65C02Variant"/> carry the bit
/// operations here. It has no <c>WAI</c> or <c>STP</c>; those are WDC's.
/// </remarks>
public readonly struct Rockwell65C02Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.Rockwell65C02;
}
