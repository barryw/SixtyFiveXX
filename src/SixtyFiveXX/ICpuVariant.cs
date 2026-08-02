namespace SixtyFiveXX;

/// <summary>
/// Compile-time contract for a member of the 65xx family that <c>Cpu&lt;TBus, TVariant&gt;</c>
/// can be built for. Implemented by a zero-size struct and consumed only as a generic type
/// parameter: <see cref="Variant"/> is <c>static abstract</c>, so it resolves at compile time
/// through the type parameter with no instance and no virtual dispatch. That is what keeps
/// the tick loop monomorphic once <c>Cpu</c> itself becomes generic over the variant.
/// </summary>
public interface ICpuVariant
{
    /// <summary>
    /// Which member of the 65xx family this type models. A compile-time constant for every
    /// closed generic type that implements this interface, so code resolving per-variant
    /// data — see <c>MicroOpTable.For{TVariant}</c> — can switch on it once per variant
    /// rather than on every access.
    /// </summary>
    static abstract CpuVariant Variant { get; }
}
