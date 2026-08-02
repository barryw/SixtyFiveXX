namespace SixtyFiveXX;

/// <summary>
/// Compile-time contract for a member of the 65xx family that <c>Cpu&lt;TBus&gt;</c> can be
/// built for. Implemented by a zero-size struct and consumed only as a generic type
/// parameter: every member below is <c>static abstract</c>, so it resolves at compile time
/// through the type parameter with no instance and no virtual dispatch. That is what keeps
/// the tick loop monomorphic once <c>Cpu</c> itself becomes generic over the variant.
/// </summary>
internal interface ICpuVariant
{
    /// <summary>This variant's opcode table. Always 256 entries, one per opcode byte.</summary>
    static abstract OpcodeInfo[] OpcodeTable { get; }
}
