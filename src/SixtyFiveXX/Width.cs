namespace SixtyFiveXX;

/// <summary>
/// Which status flag decides an instruction's operand width on the 65816.
/// </summary>
/// <remarks>
/// The 65816 sizes an access from one of two independent flags: <c>m</c> for anything that
/// moves through the accumulator, <c>x</c> for anything that moves through an index register.
/// Which one applies is a fixed property of the instruction, so it is resolved once per
/// instruction in <c>Cpu.FetchOpcode</c> rather than tested by each access micro-op.
/// <para>
/// <see cref="None"/> is a real value, not a placeholder: <c>XCE</c> has no operand at all, and
/// <c>REP</c>/<c>SEP</c> take a fixed 8-bit one whose width no flag can change (datasheet Note
/// 1). Most of phase 7d's control-flow and stack instructions will be <see cref="None"/> too.
/// An opcode carrying <see cref="None"/> that nevertheless reaches a width-deciding micro-op is
/// a table bug; <c>W65C816WidthTests</c> is where that is caught.
/// </para>
/// <para>
/// The five 8-bit cores never set this — their tables predate it and take the parameter's
/// default — and never read it: <c>_wide</c> is assigned only under a compile-time variant
/// guard, so for them it is permanently <see langword="false"/>.
/// </para>
/// </remarks>
internal enum Width : byte
{
    /// <summary>No flag-dependent operand width. The default.</summary>
    None,

    /// <summary>Width comes from the <c>m</c> flag: the accumulator and memory operations.</summary>
    M,

    /// <summary>Width comes from the <c>x</c> flag: the index-register operations.</summary>
    X,
}
