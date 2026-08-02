namespace SixtyFiveXX.Variants;

/// <summary>
/// The WDC 65C02: Rockwell's instruction set plus <c>WAI</c> and <c>STP</c>.
/// </summary>
/// <remarks>
/// The fullest of the three sub-variants. <c>WAI</c> halts until an interrupt is signalled
/// and <c>STP</c> until reset; both are observable through <see cref="Cpu{TBus, TVariant}.IsWaiting"/>
/// and <see cref="Cpu{TBus, TVariant}.IsStopped"/>.
/// </remarks>
public readonly struct Wdc65C02Variant : ICpuVariant
{
    /// <inheritdoc />
    public static CpuVariant Variant => CpuVariant.Wdc65C02;
}
