namespace SixtyFiveXX;

/// <summary>How an instruction computes its effective address.</summary>
internal enum AddrMode : byte
{
    /// <summary>No operand. Two cycles, with a dummy read at PC.</summary>
    Implied,

    /// <summary>Operates on the accumulator. Cycle-identical to <see cref="Implied"/>.</summary>
    Accumulator,

    /// <summary>The operand byte follows the opcode.</summary>
    Immediate,

    /// <summary>One-byte address in page zero.</summary>
    ZeroPage,

    /// <summary>Page zero, indexed by X, wrapping within the page.</summary>
    ZeroPageX,

    /// <summary>Page zero, indexed by Y, wrapping within the page.</summary>
    ZeroPageY,

    /// <summary>Two-byte little-endian address.</summary>
    Absolute,

    /// <summary>Absolute, indexed by X. Reads may cost an extra cycle on a page cross.</summary>
    AbsoluteX,

    /// <summary>Absolute, indexed by Y. Reads may cost an extra cycle on a page cross.</summary>
    AbsoluteY,

    /// <summary>Indirect, used only by JMP. Reproduces the NMOS page-wrap bug.</summary>
    Indirect,

    /// <summary>
    /// Indirect JMP with the page-wrap bug fixed — the CMOS form, and a cycle longer than
    /// <see cref="Indirect"/>. A separate mode rather than a variant test at emission time,
    /// so the table stays the single source of truth for both timing and behaviour.
    /// </summary>
    IndirectFixed,

    /// <summary>(zp) — indirect through a page-zero pointer, with no indexing. CMOS only.</summary>
    ZeroPageIndirect,

    /// <summary>(abs,X) — absolute indexed indirect, used only by JMP. CMOS only.</summary>
    AbsoluteIndexedIndirect,

    /// <summary>(zp,X) — indexed indirect.</summary>
    IndexedIndirect,

    /// <summary>(zp),Y — indirect indexed. Reads may cost an extra cycle on a page cross.</summary>
    IndirectIndexed,

    /// <summary>Signed 8-bit branch displacement.</summary>
    Relative,

    /// <summary>Hand-written sequence: pushes, pulls, JSR, RTS, RTI, BRK, JMP absolute.</summary>
    Stack,

    /// <summary>
    /// Two operands: a page-zero address then a signed branch displacement. Used only by
    /// Rockwell's <c>BBR</c> and <c>BBS</c>, which read a byte, test one bit of it, and
    /// branch — the only 65xx addressing mode that both reads memory and branches.
    /// </summary>
    ZeroPageRelative,

    // The CMOS NOP timing shapes. Every opcode the NMOS parts left undocumented became a
    // NOP on the 65C02, but not a uniform one: the shapes below were measured from the
    // vectors and cover all 76 of them, alongside Implied (one, $CB), Immediate (seven),
    // ZeroPage and ZeroPageX (nine and twelve, which do perform their mode's read).

    /// <summary>
    /// One byte, one cycle: the opcode fetch and nothing else. Emits no micro-ops at all,
    /// so it is the only shape whose instruction is over before the sequence begins.
    /// </summary>
    NopSingleCycle,

    /// <summary>Three bytes, three cycles: both operand bytes fetched, no access performed.</summary>
    NopAbsolute,

    /// <summary>Three bytes, four cycles: as <see cref="NopAbsolute"/>, then a discarded re-read of the high operand byte.</summary>
    NopAbsoluteExtra,

    /// <summary>Not implemented by this variant.</summary>
    Undefined,
}
