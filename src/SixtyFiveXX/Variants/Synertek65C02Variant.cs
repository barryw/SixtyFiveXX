namespace SixtyFiveXX.Variants;

/// <summary>
/// The Synertek 65C02: the CMOS baseline instruction set with no bit operations.
/// </summary>
/// <remarks>
/// The smallest of the three 65C02 sub-variants. It has neither Rockwell's
/// <c>RMB</c>/<c>SMB</c>/<c>BBR</c>/<c>BBS</c> nor WDC's <c>WAI</c>/<c>STP</c>, so the
/// opcodes those occupy are NOPs here. Name this type as <c>TVariant</c> to build a
/// <see cref="Cpu{TBus, TVariant}"/> that models it.
/// </remarks>
public readonly struct Synertek65C02Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.Synertek65C02;
}
